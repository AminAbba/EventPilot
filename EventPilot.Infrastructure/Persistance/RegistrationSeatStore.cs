using System.Data;
using EventPilot.Application.Registrations.Common.Exceptions;
using EventPilot.Application.Common.Diagnostics;
using EventPilot.Application.Registrations.Interfaces;
using EventPilot.Domain.Enums;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace EventPilot.Infrastructure.Persistence;

public sealed class RegistrationSeatStore(IDbContextFactory<EventPilotDbContext> factory) : IRegistrationSeatStore
{
    private const int MaxAttempts = 5;

    public async Task<RegistrationSeatResult> ChangeAsync(Guid eventId, int userId,
        Func<RegistrationSeatState, Registration> change, CancellationToken ct)
    {
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            try
            {
                return await SaveRegistrationAsync(eventId, userId, change, ct);
            }
            catch (Exception ex) when (ex is DbUpdateConcurrencyException || IsDeadlock(ex))
            {
                AppTelemetry.SeatRetries.Add(1,
                    new KeyValuePair<string, object?>("reason", IsDeadlock(ex) ? "deadlock" : "concurrency"));
                if (attempt == MaxAttempts - 1)
                    throw new BusinessRuleException("The event is busy. Please retry your request.");
                await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(10, 35) * (attempt + 1)), ct);
            }
            catch (DbUpdateException ex) when (SqlServerErrors.IsUniqueConstraintViolation(ex))
            {
                throw new AlreadyRegisteredException("You have already registered for this event.");
            }
        }
        throw new InvalidOperationException("Registration retry loop exhausted.");
    }

    private async Task<RegistrationSeatResult> SaveRegistrationAsync(Guid eventId, int userId,
        Func<RegistrationSeatState, Registration> change, CancellationToken ct)
    {
        // Use a fresh context for each retry.
        await using var db = await factory.CreateDbContextAsync(ct);
        var ev = await db.Events.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == eventId, ct)
            ?? throw new NotFoundException("Event not found.");

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        // Lock the event first. Even an unchanged Capacity updates rowversion.
        var affected = await db.Events.IgnoreQueryFilters()
            .Where(x => x.Id == eventId && x.RowVersion == ev.RowVersion)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Capacity, x => x.Capacity), ct);
        if (affected != 1)
            throw new DbUpdateConcurrencyException();

        var existing = await db.Registrations.SingleOrDefaultAsync(
            x => x.EventId == eventId && x.UserId == userId, ct);
        var confirmedCount = await db.Registrations.CountAsync(
            x => x.EventId == eventId && x.Status == RegistrationStatus.Confirmed, ct);
        var wasConfirmed = existing?.Status == RegistrationStatus.Confirmed;

        var registration = change(new RegistrationSeatState(ev, existing, confirmedCount));
        if (existing is null)
            db.Registrations.Add(registration);

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        if (wasConfirmed)
            confirmedCount--;
        if (registration.Status == RegistrationStatus.Confirmed)
            confirmedCount++;

        return new RegistrationSeatResult(registration, ev.Capacity, ev.Capacity - confirmedCount);
    }

    private static bool IsDeadlock(Exception ex) =>
        ex is SqlException { Number: 1205 } || ex.InnerException is SqlException { Number: 1205 };
}
