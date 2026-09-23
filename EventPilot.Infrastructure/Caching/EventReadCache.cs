using System.Text.Json;
using EventPilot.Application.Common.Diagnostics;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Microsoft.Extensions.DependencyInjection;

namespace EventPilot.Infrastructure.Caching;

public sealed class EventCacheOptions
{
    public bool Enabled { get; set; }
    public int TtlSeconds { get; set; } = 60;
    public int TimeoutMilliseconds { get; set; } = 250;
}

// SQL is the source of truth for cached events.
public sealed class EventReadCache([FromKeyedServices("events")] IDistributedCache cache, IOptions<EventCacheOptions> options,
    ILogger<EventReadCache> logger)
{
    private long retryAfterTicks;
    public bool Enabled => options.Value.Enabled;

    public Task<T?> GetAsync<T>(string key, CancellationToken ct) where T : class =>
        ExecuteAsync(async token =>
        {
            var bytes = await cache.GetAsync(key, token);
            var value = bytes is null ? null : JsonSerializer.Deserialize<T>(bytes);
            AppTelemetry.CacheRequests.Add(1, new KeyValuePair<string, object?>("result", value is null ? "miss" : "hit"));
            return value;
        }, ct);

    public async Task SetAsync<T>(string key, T value, CancellationToken ct) where T : class =>
        await ExecuteAsync<object>(async token =>
        {
            await cache.SetAsync(key, JsonSerializer.SerializeToUtf8Bytes(value), new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(options.Value.TtlSeconds)
            }, token);
            return null;
        }, ct);

    private async Task<T?> ExecuteAsync<T>(Func<CancellationToken, Task<T?>> action, CancellationToken ct) where T : class
    {
        if (!Enabled || DateTime.UtcNow.Ticks < Interlocked.Read(ref retryAfterTicks))
            return null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(options.Value.TimeoutMilliseconds);
        using var activity = AppTelemetry.Activities.StartActivity("cache.event_read");
        try
        {
            return await action(timeout.Token).WaitAsync(timeout.Token);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested &&
            ex is RedisException or TimeoutException or OperationCanceledException or JsonException)
        {
            // Log once per outage without connection details.
            var next = DateTime.UtcNow.AddSeconds(30).Ticks;
            var previous = Interlocked.Exchange(ref retryAfterTicks, next);
            if (previous < DateTime.UtcNow.Ticks)
                logger.LogWarning("Event cache unavailable; using SQL. Failure type {FailureType}", ex.GetType().Name);
            AppTelemetry.CacheRequests.Add(1, new KeyValuePair<string, object?>("result", "error"));
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error);
            return null;
        }
    }
}
