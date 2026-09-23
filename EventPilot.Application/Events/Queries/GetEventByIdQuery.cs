using EventPilot.Application.Abstractions.Auth;
using EventPilot.Application.Events.Dtos;
using EventPilot.Application.Events.Interfaces;
using EventPilot.Application.Registrations.Common.Exceptions;
using MediatR;

namespace EventPilot.Application.Events.Queries;

public sealed record GetEventByIdQuery(Guid Id) : IRequest<EventDto>;

public sealed class GetEventByIdQueryHandler(IEventRepository events, ICurrentUser currentUser)
    : IRequestHandler<GetEventByIdQuery, EventDto>
{
    public async Task<EventDto> Handle(GetEventByIdQuery request, CancellationToken ct) =>
        await events.GetDetailsAsync(request.Id, currentUser.IsAuthenticated ? currentUser.UserId : null, ct)
        ?? throw new NotFoundException("Event not found.");
}
