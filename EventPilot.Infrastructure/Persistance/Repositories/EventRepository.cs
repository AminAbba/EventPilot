using EventPilot.Application.Abstractions;
using EventPilot.Application.Abstractions.Models;
using EventPilot.Application.Events.Dtos;
using EventPilot.Application.Events.Interfaces;
using EventPilot.Application.Events.Queries;
using EventPilot.Domain.Entities;
using EventPilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using EventPilot.Domain.Enums;
using EventPilot.Application.Events;
using EventPilot.Infrastructure.Caching;
using System.Security.Cryptography;
using System.Text;

namespace EventPilot.Infrastructure.Persistance.Repositories
{
    public sealed class EventRepository : IEventRepository
    {
        private readonly EventPilotDbContext _db;
        private readonly IUnitOfWork _uow;
        private readonly EventReadCache? _cache;

        public EventRepository(EventPilotDbContext db, IUnitOfWork uow, EventReadCache? cache = null)
        {
            _db = db;
            _uow = uow;
            _cache = cache;
        }
        public async Task<Event?> GetByIdAsync(Guid id, CancellationToken ct) =>
            await _db.Events.FirstOrDefaultAsync(e => e.Id == id, ct);

        public async Task<bool> ExistsAsync(Guid id, CancellationToken ct) =>
            await _db.Events.AnyAsync(e => e.Id == id, ct);

        public async Task<EventDto?> GetDetailsAsync(Guid id, int? viewerId, CancellationToken ct)
        {
            var query = _db.Events.AsNoTracking()
                .Where(x => x.Id == id && (x.Status == EventStatus.Published || x.OrganizerUserId == viewerId));
            string? key = null;
            if (_cache?.Enabled == true)
            {
                // Check visibility and version before reading the cache.
                var version = await query.Select(x => x.RowVersion).SingleOrDefaultAsync(ct);
                if (version is null) return null;
                key = $"event:v1:{id:N}:{Convert.ToHexString(version)}";
                var cached = await _cache.GetAsync<EventDto>(key, ct);
                if (cached is not null) return cached;
            }
            var result = await SelectDetails(query).SingleOrDefaultAsync(ct);
            if (result is not null && key == $"event:v1:{id:N}:{Convert.ToHexString(result.RowVersion)}")
            {
                var currentVersion = await query.Select(x => x.RowVersion).SingleOrDefaultAsync(ct);
                if (currentVersion is not null && currentVersion.SequenceEqual(result.RowVersion))
                    await _cache!.SetAsync(key, result, ct);
            }
            return result;
        }

        public async Task<Guid> CreateEventAsync(Event ev, int CurrentUserId, CancellationToken ct)
        {
            _db.Set<Event>().Add(ev);
            await _uow.SaveChangesAsync(ct);
            return ev.Id;
        }

        public Task<PagedResult<EventDto>> ListPublishedAsync(ListEventQuery query, CancellationToken ct) =>
            ListAsync(_db.Events.AsNoTracking().Where(x => x.Status == EventStatus.Published && x.EndAt > DateTime.UtcNow), query, ct);

        public Task<PagedResult<EventDto>> GetByOrganizerUserId(ListEventQuery query, int? organizerId, CancellationToken ct)
        {
            if (organizerId is null || organizerId <= 0)
                throw new UnauthorizedAccessException("Authentication is required to list your events.");

            return ListAsync(_db.Events.AsNoTracking().Where(x => x.OrganizerUserId == organizerId), query, ct);
        }

        private async Task<PagedResult<EventDto>> ListAsync(
            IQueryable<Event> events, ListEventQuery query, CancellationToken ct)
        {
            var search = query.Search?.Trim();
            if (!string.IsNullOrEmpty(search))
                events = events.Where(x => x.Title.Contains(search));
            if (query.Price.HasValue)
                events = events.Where(x => x.Price == query.Price.Value);

            var totalCount = await events.CountAsync(ct);
            var page = events.OrderByDescending(x => x.StartAt).ThenBy(x => x.Id)
                .Skip((query.PageNumber - 1) * query.PageSize)
                .Take(query.PageSize);
            string? key = null;
            if (_cache?.Enabled == true)
            {
                var versions = await page.Select(x => new { x.Id, x.RowVersion }).ToListAsync(ct);
                key = PageKey(versions.Select(x => (x.Id, x.RowVersion)), query, totalCount);
                var cached = await _cache.GetAsync<PagedResult<EventDto>>(key, ct);
                if (cached is not null) return cached;
            }
            var items = await SelectDetails(page).ToListAsync(ct);
            var result = PagedResult<EventDto>.Create(items, query.PageNumber, query.PageSize, totalCount);
            // Skip caching if the version changed between reads.
            if (key is not null && key == PageKey(items.Select(x => (x.Id, x.RowVersion)), query, totalCount))
            {
                var current = await page.Select(x => new { x.Id, x.RowVersion }).ToListAsync(ct);
                if (key == PageKey(current.Select(x => (x.Id, x.RowVersion)), query, totalCount))
                    await _cache!.SetAsync(key, result, ct);
            }
            return result;
        }

        private static IQueryable<EventDto> SelectDetails(IQueryable<Event> events)
        {
            return events.Select(x => new EventDto
            {
                Id = x.Id,
                CreatedAt = x.CreatedAt,
                RowVersion = x.RowVersion,
                Title = x.Title,
                Description = x.Description,
                Location = x.Location,
                StartAt = x.StartAt,
                EndAt = x.EndAt,
                ImageUrl = x.ImageUrl,
                Price = x.Price,
                Status = x.Status,
                Category = x.Category,
                Capacity = x.Capacity,
                ConfirmedRegistrations = x.Registrations.Count(r => r.Status == RegistrationStatus.Confirmed)
            });
        }

        private static string PageKey(IEnumerable<(Guid Id, byte[] Version)> rows, ListEventQuery query, int count)
        {
            var signature = $"{query.PageNumber}:{query.PageSize}:{count}:" +
                string.Join('|', rows.Select(x => $"{x.Id:N}:{Convert.ToHexString(x.Version)}"));
            return "events:v1:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(signature)));
        }
        public async Task DeleteEvent(Guid EventId, int? userId, CancellationToken ct)
        {
            if (userId is null || userId <= 0) throw new UnauthorizedAccessException("Authentication required.");
            var ev = await _db.Events.SingleOrDefaultAsync(x => x.Id == EventId, ct)
                ?? throw new KeyNotFoundException("Event not found.");
            EventManagementRules.EnsureOwner(ev, userId);
            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            ev.IsDeleted = true;
            ev.DeletedAt = DateTime.UtcNow;
            // Save the event first to keep lock order consistent.
            await _uow.SaveChangesAsync(ct);
            await CancelActiveRegistrations(EventId, ct);
            await transaction.CommitAsync(ct);
        }

        public async Task UpdateAsync(Guid eventId, int? userId, UpdateEventDto dto, CancellationToken cancellationToken)
        {
            if (userId is null || userId <= 0) throw new UnauthorizedAccessException("Authentication required.");
            var ev = await _db.Events.SingleOrDefaultAsync(x => x.Id == eventId, cancellationToken)
                ?? throw new KeyNotFoundException("Event not found.");
            EventManagementRules.EnsureOwner(ev, userId);
            var confirmed = await _db.Registrations.CountAsync(
                x => x.EventId == eventId && x.Status == RegistrationStatus.Confirmed, cancellationToken);
            EventManagementRules.ApplyUpdate(ev, dto, confirmed, userId);
            _db.Entry(ev).Property(x => x.RowVersion).OriginalValue = dto.RowVersion;
            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
            await _uow.SaveChangesAsync(cancellationToken);
            if (ev.Status == EventStatus.Cancelled)
                await CancelActiveRegistrations(eventId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        private Task<int> CancelActiveRegistrations(Guid eventId, CancellationToken ct) =>
            _db.Registrations.Where(x => x.EventId == eventId && x.Status != RegistrationStatus.Cancelled)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Status, RegistrationStatus.Cancelled), ct);
        public async Task<int> GetOrganizerIdByEventId(Guid eventId, CancellationToken cancellationToken)
        {
            return await _db.Events.Where(x => x.Id == eventId).Select(x => x.OrganizerUserId).SingleAsync();
        }
    }
}
