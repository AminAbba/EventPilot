using System.Reflection;
using EventPilot.Application.Abstractions.Auth;
using EventPilot.Application.Events.Queries;
using EventPilot.Application.Registrations.Queries;
using EventPilot.Application.Registrations.Queries.ListEventRegistrations;
using EventPilot.Web.Controllers;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace EventPilot.Tests;

public sealed class EventListingContractTests
{
    [Theory]
    [InlineData(0, 20)]
    [InlineData(-1, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    [InlineData(int.MaxValue, 100)]
    public void Discovery_invalid_pagination_is_rejected_for_both_queries(int page, int size)
    {
        Assert.False(new ListEventQueryValidator().Validate(new ListEventQuery(page, size)).IsValid);
        Assert.False(new ListMyEventsQueryValidator().Validate(new ListMyEventsQuery(page, size)).IsValid);
        Assert.False(new ListMyRegistrationsValidator().Validate(new ListMyRegistrationsQuery(page, size)).IsValid);
        Assert.False(new ListEventRegistrationsQueryValidator()
            .Validate(new ListEventRegistrationsQuery(Guid.NewGuid(), page, size)).IsValid);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 100)]
    [InlineData(21474837, 100)]
    public void Pagination_accepts_valid_boundaries_for_all_lists(int page, int size)
    {
        Assert.True(new ListEventQueryValidator().Validate(new ListEventQuery(page, size)).IsValid);
        Assert.True(new ListMyEventsQueryValidator().Validate(new ListMyEventsQuery(page, size)).IsValid);
        Assert.True(new ListMyRegistrationsValidator().Validate(new ListMyRegistrationsQuery(page, size)).IsValid);
        Assert.True(new ListEventRegistrationsQueryValidator()
            .Validate(new ListEventRegistrationsQuery(Guid.NewGuid(), page, size)).IsValid);
    }

    [Fact]
    public void Discovery_valid_defaults_and_invalid_filters()
    {
        Assert.True(new ListEventQueryValidator().Validate(new ListEventQuery()).IsValid);
        Assert.True(new ListMyEventsQueryValidator().Validate(new ListMyEventsQuery()).IsValid);
        Assert.False(new ListEventQueryValidator().Validate(new ListEventQuery(Price: -1)).IsValid);
        Assert.False(new ListEventQueryValidator().Validate(new ListEventQuery(Search: new string('a', 101))).IsValid);
    }

    [Fact]
    public void Discovery_only_public_list_bypasses_controller_authorization()
    {
        var controller = typeof(EventsController);
        Assert.NotNull(controller.GetCustomAttribute<AuthorizeAttribute>());
        Assert.NotNull(controller.GetMethod(nameof(EventsController.List))!.GetCustomAttribute<AllowAnonymousAttribute>());
        foreach (var method in new[] { "Mine", "Create", "UpdateEvent", "DeleteEvent" })
            Assert.Null(controller.GetMethod(method)!.GetCustomAttribute<AllowAnonymousAttribute>());
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, null)]
    [InlineData(true, 0)]
    public async Task Discovery_mine_requires_trusted_identity_before_repository_access(bool authenticated, int? id)
    {
        var current = new TestCurrentUser
        {
            IsAuthenticated = authenticated,
            UserId = id
        };
        var handler = new ListMyEventsQueryHandler(null!, current);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handler.Handle(new(), default));
    }
}

