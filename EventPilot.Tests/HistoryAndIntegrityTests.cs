using System.Net;
using System.Net.Http.Json;
using EventPilot.Application.Abstractions.Models;
using EventPilot.Application.Common.Models;
using EventPilot.Application.Registrations.Queries;
using EventPilot.Application.Registrations.Common.Exceptions;
using EventPilot.Domain.Enums;
using EventPilot.Domain.Entities;
using EventPilot.Infrastructure.Persistence;
using EventPilot.Infrastructure.Persistance;
using EventPilot.Infrastructure.Persistance.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EventPilot.Tests;

public sealed partial class RegistrationCapacityTests
{
    [Fact]
    public async Task History_is_private_paginated_and_preserves_deleted_and_cancelled_events()
    {
        var id = await Event(3); var other = await Event();
        await Register(id, 2); await Register(other, 2); await Register(id, 3);
        await using (var db = new EventPilotDbContext(options))
            await new EventRepository(db, new EfUnitOfWork(db)).DeleteEvent(id, 1, default);
        await using var api = Api();
        using var user = Client(api, 2); using var another = Client(api, 3); using var anonymous = Client(api);
        await AssertError(await anonymous.GetAsync("/api/users/me/registrations"), HttpStatusCode.Unauthorized);
        var history = (await user.GetFromJsonAsync<ApiResult<PagedResult<MyRegistrationDto>>>("/api/users/me/registrations"))!.Data!;
        Assert.Equal(2, history.TotalCount);
        var deleted = Assert.Single(history.Items, x => x.EventId == id);
        Assert.True(deleted.EventIsDeleted); Assert.Equal(RegistrationStatus.Cancelled, deleted.Status);
        Assert.Single((await another.GetFromJsonAsync<ApiResult<PagedResult<MyRegistrationDto>>>("/api/users/me/registrations"))!.Data!.Items);
        var page = (await user.GetFromJsonAsync<ApiResult<PagedResult<MyRegistrationDto>>>("/api/users/me/registrations?pageSize=1"))!.Data!;
        Assert.Single(page.Items); Assert.Equal(2, page.TotalPages);
        await AssertError(await user.GetAsync("/api/users/me/registrations?pageSize=101"), HttpStatusCode.BadRequest);
        Assert.Equal(HttpStatusCode.OK, (await user.DeleteAsync($"/api/events/{id}/registrations/me")).StatusCode);
    }

    [Fact]
    public async Task Event_cancellation_cancels_active_registrations_atomically()
    {
        var id = await Event(2); await Register(id, 2); await Register(id, 3);
        await using var db = new EventPilotDbContext(options);
        var dto = UpdatedEvent(); dto.Status = EventStatus.Cancelled;
        dto.RowVersion = await db.Events.Where(x => x.Id == id).Select(x => x.RowVersion).SingleAsync();
        await new EventRepository(db, new EfUnitOfWork(db)).UpdateAsync(id, 1, dto, default);
        Assert.All(await db.Registrations.Where(x => x.EventId == id).ToListAsync(), x => Assert.Equal(RegistrationStatus.Cancelled, x.Status));
        await Assert.ThrowsAsync<BusinessRuleException>(() => Register(id, 4));
    }

    [Fact]
    public async Task Registration_closes_when_event_starts()
    {
        var id = await Event();
        await using var db = new EventPilotDbContext(options);
        var ev = await db.Events.SingleAsync(x => x.Id == id);
        ev.StartAt = DateTime.UtcNow.AddMinutes(-1); ev.EndAt = DateTime.UtcNow.AddHours(1);
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<BusinessRuleException>(() => Register(id, 2));
    }

    [Theory]
    [InlineData("capacity")] [InlineData("price")] [InlineData("dates")] [InlineData("owner")]
    public async Task Database_rejects_invalid_event_data_even_without_application_validation(string field)
    {
        var id = await Event();
        await using var db = new EventPilotDbContext(options);
        var ev = await db.Events.SingleAsync(x => x.Id == id);
        switch (field)
        {
            case "capacity": ev.Capacity = 0; break;
            case "price": ev.Price = -1; break;
            case "dates": ev.EndAt = ev.StartAt; break;
            case "owner": ev.OrganizerUserId = 999999; break;
        }
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Database_schema_is_built_from_all_migrations_without_pending_model_changes()
    {
        await using var db = new EventPilotDbContext(options);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.False(db.Database.HasPendingModelChanges());
        var id = await Event();
        Assert.True((await db.Events.SingleAsync(x => x.Id == id)).CreatedAt > DateTime.UtcNow.AddMinutes(-1));
    }
}
