using EventPilot.Application.Common.Diagnostics;
using EventPilot.Infrastructure.Caching;
using EventPilot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace EventPilot.Web.Operations;

public static class OperationalServices
{
    public static void AddOperationalServices(this WebApplicationBuilder builder)
    {
        var config = builder.Configuration;
        builder.Services.AddOptions<EventCacheOptions>().Bind(config.GetSection("Cache"))
            .Validate(x => x.TtlSeconds is >= 5 and <= 300, "Cache TTL must be 5–300 seconds.")
            .Validate(x => x.TimeoutMilliseconds is >= 50 and <= 2000, "Cache timeout must be 50–2000 ms.")
            .ValidateOnStart();
        if (config.GetValue<bool>("Cache:Enabled"))
        {
            var connection = config.GetConnectionString("Redis");
            if (string.IsNullOrWhiteSpace(connection)) throw new InvalidOperationException("ConnectionStrings:Redis is required when caching is enabled.");
            builder.Services.AddKeyedSingleton<IDistributedCache>("events", (_, _) =>
            {
                var redis = StackExchange.Redis.ConfigurationOptions.Parse(connection);
                redis.AbortOnConnectFail = false;
                redis.ConnectTimeout = 1000;
                redis.AsyncTimeout = 1000;
                return new RedisCache(Options.Create(new RedisCacheOptions
                {
                    ConfigurationOptions = redis, InstanceName = "eventpilot:"
                }));
            });
        }
        else builder.Services.AddKeyedSingleton<IDistributedCache>("events", (_, _) =>
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())));
        builder.Services.AddSingleton<EventReadCache>();

        builder.Services.AddHealthChecks()
            .AddCheck<SqlReadinessCheck>("sql", tags: ["ready"], timeout: TimeSpan.FromSeconds(5))
            .AddCheck<CacheReadinessCheck>("cache", failureStatus: HealthStatus.Degraded, tags: ["ready"], timeout: TimeSpan.FromSeconds(2));

        var endpoint = config["Telemetry:OtlpEndpoint"];
        if (!string.IsNullOrWhiteSpace(endpoint) &&
            (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")))
            throw new InvalidOperationException("Telemetry:OtlpEndpoint must be an absolute HTTP(S) collector URL.");
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService("EventPilot", serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString()))
            .WithTracing(traces =>
            {
                // Collect only our own telemetry to avoid sensitive data.
                traces.AddSource(AppTelemetry.Name).SetSampler(new ParentBasedSampler(new AlwaysOnSampler()));
                if (!string.IsNullOrWhiteSpace(endpoint)) traces.AddOtlpExporter(options => options.Endpoint = new Uri(endpoint));
            })
            .WithMetrics(metrics =>
            {
                metrics.AddMeter(AppTelemetry.Name, "System.Runtime", "Microsoft.AspNetCore.Hosting", "Microsoft.AspNetCore.Server.Kestrel");
                if (!string.IsNullOrWhiteSpace(endpoint)) metrics.AddOtlpExporter(options => options.Endpoint = new Uri(endpoint));
            });
    }

    public static void MapOperationalEndpoints(this WebApplication app)
    {
        static Task Write(HttpContext context, HealthReport report) => context.Response.WriteAsJsonAsync(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.ToDictionary(x => x.Key, x => x.Value.Status.ToString())
        });
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false, ResponseWriter = Write });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready"), ResponseWriter = Write });
    }
}

public sealed class SqlReadinessCheck(IDbContextFactory<EventPilotDbContext> factory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            // Check that the database tables are available.
            await db.Events.AsNoTracking().Select(x => x.CreatedAt).Take(1).ToListAsync(ct);
            return HealthCheckResult.Healthy();
        }
        catch (Exception) when (!ct.IsCancellationRequested) { return HealthCheckResult.Unhealthy(); }
    }
}

public sealed class CacheReadinessCheck([FromKeyedServices("events")] IDistributedCache cache, EventReadCache events) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        if (!events.Enabled) return HealthCheckResult.Healthy();
        try
        {
            await cache.GetAsync("health", ct).WaitAsync(ct);
            return HealthCheckResult.Healthy();
        }
        catch (Exception) { return HealthCheckResult.Degraded(); }
    }
}
