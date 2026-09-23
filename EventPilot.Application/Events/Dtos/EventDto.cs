using EventPilot.Domain.Entities;

namespace EventPilot.Application.Events.Dtos
{
    public sealed class EventDto
    {
        public Guid Id { get; set; }
        public DateTime CreatedAt { get; init; }
        public byte[] RowVersion { get; init; } = [];
        public string Title { get; init; } = "";
        public string? Description { get; init; }
        public string Location { get; init; } = "";
        public DateTime StartAt { get; init; }
        public DateTime EndAt { get; init; }
        public int Capacity { get; init; }
        public int ConfirmedRegistrations { get; init; }
        public int RemainingSeats => Capacity - ConfirmedRegistrations;
        public bool IsFull => RemainingSeats <= 0;
        public EventStatus Status { get; init; }
        public EventCategory Category { get; init; }
        public string? ImageUrl { get; init; }
        public decimal Price { get; set; }

    }
}
