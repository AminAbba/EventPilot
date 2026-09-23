using MediatR;
using System;
using System.Collections.Generic;
using System.Text;

namespace EventPilot.Application.Events.Commands.DeleteEvent
{
    public sealed record DeleteEventCommand(Guid id) : IRequest;
}
