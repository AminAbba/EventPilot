namespace EventPilot.Application.Events.Dtos
{
    public sealed class UpdateEventDto
    {
        public required string Title { get; set; }
        public required string Description { get; set; }
        public required string Location { get; set; }
        public DateTime StartAt { get; set; }
        public DateTime EndAt { get; set; }
        public int Capacity { get; set; }
        public decimal Price { get; set; }
        public byte[] RowVersion { get; set; } = [];
        public EventPilot.Domain.Entities.EventStatus? Status { get; set; }
        public EventPilot.Domain.Entities.EventCategory? Category { get; set; }
    }
}
