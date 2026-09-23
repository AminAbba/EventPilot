using EventPilot.Application.Events.Dtos;
using EventPilot.Infrastructure.Persistance;
using EventPilot.Infrastructure.Persistance.Repositories;
using EventPilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EventPilot.Tests;

public sealed partial class RegistrationCapacityTests
{
    [Fact]
    public async Task Event_lookup_update_persists_changes_for_requested_id()
    {
        var id = await Event();
        var otherId = await Event();
        using var cancellation = new CancellationTokenSource();
        await using (var db = new EventPilotDbContext(options))
        {
            var repository = new EventRepository(db, new EfUnitOfWork(db));
            var dto = UpdatedEvent();
            dto.RowVersion = await db.Events.Where(x => x.Id == id).Select(x => x.RowVersion).SingleAsync();
            await repository.UpdateAsync(id, 1, dto, cancellation.Token);
        }

        await using var verify = new EventPilotDbContext(options);
        var updated = await verify.Events.SingleAsync(x => x.Id == id);
        Assert.Equal("Updated event", updated.Title);
        Assert.Equal(2, updated.Capacity);
        Assert.Equal("Test", (await verify.Events.SingleAsync(x => x.Id == otherId)).Title);
    }

    [Fact]
    public async Task Event_lookup_delete_soft_deletes_only_requested_id()
    {
        var id = await Event();
        var otherId = await Event();
        using var cancellation = new CancellationTokenSource();
        await using (var db = new EventPilotDbContext(options))
        {
            var repository = new EventRepository(db, new EfUnitOfWork(db));
            await repository.DeleteEvent(id, 1, cancellation.Token);
        }

        await using var verify = new EventPilotDbContext(options);
        Assert.False(await verify.Events.AnyAsync(x => x.Id == id));
        var deleted = await verify.Events.IgnoreQueryFilters().SingleAsync(x => x.Id == id);
        Assert.True(deleted.IsDeleted);
        Assert.NotNull(deleted.DeletedAt);
        Assert.True(await verify.Events.AnyAsync(x => x.Id == otherId));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Event_lookup_rejects_missing_or_soft_deleted_event(bool deleted, bool deleteOperation)
    {
        var id = deleted ? await Event() : Guid.NewGuid();
        await using var db = new EventPilotDbContext(options);
        if (deleted)
        {
            // The filter must also exclude tracked entities.
            var ev = await db.Events.SingleAsync(x => x.Id == id);
            ev.IsDeleted = true;
            ev.DeletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        var repository = new EventRepository(db, new EfUnitOfWork(db));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => deleteOperation
            ? repository.DeleteEvent(id, 1, default)
            : repository.UpdateAsync(id, 1, UpdatedEvent(), default));
        if (deleted)
        {
            var unchanged = await db.Events.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.Id == id);
            Assert.Equal("Test", unchanged.Title);
            Assert.True(unchanged.IsDeleted);
        }
    }

    private static UpdateEventDto UpdatedEvent() => new()
    {
        Title = "Updated event", Description = "Updated description", Location = "Updated location",
        Capacity = 2, Price = 10,
        StartAt = DateTime.UtcNow.AddDays(3), EndAt = DateTime.UtcNow.AddDays(4)
    };
}
