using EventPilot.Application.Events.Commands.CreateEvent;
using EventPilot.Application.Events.Commands.UpdateEvent;
using EventPilot.Application.Events.Dtos;
using EventPilot.Application.Events.Dtos.Validators;
using EventPilot.Application.Registrations.Queries.ListEventRegistrations;
using EventPilot.Domain.Entities;
using Xunit;

namespace EventPilot.Tests;

public sealed class EventValidationTests
{
    [Theory]
    [InlineData(0)] [InlineData(-1)]
    public void Creation_rejects_nonpositive_capacity(int capacity) =>
        Assert.False(new CreateEventDtoValidator().Validate(Create(capacity: capacity)).IsValid);

    [Theory]
    [InlineData(-1)] [InlineData(1.001)]
    public void Creation_rejects_invalid_price(decimal price) =>
        Assert.False(new CreateEventDtoValidator().Validate(Create(price: price)).IsValid);

    [Fact]
    public void Creation_rejects_invalid_enums_and_missing_body()
    {
        Assert.False(new CreateEventDtoValidator().Validate(Create(status: (EventStatus)999)).IsValid);
        Assert.False(new CreateEventDtoValidator().Validate(new CreateEventDto { Category = (EventCategory)999 }).IsValid);
        Assert.False(new CreateEventCommandValidator().Validate(new CreateEventCommand(null!)).IsValid);
        Assert.True(new CreateEventDtoValidator().Validate(Create()).IsValid);
    }

    [Fact]
    public void Creation_checks_the_current_clock_on_every_validation()
    {
        var validator = new CreateEventDtoValidator();
        var past = new CreateEventDto
        {
            Title = "Test", Description = "Test", Location = "Test", Capacity = 1,
            StartAt = DateTime.UtcNow.AddDays(-1), EndAt = DateTime.UtcNow.AddDays(1)
        };
        Assert.False(validator.Validate(past).IsValid);
        Assert.True(validator.Validate(Create()).IsValid);
    }

    [Theory]
    [InlineData("capacity")] [InlineData("price")] [InlineData("precision")]
    [InlineData("start")] [InlineData("end")] [InlineData("version")]
    [InlineData("status")] [InlineData("category")] [InlineData("title")]
    public void Updates_reject_invalid_fields(string field)
    {
        var dto = ValidUpdate();
        switch (field)
        {
            case "capacity": dto.Capacity = 0; break;
            case "price": dto.Price = -1; break;
            case "precision": dto.Price = 1.001m; break;
            case "start": dto.StartAt = default; break;
            case "end": dto.EndAt = dto.StartAt; break;
            case "version": dto.RowVersion = []; break;
            case "status": dto.Status = (EventStatus)999; break;
            case "category": dto.Category = (EventCategory)999; break;
            case "title": dto.Title = " "; break;
        }
        Assert.False(new UpdateEventDtoValidator().Validate(dto).IsValid);
    }

    [Fact]
    public void Updates_accept_optional_status_and_reject_missing_body()
    {
        Assert.True(new UpdateEventDtoValidator().Validate(ValidUpdate()).IsValid);
        Assert.False(new UpdateEventCommandValidator().Validate(new UpdateEventCommand(null!, Guid.NewGuid())).IsValid);
    }

    [Theory]
    [InlineData(0, 20)] [InlineData(1, 0)] [InlineData(1, 101)] [InlineData(int.MaxValue, 100)]
    public void Attendee_pagination_rejects_invalid_bounds(int page, int size) =>
        Assert.False(new ListEventRegistrationsQueryValidator().Validate(new ListEventRegistrationsQuery(Guid.NewGuid(), page, size)).IsValid);

    private static CreateEventDto Create(int capacity = 1, decimal price = 0, EventStatus status = EventStatus.Draft) => new()
    {
        Title = "Test", Description = "Description", Location = "Paris", Capacity = capacity, Price = price,
        StartAt = DateTime.UtcNow.AddDays(1), EndAt = DateTime.UtcNow.AddDays(2), Status = status
    };
    private static UpdateEventDto ValidUpdate() => new()
    {
        Title = "Test", Description = "Description", Location = "Paris", Capacity = 1,
        StartAt = DateTime.UtcNow.AddDays(1), EndAt = DateTime.UtcNow.AddDays(2), RowVersion = new byte[8]
    };
}
