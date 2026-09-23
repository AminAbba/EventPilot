using EventPilot.Application.Abstractions.Auth;
using EventPilot.Application.Registrations.Common.Exceptions;
using EventPilot.Application.Registrations.Dtos;
using EventPilot.Application.Registrations.Interfaces;
using EventPilot.Domain.Enums;
using MediatR;

namespace EventPilot.Application.Registrations.Commands.CancelRegistration;

public sealed class CancelRegistrationHandler(IRegistrationSeatStore seats, ICurrentUser currentUser)
    : IRequestHandler<CancelRegistrationCommand, RegistrationResponse>
{
    public async Task<RegistrationResponse> Handle(CancelRegistrationCommand request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not int userId || userId <= 0)
            throw new UnauthorizedAccessException("Authentication is required to cancel a registration.");

        var result = await seats.ChangeAsync(request.EventId, userId, state =>
        {
            var registration = state.Registration ?? throw new NotFoundException("Registration not found.");
            // Repeated cancellations must not release another seat.
            registration.Status = RegistrationStatus.Cancelled;
            return registration;
        }, ct);
        return RegistrationResponse.From(result);
    }
}
