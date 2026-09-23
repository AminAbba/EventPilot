using EventPilot.Domain.Entities;

namespace EventPilot.Application.Registrations.Interfaces;

public sealed record RegistrationSeatState(Event Event, Registration? Registration, int ConfirmedCount);
public sealed record RegistrationSeatResult(Registration Registration, int Capacity, int RemainingSeats);

public interface IRegistrationSeatStore
{
    // Retries can rerun the callback; avoid external side effects.
    Task<RegistrationSeatResult> ChangeAsync(Guid eventId, int userId,
        Func<RegistrationSeatState, Registration> change, CancellationToken ct);
}
