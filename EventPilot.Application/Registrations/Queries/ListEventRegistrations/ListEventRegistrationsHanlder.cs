using EventPilot.Application.Abstractions.Auth;
using EventPilot.Application.Abstractions.Models;
using EventPilot.Application.Common.Exceptions;
using EventPilot.Application.Events.Interfaces;
using EventPilot.Application.Registrations.Common.Exceptions;
using EventPilot.Application.Registrations.Dtos;
using EventPilot.Application.Registrations.Interfaces;
using MediatR;

namespace EventPilot.Application.Registrations.Queries.ListEventRegistrations;

public sealed class ListEventRegistrationsHandler(
    IEventRepository events, IRegistrationRepository registrations, ICurrentUser currentUser)
    : IRequestHandler<ListEventRegistrationsQuery, PagedResult<ListEventRegistrationsDto>>
{
    public async Task<PagedResult<ListEventRegistrationsDto>> Handle(ListEventRegistrationsQuery request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not int userId || userId <= 0)
            throw new UnauthorizedAccessException("Authentication required.");
        var ev = await events.GetByIdAsync(request.EventId, ct) ?? throw new NotFoundException("Event not found.");
        if (ev.OrganizerUserId != userId)
            throw new ForbiddenException("You may only view registrations for your own events.");
        return await registrations.ListByEventAsync(request.EventId, request.PageNumber, request.PageSize, ct);
    }
}
