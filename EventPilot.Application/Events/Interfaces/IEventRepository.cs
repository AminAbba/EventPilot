using EventPilot.Application.Abstractions.Models;
using EventPilot.Application.Events.Dtos;
using EventPilot.Application.Events.Queries;
using EventPilot.Domain.Entities;

namespace EventPilot.Application.Events.Interfaces;

public interface IEventRepository
{
    Task<Event?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<EventDto?> GetDetailsAsync(Guid id, int? viewerId, CancellationToken ct);
    Task<bool> ExistsAsync(Guid id, CancellationToken ct);
    Task<Guid> CreateEventAsync(Event ev,int CurrentUserId, CancellationToken ct);
    Task<PagedResult<EventDto>> GetByOrganizerUserId(ListEventQuery query,int? OrganizerId, CancellationToken ct);
    Task<PagedResult<EventDto>> ListPublishedAsync(ListEventQuery query, CancellationToken ct);
    Task DeleteEvent(Guid EventId, int? userId, CancellationToken ct);
    Task UpdateAsync(Guid eventId, int? userId, UpdateEventDto dto, CancellationToken cancellationToken);
    Task<int> GetOrganizerIdByEventId(Guid eventId , CancellationToken cancellationToken);
}

