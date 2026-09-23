using EventPilot.Application.Abstractions.Auth;
using EventPilot.Application.Events.Interfaces;
using MediatR;
using System;
using System.Collections.Generic;
using System.Text;

namespace EventPilot.Application.Events.Commands.UpdateEvent
{
    public class UpdateEventCommandHandler : IRequestHandler<UpdateEventCommand>
    {
        private readonly IEventRepository _eventRepository;
        private readonly ICurrentUser _currentuser;
        public UpdateEventCommandHandler(IEventRepository eventRepository, ICurrentUser currentUser)
        {
            _eventRepository = eventRepository;
            _currentuser = currentUser;
        }
        public async Task Handle(UpdateEventCommand request, CancellationToken cancellationToken)
        {
            if (!_currentuser.IsAuthenticated || _currentuser.UserId is not int userId || userId <= 0)
                throw new UnauthorizedAccessException("Authentication required.");

            await _eventRepository.UpdateAsync(request.eventId, userId, request.updateEventDto, cancellationToken);
        }
    }
}
