using EventPilot.Application.Abstractions.Auth;
using EventPilot.Application.Events.Interfaces;
using MediatR;
using System;
using System.Collections.Generic;
using System.Text;

namespace EventPilot.Application.Events.Commands.DeleteEvent
{
    public class DeleteEventCommandHandler : IRequestHandler<DeleteEventCommand>
    {
        private readonly IEventRepository _eventRepository;
        private readonly ICurrentUser _currentUser;
        public DeleteEventCommandHandler(IEventRepository eventRepository, ICurrentUser currentUser)
        {
            _eventRepository = eventRepository;
            _currentUser = currentUser;
        }
        public async Task Handle(DeleteEventCommand request, CancellationToken cancellationToken)
        {
            if (!_currentUser.IsAuthenticated || _currentUser.UserId is not int userId || userId <= 0)
                throw new UnauthorizedAccessException("Authentication required.");
            await _eventRepository.DeleteEvent(request.id, userId, cancellationToken);
        }
    }
}
