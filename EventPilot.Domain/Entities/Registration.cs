using EventPilot.Domain.Entities;
using EventPilot.Domain.Enums;

public class Registration
{
    public int Id { get; set; }

    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;

    public RegistrationStatus Status { get; set; }

    public string EmailSnapshot { get; set; } = string.Empty;
    public string FullNameSnapshot { get; set; } = string.Empty;

    public bool IsPaid { get; set; }
    public decimal PaidAmount { get; set; }

    public Guid EventId { get; set; }
    public Event Event { get; set; } = null!;

    public int UserId { get; set; }
}