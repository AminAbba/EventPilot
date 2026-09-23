using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EventPilot.Application.Abstractions.Auth;
using EventPilot.Application.Common.Models;
using EventPilot.Application.Events.Dtos;
using EventPilot.Application.Users.Commands.BecomeOrganizer;
using EventPilot.Application.Users.Dtos;
using EventPilot.Domain.Entities;
using EventPilot.Infrastructure.Identity;
using EventPilot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EventPilot.Tests;

public sealed partial class RegistrationCapacityTests
{
    [Theory]
    [InlineData("/api/auth/register")]
    [InlineData("/api/users")]
    public async Task Signup_assigns_only_user_role_regardless_of_client_role_fields(string endpoint)
    {
        await using var api = AccountApi(new CapturedMail());
        using var client = Client(api);
        var response = await client.PostAsJsonAsync(endpoint, new
        {
            email = "account@example.com", password = "StrongPassword1!", firstName = "First", lastName = "Last",
            role = "Admin", roles = new[] { "Organizer", "Admin" }, isOrganizer = true
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Authorize(client, (await LoginAccount(client)).AccessToken!);
        var me = (await client.GetFromJsonAsync<ApiResult<MeDto>>("/api/users/me"))!.Data!;
        Assert.Equal([AppRoles.User], me.Roles);
        await AssertError(await client.GetAsync("/api/events/mine"), HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task User_role_cannot_manage_events_but_can_register_and_cancel()
    {
        var id = await Event(2);
        await using var api = Api();
        using var attendee = Client(api, 3);
        using var anonymous = Client(api);
        await AssertError(await anonymous.PostAsync("/api/users/me/organizer", null), HttpStatusCode.Unauthorized);
        await AssertError(await attendee.PostAsJsonAsync("/api/events", new { }), HttpStatusCode.Forbidden);
        await AssertError(await attendee.PutAsJsonAsync("/api/events", new { }), HttpStatusCode.Forbidden);
        await AssertError(await attendee.DeleteAsync($"/api/events/{id}"), HttpStatusCode.Forbidden);
        await AssertError(await attendee.GetAsync("/api/events/mine"), HttpStatusCode.Forbidden);
        await AssertError(await attendee.GetAsync($"/api/events/{id}/registrations"), HttpStatusCode.Forbidden);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/api/events")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await attendee.PostAsync($"/api/events/{id}/registrations", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await attendee.GetAsync("/api/users/me/registrations")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await attendee.DeleteAsync($"/api/events/{id}/registrations/me")).StatusCode);
    }

    [Fact]
    public async Task Organizer_enrollment_is_self_only_revokes_all_sessions_and_retains_ownership_checks()
    {
        var foreignId = await Event();
        await using var api = AccountApi(new CapturedMail());
        using var client = Client(api);
        using var secondDevice = Client(api);
        await RegisterAccount(client);
        Authorize(client, (await LoginAccount(client)).AccessToken!);
        Authorize(secondDevice, (await LoginAccount(secondDevice)).AccessToken!);
        var enrolled = await client.PostAsJsonAsync("/api/users/me/organizer", new { userId = 3, role = "Admin" });
        Assert.Equal(HttpStatusCode.OK, enrolled.StatusCode);
        var result = (await enrolled.Content.ReadFromJsonAsync<ApiResult<OrganizerEnrollmentResult>>())!.Data!;
        Assert.True(result.RoleChanged); Assert.True(result.RequiresLogin);
        await AssertError(await client.GetAsync("/api/users/me"), HttpStatusCode.Unauthorized);
        await AssertError(await secondDevice.GetAsync("/api/users/me"), HttpStatusCode.Unauthorized);
        Authorize(client, (await LoginAccount(client)).AccessToken!);
        var me = (await client.GetFromJsonAsync<ApiResult<MeDto>>("/api/users/me"))!.Data!;
        Assert.Equal(new[] { AppRoles.Organizer, AppRoles.User }, me.Roles.Order());
        var repeated = (await (await client.PostAsync("/api/users/me/organizer", null))
            .Content.ReadFromJsonAsync<ApiResult<OrganizerEnrollmentResult>>())!.Data!;
        Assert.False(repeated.RoleChanged); Assert.False(repeated.RequiresLogin);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/users/me")).StatusCode);
        var created = await client.PostAsJsonAsync("/api/events", new { ev = new CreateEventDto
        {
            Title = "My event", Description = "Test", Location = "Paris", Capacity = 5,
            StartAt = DateTime.UtcNow.AddDays(2), EndAt = DateTime.UtcNow.AddDays(3), Status = EventStatus.Published
        } });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var ownedId = (await created.Content.ReadFromJsonAsync<ApiResult<Guid>>())!.Data;
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/events/{ownedId}/registrations")).StatusCode);
        Assert.Contains(ownedId.ToString(), await client.GetStringAsync("/api/events/mine"));
        await AssertError(await client.DeleteAsync($"/api/events/{foreignId}"), HttpStatusCode.Forbidden);
        await AssertError(await client.GetAsync($"/api/events/{foreignId}/registrations"), HttpStatusCode.Forbidden);
        var foreign = (await client.GetFromJsonAsync<ApiResult<EventDto>>($"/api/events/{foreignId}"))!.Data!;
        var update = UpdatedEvent(); update.RowVersion = foreign.RowVersion;
        await AssertError(await client.PutAsJsonAsync("/api/events", new { eventId = foreignId, updateEventDto = update }), HttpStatusCode.Forbidden);
        using var untouched = Client(api, 3);
        Assert.Equal([AppRoles.User], (await untouched.GetFromJsonAsync<ApiResult<MeDto>>("/api/users/me"))!.Data!.Roles);
    }

    [Fact]
    public async Task Removing_an_identity_role_immediately_rejects_stale_jwt_claims()
    {
        await using var api = Api();
        using var client = Client(api, 1);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/events/mine")).StatusCode);
        await using (var scope = api.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByIdAsync("1"))!;
            var stamp = user.SecurityStamp;
            Assert.True((await users.RemoveFromRoleAsync(user, AppRoles.Organizer)).Succeeded);
            Assert.Equal(stamp, user.SecurityStamp); // The security stamp is unchanged.
        }
        await AssertError(await client.GetAsync("/api/events/mine"), HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Concurrent_organizer_enrollment_never_duplicates_membership_or_leaves_old_tokens_active()
    {
        await using var api = AccountApi(new CapturedMail());
        using var client = Client(api);
        await RegisterAccount(client);
        var login = await LoginAccount(client);
        Authorize(client, login.AccessToken!);
        var responses = await Task.WhenAll(client.PostAsync("/api/users/me/organizer", null), client.PostAsync("/api/users/me/organizer", null));
        Assert.Contains(responses, x => x.StatusCode == HttpStatusCode.OK);
        Assert.All(responses, x => Assert.Contains(x.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict, HttpStatusCode.Unauthorized }));
        await AssertError(await client.GetAsync("/api/users/me"), HttpStatusCode.Unauthorized);
        await using var db = new EventPilotDbContext(options);
        var role = await db.Roles.SingleAsync(x => x.NormalizedName == "ORGANIZER");
        Assert.Equal(1, await db.UserRoles.CountAsync(x => x.UserId == login.UserId && x.RoleId == role.Id));
        Authorize(client, (await LoginAccount(client)).AccessToken!);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/events/mine")).StatusCode);
    }

    [Fact]
    public async Task Signup_rolls_back_account_if_default_role_assignment_fails()
    {
        await using (var db = new EventPilotDbContext(options))
        {
            var role = await db.Roles.SingleAsync(x => x.NormalizedName == "USER");
            await db.UserRoles.Where(x => x.RoleId == role.Id).ExecuteDeleteAsync();
            db.Roles.Remove(role); await db.SaveChangesAsync();
        }
        await using var api = AccountApi(new CapturedMail());
        using var client = Client(api);
        await AssertError(await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = "rollback@example.com", password = "StrongPassword1!", firstName = "Test", lastName = "User"
        }), HttpStatusCode.InternalServerError);
        await using var verify = new EventPilotDbContext(options);
        Assert.False(await verify.Users.AnyAsync(x => x.Email == "rollback@example.com"));
    }

    [Fact]
    public async Task Role_migration_preserves_existing_roles_and_grants_organizer_to_legacy_owners()
    {
        await using var current = new EventPilotDbContext(options);
        var connection = new SqlConnectionStringBuilder(current.Database.GetConnectionString()) { InitialCatalog = databaseName + "_roles" };
        var legacyOptions = new DbContextOptionsBuilder<EventPilotDbContext>().UseSqlServer(connection.ConnectionString).Options;
        await using var db = new EventPilotDbContext(legacyOptions);
        try
        {
            await db.GetService<IMigrator>().MigrateAsync("20260920144743_HardenMvpIntegrity");
            var existingRole = new IdentityRole<int> { Name = "ExistingRole", NormalizedName = "EXISTINGROLE" };
            db.Roles.Add(existingRole);
            var owner = new ApplicationUser { UserName = "owner", NormalizedUserName = "OWNER", FirstName = "Owner", LastName = "One", SecurityStamp = "before" };
            var attendee = new ApplicationUser { UserName = "attendee", NormalizedUserName = "ATTENDEE", FirstName = "User", LastName = "Two", SecurityStamp = "before" };
            db.Users.AddRange(owner, attendee); await db.SaveChangesAsync();
            db.UserRoles.Add(new IdentityUserRole<int> { UserId = attendee.Id, RoleId = existingRole.Id });
            db.Events.Add(new Event { Id = Guid.NewGuid(), OrganizerUserId = owner.Id, Title = "Old", Description = "Old", Location = "Paris",
                StartAt = DateTime.UtcNow.AddDays(-2), EndAt = DateTime.UtcNow.AddDays(-1), Capacity = 1, IsDeleted = true });
            await db.SaveChangesAsync();
            await db.Database.MigrateAsync(); db.ChangeTracker.Clear();
            var userRole = await db.Roles.SingleAsync(x => x.NormalizedName == "USER");
            var organizerRole = await db.Roles.SingleAsync(x => x.NormalizedName == "ORGANIZER");
            Assert.NotEqual(existingRole.Id, userRole.Id);
            Assert.Equal(2, await db.UserRoles.CountAsync(x => x.RoleId == userRole.Id));
            Assert.True(await db.UserRoles.AnyAsync(x => x.RoleId == organizerRole.Id && x.UserId == owner.Id));
            Assert.False(await db.UserRoles.AnyAsync(x => x.RoleId == organizerRole.Id && x.UserId == attendee.Id));
            Assert.True(await db.UserRoles.AnyAsync(x => x.RoleId == existingRole.Id && x.UserId == attendee.Id));
            var stamp = (await db.Users.SingleAsync(x => x.Id == owner.Id)).SecurityStamp;
            Assert.NotEqual("before", stamp);
            await db.Database.MigrateAsync(); db.ChangeTracker.Clear();
            Assert.Equal(stamp, (await db.Users.SingleAsync(x => x.Id == owner.Id)).SecurityStamp);
        }
        finally
        {
            Assert.Equal(databaseName + "_roles", db.Database.GetDbConnection().Database);
            await db.Database.EnsureDeletedAsync();
        }
    }
}
