using EventPilot.Application.Abstractions.Models;
using EventPilot.Application.Registrations.Dtos;
using EventPilot.Application.Registrations.Interfaces;
using EventPilot.Application.Registrations.Queries;
using EventPilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EventPilot.Infrastructure.Persistance.Repositories;

public sealed class RegistrationRepository(EventPilotDbContext db) : IRegistrationRepository
{
    public async Task<PagedResult<MyRegistrationDto>> ListByUserAsync(
        int userId, int pageNumber, int pageSize, CancellationToken ct)
    {
        // Include deleted events in the user's own history.
        var query = db.Registrations.IgnoreQueryFilters().AsNoTracking().Where(x => x.UserId == userId);
        var count = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.RegisteredAt).ThenBy(x => x.Id)
            .Skip((pageNumber - 1) * pageSize).Take(pageSize)
            .Select(x => new MyRegistrationDto(
                x.Id, x.EventId, x.Event.Title, x.Event.Location, x.Event.StartAt, x.Event.EndAt,
                x.RegisteredAt, x.Status, x.Event.Status, x.Event.IsDeleted)).ToListAsync(ct);
        return PagedResult<MyRegistrationDto>.Create(items, pageNumber, pageSize, count);
    }
    public async Task<PagedResult<ListEventRegistrationsDto>> ListByEventAsync(Guid eventId, int pageNumber, int pageSize, CancellationToken ct)
    {
        var query = db.Registrations.AsNoTracking().Where(r => r.EventId == eventId);
        var count = await query.CountAsync(ct);
        var items = await query.OrderByDescending(r => r.RegisteredAt).ThenBy(r => r.Id)
            .Skip((pageNumber - 1) * pageSize).Take(pageSize)
            .Select(r => new ListEventRegistrationsDto
            {
                RegistrationId = r.Id,
                Status = r.Status,
                Email = r.EmailSnapshot,
                FullName = r.FullNameSnapshot,
                RegisteredAt = r.RegisteredAt
            }).ToListAsync(ct);
        return PagedResult<ListEventRegistrationsDto>.Create(items, pageNumber, pageSize, count);
    }
}
