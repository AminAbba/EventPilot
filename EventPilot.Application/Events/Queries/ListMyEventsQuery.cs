using EventPilot.Application.Abstractions.Auth;
using EventPilot.Application.Abstractions.Models;
using EventPilot.Application.Events.Dtos;
using EventPilot.Application.Events.Interfaces;
using MediatR;

namespace EventPilot.Application.Events.Queries;

public sealed record ListMyEventsQuery(int PageNumber = 1, int PageSize = 20,
    string? Search = null, decimal? Price = null) : IRequest<PagedResult<EventDto>>
{
    public ListEventQuery ToFilters() => new(PageNumber, PageSize, Search, Price);
}

public sealed class ListMyEventsQueryHandler(IEventRepository repository, ICurrentUser currentUser)
    : IRequestHandler<ListMyEventsQuery, PagedResult<EventDto>>
{
    public Task<PagedResult<EventDto>> Handle(ListMyEventsQuery request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not int userId || userId <= 0)
            throw new UnauthorizedAccessException("Authentication is required to list your events.");
        return repository.GetByOrganizerUserId(request.ToFilters(), userId, ct);
    }
}

