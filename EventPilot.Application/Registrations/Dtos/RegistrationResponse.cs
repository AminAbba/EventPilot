namespace EventPilot.Application.Registrations.Dtos;

public sealed class RegistrationResponse
{
    public EventPilot.Domain.Enums.RegistrationStatus Status { get; init; }
    public int Capacity { get; init; }
    public int RemainingSeats { get; init; }
    public int RegistrationId { get; init; }
    public Guid EventId { get; init; }
    public int UserId { get; init; }
    public string Email { get; init; } = null!;
    public string FullName { get; init; } = null!;
    public DateTime RegisteredAt { get; init; }

    public static RegistrationResponse From(Interfaces.RegistrationSeatResult result) => new()
    {
        RegistrationId = result.Registration.Id,
        EventId = result.Registration.EventId,
        UserId = result.Registration.UserId,
        Email = result.Registration.EmailSnapshot,
        FullName = result.Registration.FullNameSnapshot,
        RegisteredAt = result.Registration.RegisteredAt,
        Status = result.Registration.Status,
        Capacity = result.Capacity,
        RemainingSeats = result.RemainingSeats
    };
}
