using EventPilot.Application.Abstractions.Models;
using EventPilot.Application.Events.Dtos;
using MediatR;

namespace EventPilot.Application.Events.Queries;

public sealed record ListEventQuery(
    int PageNumber = 1,
    int PageSize = 20,
    string? Search = null,
    decimal? Price = null
    ) : IRequest<PagedResult<EventDto>>;


