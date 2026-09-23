using EventPilot.Domain.Entities;

namespace EventPilot.Application.Events.Dtos;

public sealed class CreateEventDto
{
    public string Title { get; init; } = "";
    public string? Description { get; init; }
    public string Location { get; init; } = "";
    public DateTime StartAt { get; init; }
    public DateTime EndAt { get; init; }
    public int Capacity { get; init; }
    public EventStatus Status { get; init; }
    public EventCategory Category { get; init; }
    public decimal Price { get; set; }
}

