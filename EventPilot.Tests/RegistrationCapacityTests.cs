using System.Data.Common;
using EventPilot.Application.Abstractions;
using EventPilot.Application.Abstractions.Auth;
using EventPilot.Application.Events.Dtos;
using EventPilot.Application.Registrations.Commands.CancelRegistration;
using EventPilot.Application.Registrations.Commands.CreateRegistration;
using EventPilot.Application.Registrations.Common.Exceptions;
using EventPilot.Application.Users.Dtos;
using EventPilot.Domain.Entities;
using EventPilot.Domain.Enums;
using EventPilot.Infrastructure.Identity;
using EventPilot.Infrastructure.Persistance;
using EventPilot.Infrastructure.Persistance.Repositories;
using EventPilot.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Xunit;

namespace EventPilot.Tests;

// Each test run uses a separate database.
public sealed partial class RegistrationCapacityTests : IAsyncLifetime
{
    private readonly string databaseName = "EventPilot_SeatTests_" + Guid.NewGuid().ToString("N");
    private DbContextOptions<EventPilotDbContext> options = null!;
    public async Task InitializeAsync()
    {
        var connection = new SqlConnectionStringBuilder(
            Environment.GetEnvironmentVariable("EVENTPILOT_TEST_SQL")
            ?? "Server=localhost;Integrated Security=true;TrustServerCertificate=true");
        connection.InitialCatalog = databaseName;
        options = new DbContextOptionsBuilder<EventPilotDbContext>().UseSqlServer(connection.ConnectionString).Options;
        await using var db = new EventPilotDbContext(options);
        await db.Database.MigrateAsync();
        db.Users.AddRange(Enumerable.Range(1, 12).Select(id => new ApplicationUser {
            UserName = $"user{id}", NormalizedUserName = $"USER{id}", SecurityStamp = "integration-test-stamp",
            Email = $"user{id}@example.com", FirstName = "User", LastName = id.ToString()
        }));
        // SQL Server assigns user IDs 1 through 12.

        await db.SaveChangesAsync();
        var userRole = await db.Roles.SingleAsync(x => x.NormalizedName == "USER");
        var organizerRole = await db.Roles.SingleAsync(x => x.NormalizedName == "ORGANIZER");
        foreach (var user in await db.Users.ToListAsync())
        {
            db.UserRoles.Add(new IdentityUserRole<int> { UserId = user.Id, RoleId = userRole.Id });
            if (user.Id <= 2)
                db.UserRoles.Add(new IdentityUserRole<int> { UserId = user.Id, RoleId = organizerRole.Id });
        }
        await db.SaveChangesAsync();
    }
    public async Task DisposeAsync()
    {
        await using var db = new EventPilotDbContext(options);
        Assert.Equal(databaseName, db.Database.GetDbConnection().Database);
        Assert.StartsWith("EventPilot_SeatTests_", databaseName);
        await db.Database.EnsureDeletedAsync();
    }
    private async Task<Guid> Event(int capacity = 1)
    {
        await using var db = new EventPilotDbContext(options);
        var ev = new Event { Id = Guid.NewGuid(), Title = "Test", Description = "Test", Location = "Test",
            Capacity = capacity, Status = EventStatus.Published, OrganizerUserId = 1,
            StartAt = DateTime.UtcNow.AddDays(1), EndAt = DateTime.UtcNow.AddDays(2) };
        db.Events.Add(ev);
        await db.SaveChangesAsync();
        return ev.Id;
    }
    private RegistrationSeatStore Store(DbCommandInterceptor? interceptor = null) =>
        new(new Factory(interceptor is null ? options :
            new DbContextOptionsBuilder<EventPilotDbContext>(options).AddInterceptors(interceptor).Options));
    private static ICurrentUser Current(int id) => new TestCurrentUser
    {
        UserId = id,
        Email = $"user{id}@example.com"
    };
    private static IIdentityService Identity(int id) => new TestIdentityService(id);
    private Task<EventPilot.Application.Registrations.Dtos.RegistrationResponse> Register(Guid id, int user,
        RegistrationSeatStore? store = null) =>
        new CreateRegistrationHandler(store ?? Store(), Identity(user), Current(user)).Handle(new(id), default);
    private Task<EventPilot.Application.Registrations.Dtos.RegistrationResponse> Cancel(Guid id, int user,
        RegistrationSeatStore? store = null) =>
        new CancelRegistrationHandler(store ?? Store(), Current(user)).Handle(new(id), default);

    [Fact]
    public async Task Two_requests_reading_same_version_cannot_take_last_seat()
    {
        var id = await Event();
        var gate = new FirstTwoWrites();
        var store = Store(gate);
        var outcomes = await Task.WhenAll(
            Record.ExceptionAsync(() => Register(id, 2, store)),
            Record.ExceptionAsync(() => Register(id, 3, store)));
        Assert.Single(outcomes, x => x is null);
        Assert.Single(outcomes, x => x is BusinessRuleException);
        Assert.True(gate.Writes >= 3); // The conflicting write retries.
        await using var db = new EventPilotDbContext(options);
        Assert.Equal(1, await db.Registrations.CountAsync(x => x.EventId == id && x.Status == RegistrationStatus.Confirmed));
        Assert.Equal(1, (await db.Events.SingleAsync(x => x.Id == id)).Capacity);
    }
    [Fact]
    public async Task Cancellation_is_idempotent_and_rejoin_reuses_registration()
    {
        var id = await Event();
        var first = await Register(id, 2);
        Assert.Equal(0, first.RemainingSeats);
        Assert.Equal(1, (await Cancel(id, 2)).RemainingSeats);
        Assert.Equal(1, (await Cancel(id, 2)).RemainingSeats);
        var rejoined = await Register(id, 2);
        Assert.Equal(first.RegistrationId, rejoined.RegistrationId);
        Assert.Equal(0, rejoined.RemainingSeats);
        await Assert.ThrowsAsync<AlreadyRegisteredException>(() => Register(id, 2));
    }
    [Fact]
    public async Task Cancelled_attendee_cannot_rejoin_when_someone_else_takes_seat()
    {
        var id = await Event();
        await Register(id, 2);
        await Cancel(id, 2);
        await Register(id, 3);
        await Assert.ThrowsAsync<BusinessRuleException>(() => Register(id, 2));
        await using var db = new EventPilotDbContext(options);
        Assert.Equal(RegistrationStatus.Cancelled, (await db.Registrations.SingleAsync(x => x.EventId == id && x.UserId == 2)).Status);
    }
    [Fact]
    public async Task Attendee_cannot_cancel_another_users_registration()
    {
        var id = await Event();
        await Register(id, 2);
        await Assert.ThrowsAsync<NotFoundException>(() => Cancel(id, 3));
        await using var db = new EventPilotDbContext(options);
        Assert.Equal(RegistrationStatus.Confirmed, (await db.Registrations.SingleAsync(x => x.EventId == id)).Status);
    }
    [Fact]
    public async Task Concurrent_cancellations_release_only_one_seat()
    {
        var id = await Event();
        await Register(id, 2);
        var store = Store(new FirstTwoWrites());
        var results = await Task.WhenAll(Cancel(id, 2, store), Cancel(id, 2, store));
        Assert.All(results, x => Assert.Equal(1, x.RemainingSeats));
        await Register(id, 3);
        await Assert.ThrowsAsync<BusinessRuleException>(() => Register(id, 4));
    }
    [Fact]
    public async Task Registration_bumps_version_and_rejects_stale_event_edit()
    {
        var id = await Event(2);
        await using var db = new EventPilotDbContext(options);
        var stale = await db.Events.SingleAsync(x => x.Id == id);
        await Register(id, 2);
        stale.Capacity = 1;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => db.SaveChangesAsync());
    }
    [Fact]
    public async Task Capacity_cannot_be_lowered_below_confirmed_count()
    {
        var id = await Event(2);
        await Register(id, 2);
        await Register(id, 3);
        await using var db = new EventPilotDbContext(options);
        var repository = new EventRepository(db, new EfUnitOfWork(db));
        var version = await db.Events.Where(x => x.Id == id).Select(x => x.RowVersion).SingleAsync();
        await Assert.ThrowsAsync<BusinessRuleException>(() => repository.UpdateAsync(id, 1,
            new UpdateEventDto { Title = "Test", Description = "Test", Location = "Test", Capacity = 1,
                RowVersion = version, StartAt = DateTime.UtcNow.AddDays(1), EndAt = DateTime.UtcNow.AddDays(2) }, default));
    }
    [Fact]
    public async Task Failed_write_rolls_back_event_version_and_insert()
    {
        var id = await Event();
        await using var db = new EventPilotDbContext(options);
        var before = await db.Events.AsNoTracking().SingleAsync(x => x.Id == id);
        await Assert.ThrowsAsync<DbUpdateException>(() => Register(id, 9999)); // Invalid user ID.
        var after = await db.Events.AsNoTracking().SingleAsync(x => x.Id == id);
        Assert.Equal(before.RowVersion, after.RowVersion);
        Assert.Empty(await db.Registrations.Where(x => x.EventId == id).ToListAsync());
        await Register(id, 2);
    }
    [Fact]
    public async Task Deleted_event_rejects_registration()
    {
        var id = await Event();
        await using var db = new EventPilotDbContext(options);
        var ev = await db.Events.SingleAsync(x => x.Id == id);
        ev.IsDeleted = true;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<NotFoundException>(() => Register(id, 2));
    }
    private sealed class Factory(DbContextOptions<EventPilotDbContext> options) : IDbContextFactory<EventPilotDbContext>
    {
        public EventPilotDbContext CreateDbContext() => new(options);
    }
    [Fact]
    public async Task Repeated_version_conflicts_stop_after_five_attempts_without_inserting()
    {
        var id = await Event();
        var conflicts = new ForceConflicts(options, id);
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Register(id, 2, Store(conflicts)));
        Assert.Contains("retry", error.Message);
        Assert.Equal(5, conflicts.Writes);
        await using var db = new EventPilotDbContext(options);
        Assert.Empty(await db.Registrations.Where(x => x.EventId == id).ToListAsync());
    }

    private sealed class ForceConflicts(DbContextOptions<EventPilotDbContext> options, Guid eventId) : DbCommandInterceptor
    {
        public int Writes;
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (command.CommandText.StartsWith("UPDATE") && command.CommandText.Contains("[Capacity]"))
            {
                Writes++;
                await using var other = new EventPilotDbContext(options);
                await other.Events.Where(x => x.Id == eventId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Capacity, x => x.Capacity), ct);
            }
            return result;
        }
    }
    private sealed class FirstTwoWrites : DbCommandInterceptor
    {
        private readonly TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Writes;
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (command.CommandText.StartsWith("UPDATE") && command.CommandText.Contains("[Capacity]"))
            {
                var number = Interlocked.Increment(ref Writes);
                if (number == 2) gate.TrySetResult();
                if (number <= 2) await gate.Task.WaitAsync(TimeSpan.FromSeconds(15), ct);
            }
            return result;
        }
    }
}

