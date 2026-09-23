using EventPilot.Application.Events.Dtos;
using MediatR;
using System;
using System.Collections.Generic;
using System.Text;

namespace EventPilot.Application.Events.Commands.UpdateEvent
{
    public sealed record UpdateEventCommand(UpdateEventDto updateEventDto, Guid eventId) : IRequest;
}
