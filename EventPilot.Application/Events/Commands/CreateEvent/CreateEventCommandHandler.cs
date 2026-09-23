using EventPilot.Application.Abstractions.Auth;
using EventPilot.Application.Events.Interfaces;
using EventPilot.Domain.Entities;
using MediatR;

namespace EventPilot.Application.Events.Commands.CreateEvent;

public class CreateEventCommandHandler : IRequestHandler<CreateEventCommand, Guid>
{
    private readonly IEventRepository _eventRepository;
    private readonly ICurrentUser _currentUser;


    public CreateEventCommandHandler(ICurrentUser currentUser ,IEventRepository eventRepository)
    {
        _currentUser = currentUser;
        _eventRepository = eventRepository;
    }

    public async Task<Guid> Handle(CreateEventCommand request, CancellationToken cancellationToken)
    {

        if (!_currentUser.IsAuthenticated || _currentUser.UserId is not int userId || userId <= 0)
            throw new UnauthorizedAccessException("Authentication required.");

        var entity = new Event
        {
            Id = Guid.NewGuid(),
            OrganizerUserId = userId,

            Title = request.ev.Title,
            Description = request.ev.Description,
            Location = request.ev.Location,

            StartAt = request.ev.StartAt,
            EndAt = request.ev.EndAt,

            Capacity = request.ev.Capacity,
            Category = request.ev.Category,

            Status = request.ev.Status,
            Price = request.ev.Price
        };

        return await _eventRepository.CreateEventAsync(entity, userId, cancellationToken);
    }
}

