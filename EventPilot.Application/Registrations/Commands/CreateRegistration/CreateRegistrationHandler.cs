using EventPilot.Application.Abstractions;
using EventPilot.Application.Abstractions.Auth;
using EventPilot.Application.Registrations.Common.Exceptions;
using EventPilot.Application.Registrations.Dtos;
using EventPilot.Application.Registrations.Interfaces;
using EventPilot.Domain.Entities;
using EventPilot.Domain.Enums;
using MediatR;

namespace EventPilot.Application.Registrations.Commands.CreateRegistration;

public sealed class CreateRegistrationHandler(
    IRegistrationSeatStore seats, IIdentityService identity, ICurrentUser currentUser)
    : IRequestHandler<CreateRegistrationCommand, RegistrationResponse>
{
    public async Task<RegistrationResponse> Handle(CreateRegistrationCommand request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not int userId || userId <= 0)
            throw new UnauthorizedAccessException("Authentication is required to register for an event.");

        var user = await identity.GetMeAsync(userId, ct);
        var result = await seats.ChangeAsync(request.EventId, userId, state =>
        {
            if (state.Event.IsDeleted) throw new NotFoundException("Event not found.");
            if (state.Event.Status != EventStatus.Published)
                throw new BusinessRuleException("Event is not published.");
            if (state.Event.StartAt <= DateTime.UtcNow)
                throw new BusinessRuleException("Registration closes when the event starts.");
            if (state.Registration?.Status == RegistrationStatus.Confirmed)
                throw new AlreadyRegisteredException("You have already registered for this event.");
            if (state.Registration is not null && state.Registration.Status != RegistrationStatus.Cancelled)
                throw new BusinessRuleException("This registration cannot be confirmed.");
            if (state.ConfirmedCount >= state.Event.Capacity)
                throw new BusinessRuleException("Event is full.");

            // Reuse the row to avoid a duplicate registration.
            var registration = state.Registration ?? new Registration { EventId = request.EventId, UserId = userId };
            registration.Status = RegistrationStatus.Confirmed;
            registration.RegisteredAt = DateTime.UtcNow;
            registration.EmailSnapshot = user.Email;
            registration.FullNameSnapshot = $"{user.FirstName} {user.LastName}".Trim();
            return registration;
        }, ct);
        return RegistrationResponse.From(result);
    }
}
