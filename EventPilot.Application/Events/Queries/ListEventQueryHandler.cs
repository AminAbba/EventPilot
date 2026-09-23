using EventPilot.Application.Abstractions.Models;
using EventPilot.Application.Events.Dtos;
using EventPilot.Application.Events.Interfaces;
using MediatR;

namespace EventPilot.Application.Events.Queries;

public sealed class ListEventQueryHandler(IEventRepository repository)
    : IRequestHandler<ListEventQuery, PagedResult<EventDto>>
{
    public Task<PagedResult<EventDto>> Handle(ListEventQuery request, CancellationToken ct) =>
        repository.ListPublishedAsync(request, ct);
}
