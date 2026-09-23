using System.Data.Common;
using EventPilot.Domain.Enums;
using EventPilot.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace EventPilot.Tests;

public sealed partial class RegistrationCapacityTests
{
    [Fact]
    public async Task Sql_server_deadlock_victim_is_rolled_back_and_retried_without_duplicate_seats()
    {
        var first = await Event(); var second = await Event();
        var deadlock = new RealDeadlock(options, first, second);
        var result = await Register(first, 2, Store(deadlock));
        await deadlock.Competitor.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(deadlock.VictimObserved);
        Assert.True(deadlock.Attempts >= 2);
        Assert.Equal(0, result.RemainingSeats);
        await using var db = new EventPilotDbContext(options);
        Assert.Equal(1, await db.Registrations.CountAsync(x => x.EventId == first && x.Status == RegistrationStatus.Confirmed));
    }

    private sealed class RealDeadlock(DbContextOptions<EventPilotDbContext> options, Guid first, Guid second) : DbCommandInterceptor
    {
        public int Attempts;
        public bool VictimObserved;
        public Task Competitor = Task.CompletedTask;

        public override async ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            int result, CancellationToken ct = default)
        {
            if (!command.CommandText.StartsWith("UPDATE") || !command.CommandText.Contains("[Capacity]")) return result;
            if (Interlocked.Increment(ref Attempts) != 1) return result;
            await using var own = command.Connection!.CreateCommand();
            own.Transaction = command.Transaction;
            own.CommandText = "SET DEADLOCK_PRIORITY LOW";
            await own.ExecuteNonQueryAsync(ct);
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            // Reverse the lock order to trigger a real deadlock.
            Competitor = Task.Run(async () =>
            {
                try
                {
                    await using var other = new EventPilotDbContext(options);
                    await using var transaction = await other.Database.BeginTransactionAsync(ct);
                    await other.Database.ExecuteSqlRawAsync("SET DEADLOCK_PRIORITY HIGH", ct);
                    await other.Events.Where(x => x.Id == second)
                        .ExecuteUpdateAsync(s => s.SetProperty(x => x.Capacity, x => x.Capacity), ct);
                    ready.SetResult();
                    await other.Events.Where(x => x.Id == first)
                        .ExecuteUpdateAsync(s => s.SetProperty(x => x.Capacity, x => x.Capacity), ct);
                    await transaction.CommitAsync(ct);
                }
                catch (Exception ex) { ready.TrySetException(ex); throw; }
            }, ct);
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(15), ct);
            own.CommandText = "UPDATE Events SET Capacity = Capacity WHERE Id = @id";
            var parameter = own.CreateParameter(); parameter.ParameterName = "@id"; parameter.Value = second; own.Parameters.Add(parameter);
            try { await own.ExecuteNonQueryAsync(ct); }
            catch (SqlException ex) when (ex.Number == 1205) { VictimObserved = true; throw; }
            return result;
        }
    }
}
