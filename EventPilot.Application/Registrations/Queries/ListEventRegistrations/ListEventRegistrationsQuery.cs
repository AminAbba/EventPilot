using EventPilot.Application.Abstractions.Models;
using EventPilot.Application.Registrations.Dtos;
using MediatR;

namespace EventPilot.Application.Registrations.Queries.ListEventRegistrations;

public sealed record ListEventRegistrationsQuery(Guid EventId, int PageNumber = 1, int PageSize = 20)
    : IRequest<PagedResult<ListEventRegistrationsDto>>;
