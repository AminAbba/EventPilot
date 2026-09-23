using EventPilot.Application.Abstractions.Models;
using EventPilot.Application.Registrations.Dtos;
using EventPilot.Application.Registrations.Queries;

namespace EventPilot.Application.Registrations.Interfaces;

public interface IRegistrationRepository
{
    Task<PagedResult<ListEventRegistrationsDto>> ListByEventAsync(Guid eventId, int pageNumber, int pageSize, CancellationToken ct);
    Task<PagedResult<MyRegistrationDto>> ListByUserAsync(int userId, int pageNumber, int pageSize, CancellationToken ct);
}
