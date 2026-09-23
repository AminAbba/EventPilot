using EventPilot.Application.Events.Queries;
using EventPilot.Domain.Entities;
using EventPilot.Infrastructure.Persistance;
using EventPilot.Infrastructure.Persistance.Repositories;
using EventPilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EventPilot.Tests;

public sealed partial class RegistrationCapacityTests
{
    [Fact]
    public async Task Discovery_lists_published_events_across_organizers_and_excludes_hidden_events()
    {
        var ids = await SeedDiscoveryEvents();
        await Register(ids[0], 3);
        await Register(ids[0], 4);
        await Cancel(ids[0], 4);
        await using var db = new EventPilotDbContext(options);
        var handler = new ListEventQueryHandler(new EventRepository(db, new EfUnitOfWork(db)));
        var page = await handler.Handle(new(), default);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(ids.Take(2).Order(), page.Items.Select(x => x.Id).Order());
        var item = Assert.Single(page.Items, x => x.Id == ids[0]);
        Assert.Equal("Paris", item.Location);
        Assert.Equal(EventStatus.Published, item.Status);
        Assert.Equal(EventCategory.Business, item.Category);
        Assert.Equal(1, item.ConfirmedRegistrations);
        Assert.Equal(4, item.RemainingSeats);
        Assert.True(item.EndAt > item.StartAt);
    }

    [Fact]
    public async Task Discovery_mine_includes_own_drafts_and_cancelled_events_but_never_other_organizers()
    {
        var ids = await SeedDiscoveryEvents();
        await using var db = new EventPilotDbContext(options);
        var repository = new EventRepository(db, new EfUnitOfWork(db));
        var first = await new ListMyEventsQueryHandler(repository, Current(1)).Handle(new(), default);
        Assert.Equal(new[] { ids[0], ids[2], ids[3] }.Order(), first.Items.Select(x => x.Id).Order());
        var second = await new ListMyEventsQueryHandler(repository, Current(2)).Handle(new(), default);
        Assert.Equal(ids[1], Assert.Single(second.Items).Id);
        Assert.Empty((await new ListMyEventsQueryHandler(repository, Current(12)).Handle(new(), default)).Items);
    }

    [Fact]
    public async Task Discovery_search_price_and_pagination_preserve_visibility_and_totals()
    {
        var ids = await SeedDiscoveryEvents();
        await using var db = new EventPilotDbContext(options);
        var repository = new EventRepository(db, new EfUnitOfWork(db));
        var filters = new ListEventQuery(PageSize: 1, Search: "  Conference  ", Price: 10);
        var first = await repository.ListPublishedAsync(filters, default);
        var second = await repository.ListPublishedAsync(filters with { PageNumber = 2 }, default);
        Assert.Equal(2, first.TotalCount);
        Assert.Equal(2, first.TotalPages);
        Assert.Equal(ids.Take(2).Order(), first.Items.Concat(second.Items).Select(x => x.Id).Order());
        Assert.Empty((await repository.ListPublishedAsync(filters with { PageNumber = 3 }, default)).Items);
        Assert.Empty((await repository.ListPublishedAsync(filters with { Price = 0 }, default)).Items);
        Assert.Empty((await repository.ListPublishedAsync(filters with { Search = "unmatched" }, default)).Items);
    }

    private async Task<Guid[]> SeedDiscoveryEvents()
    {
        var ids = new Guid[5];
        for (var i = 0; i < ids.Length; i++) ids[i] = await Event(5);
        await using var db = new EventPilotDbContext(options);
        var start = DateTime.UtcNow.AddDays(2);
        for (var i = 0; i < ids.Length; i++)
        {
            var ev = await db.Events.SingleAsync(x => x.Id == ids[i]);
            ev.Title = "Conference";
            ev.Price = 10;
            ev.StartAt = start; // Check the ID tiebreaker.
            ev.EndAt = start.AddHours(2);
            ev.Location = "Paris";
            ev.Category = EventCategory.Business;
            ev.OrganizerUserId = i == 1 ? 2 : 1;
            ev.Status = i == 2 ? EventStatus.Draft : i == 3 ? EventStatus.Cancelled : EventStatus.Published;
            ev.IsDeleted = i == 4;
        }
        await db.SaveChangesAsync();
        return ids;
    }
}
