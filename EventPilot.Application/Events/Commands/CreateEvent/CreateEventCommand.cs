using EventPilot.Application.Events.Dtos;
using MediatR;

namespace EventPilot.Application.Events.Commands.CreateEvent;
    public sealed record CreateEventCommand(CreateEventDto ev) : IRequest<Guid>;


