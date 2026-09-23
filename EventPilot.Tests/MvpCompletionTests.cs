using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EventPilot.Application.Common.Diagnostics;
using EventPilot.Application.Common.Models;
using EventPilot.Application.Events.Dtos;
using EventPilot.Application.Events.Queries;
using EventPilot.Infrastructure.Caching;
using EventPilot.Infrastructure.Persistance;
using EventPilot.Infrastructure.Persistance.Repositories;
using EventPilot.Infrastructure.Persistence;
using EventPilot.Web.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Xunit;

namespace EventPilot.Tests;

public sealed class UtcContractTests
{
    private static readonly JsonSerializerOptions Json = new() { Converters = { new UtcDateTimeConverter() } };

    [Theory]
    [InlineData("2030-01-01T12:00:00Z")]
    [InlineData("2030-01-01T14:00:00+02:00")]
    [InlineData("2030-01-01T07:00:00-05:00")]
    public void Equivalent_instants_normalize_to_utc(string input)
    {
        var value = JsonSerializer.Deserialize<DateTime>(JsonSerializer.Serialize(input), Json);
        Assert.Equal(new DateTime(2030, 1, 1, 12, 0, 0, DateTimeKind.Utc), value);
        Assert.Equal(DateTimeKind.Utc, value.Kind);
        Assert.Equal("\"2030-01-01T12:00:00Z\"", JsonSerializer.Serialize(value, Json));
    }

    [Theory]
    [InlineData("2030-01-01T12:00:00")]
    [InlineData("2030-01-01")]
    [InlineData("invalid")]
    public void Ambiguous_or_invalid_timestamps_are_rejected(string input) =>
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<DateTime>(JsonSerializer.Serialize(input), Json));

    [Fact]
    public void All_price_filters_reject_unrepresentable_values()
    {
        foreach (var price in new[] { decimal.MaxValue, 1.001m, -1 })
        {
            Assert.False(new ListEventQueryValidator().Validate(new ListEventQuery(Price: price)).IsValid);
            Assert.False(new ListMyEventsQueryValidator().Validate(new ListMyEventsQuery(Price: price)).IsValid);
        }
        Assert.True(new ListEventQueryValidator().Validate(new ListEventQuery(Price: 9999999999999999.99m)).IsValid);
    }
}

public sealed partial class RegistrationCapacityTests
{
    private static EventReadCache ReadCache(IDistributedCache backend) => new(backend,
        Options.Create(new EventCacheOptions { Enabled = true, TimeoutMilliseconds = 100 }),
        NullLogger<EventReadCache>.Instance);

    [Fact]
    public async Task Api_normalizes_offsets_roundtrips_utc_and_rejects_ambiguous_dates_and_prices()
    {
        await using var api = Api();
        using var owner = Client(api, 1);
        var start = new DateTimeOffset(DateTime.UtcNow.AddDays(3)).ToOffset(TimeSpan.FromHours(3));
        var body = new { ev = new { title = "UTC", description = "UTC", location = "Paris", capacity = 5,
            status = 1, startAt = start.ToString("O"), endAt = start.AddHours(1).ToString("O") } };
        var created = await owner.PostAsJsonAsync("/api/events", body);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<ApiResult<Guid>>())!.Data;
        var detail = await owner.GetStringAsync($"/api/events/{id}");
        using var json = JsonDocument.Parse(detail);
        Assert.EndsWith("Z", json.RootElement.GetProperty("data").GetProperty("startAt").GetString());
        var dto = JsonSerializer.Deserialize<ApiResult<EventDto>>(detail, new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Data!;
        Assert.Equal(start.UtcDateTime, dto.StartAt);
        await using var db = new EventPilotDbContext(options);
        Assert.Equal(DateTimeKind.Utc, (await db.Events.SingleAsync(x => x.Id == id)).StartAt.Kind);
        await AssertError(await owner.PostAsJsonAsync("/api/events", body with { ev = body.ev with { startAt = "2030-01-01T12:00:00" } }), HttpStatusCode.BadRequest);
        await AssertError(await owner.GetAsync("/api/events?price=79228162514264337593543950335"), HttpStatusCode.BadRequest);
        await AssertError(await owner.GetAsync("/api/events/mine?price=1.001"), HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Discovery_excludes_ended_events_but_keeps_ongoing_and_owner_history()
    {
        var ended = await Event();
        var ongoing = await Event();
        await Register(ended, 2);
        await using (var db = new EventPilotDbContext(options))
        {
            var ev = await db.Events.SingleAsync(x => x.Id == ended);
            ev.StartAt = DateTime.UtcNow.AddDays(-2); ev.EndAt = DateTime.UtcNow.AddDays(-1);
            var active = await db.Events.SingleAsync(x => x.Id == ongoing);
            active.StartAt = DateTime.UtcNow.AddHours(-1); active.EndAt = DateTime.UtcNow.AddHours(1);
            await db.SaveChangesAsync();
        }
        await using var api = Api();
        using var owner = Client(api, 1);
        using var attendee = Client(api, 2);
        Assert.DoesNotContain(ended.ToString(), await owner.GetStringAsync("/api/events"));
        Assert.Contains(ongoing.ToString(), await owner.GetStringAsync("/api/events"));
        Assert.Contains(ended.ToString(), await owner.GetStringAsync("/api/events/mine"));
        Assert.Contains(ended.ToString(), await attendee.GetStringAsync("/api/users/me/registrations"));
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/events/{ended}")).StatusCode);
        await AssertError(await attendee.PostAsync($"/api/events/{ongoing}/registrations", null), HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Cache_hits_refresh_after_seat_changes_and_never_bypass_visibility()
    {
        var backend = new CountingCache();
        await VerifyCache(ReadCache(backend));
        Assert.True(backend.Hits >= 2);
    }

    private async Task VerifyCache(EventReadCache cache)
    {
        var id = await Event(2);
        async Task<EventDto?> Details(int? viewer = null)
        {
            await using var db = new EventPilotDbContext(options);
            return await new EventRepository(db, new EfUnitOfWork(db), cache).GetDetailsAsync(id, viewer, default);
        }
        async Task<EventPilot.Application.Abstractions.Models.PagedResult<EventDto>> List()
        {
            await using var db = new EventPilotDbContext(options);
            return await new EventRepository(db, new EfUnitOfWork(db), cache).ListPublishedAsync(new(), default);
        }
        Assert.Equal(2, (await Details())!.RemainingSeats);
        Assert.Equal(2, (await Details())!.RemainingSeats);
        await List(); await List();
        await Register(id, 2);
        Assert.Equal(1, (await Details())!.RemainingSeats);
        Assert.Equal(1, Assert.Single((await List()).Items).RemainingSeats);
        await Cancel(id, 2);
        Assert.Equal(2, (await Details())!.RemainingSeats);
        await using (var db = new EventPilotDbContext(options))
        {
            var ev = await db.Events.SingleAsync(x => x.Id == id);
            ev.Status = EventPilot.Domain.Entities.EventStatus.Draft;
            await db.SaveChangesAsync();
        }
        Assert.NotNull(await Details(1)); // Cache the private event.
        Assert.Null(await Details(2));
        Assert.Null(await Details());
        Assert.Empty((await List()).Items);
        await using (var db = new EventPilotDbContext(options))
            await new EventRepository(db, new EfUnitOfWork(db), cache).DeleteEvent(id, 1, default);
        Assert.Null(await Details(1));
    }

    [RedisFact]
    public async Task Redis_actual_backend_preserves_versions_and_visibility()
    {
        using var backend = new RedisCache(Options.Create(new RedisCacheOptions
        {
            Configuration = Environment.GetEnvironmentVariable("EVENTPILOT_TEST_REDIS"),
            InstanceName = "eventpilot-tests:" + Guid.NewGuid().ToString("N") + ":"
        }));
        // Check Redis directly so cache fallback cannot hide a failure.
        await backend.SetStringAsync("probe", "ok", new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60) });
        Assert.Equal("ok", await backend.GetStringAsync("probe"));
        await backend.RemoveAsync("probe");
        var cache = new EventReadCache(backend, Options.Create(new EventCacheOptions { Enabled = true, TimeoutMilliseconds = 2000 }), NullLogger<EventReadCache>.Instance);
        await VerifyCache(cache);
        await using var api = Api().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Cache:Enabled", "true");
            builder.UseSetting("ConnectionStrings:Redis", Environment.GetEnvironmentVariable("EVENTPILOT_TEST_REDIS"));
        });
        using var client = Client(api);
        Assert.IsType<RedisCache>(api.Services.GetRequiredKeyedService<IDistributedCache>("events"));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/events")).StatusCode);
    }

    [Fact]
    public async Task Cache_failure_falls_back_to_sql_and_opens_short_circuit()
    {
        var backend = new FailingCache();
        var cache = ReadCache(backend);
        var id = await Event();
        await using var db = new EventPilotDbContext(options);
        var repository = new EventRepository(db, new EfUnitOfWork(db), cache);
        Assert.NotNull(await repository.GetDetailsAsync(id, null, default));
        Assert.NotNull(await repository.GetDetailsAsync(id, null, default));
        Assert.Equal(1, backend.Attempts);
    }

    [Fact]
    public async Task Health_and_tracing_are_safe_and_correlated()
    {
        var spans = new ConcurrentQueue<Activity>();
        var measurements = new ConcurrentQueue<string>();
        using var meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == AppTelemetry.Name) listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, value, tags, state) => measurements.Enqueue(instrument.Name));
        meterListener.SetMeasurementEventCallback<double>((instrument, value, tags, state) => measurements.Enqueue(instrument.Name));
        meterListener.Start();
        await using var api = Api().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddOpenTelemetry().WithTracing(traces => traces.AddProcessor(new CapturedSpans(spans)))));
        using var client = Client(api);
        var live = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Contains(spans, x => x.OperationName == "HTTP request" && x.TraceId.ToString() == live.Headers.GetValues("X-Trace-Id").Single());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
        const string traceId = "12345678901234567890123456789012";
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/events?secret=trace-secret-marker");
        request.Headers.Add("traceparent", $"00-{traceId}-1234567890123456-01");
        var response = await client.SendAsync(request);
        Assert.Equal(traceId, response.Headers.GetValues("X-Trace-Id").Single());
        var requestSpan = Assert.Single(spans, x => x.OperationName == "HTTP request" && x.TraceId.ToString() == traceId);
        Assert.Equal("api/events", requestSpan.GetTagItem("http.route"));
        Assert.Contains(spans, x => x.OperationName == "ListEventQuery" && x.TraceId.ToString() == traceId);
        Assert.DoesNotContain("trace-secret-marker", string.Join(" ", spans.SelectMany(x => x.Tags).Select(x => x.Value)));
        Assert.Contains("eventpilot.http.requests", measurements);
        Assert.Contains("eventpilot.http.duration", measurements);
    }

    [Fact]
    public async Task Optional_cache_outage_is_degraded_while_the_api_remains_ready()
    {
        await using var api = Api().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.Configure<EventCacheOptions>(options => options.Enabled = true);
            services.AddKeyedSingleton<IDistributedCache>("events", new FailingCache());
        }));
        using var client = Client(api);
        var ready = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        using var json = JsonDocument.Parse(await ready.Content.ReadAsStringAsync());
        Assert.Equal("Degraded", json.RootElement.GetProperty("status").GetString());
        Assert.Equal("Degraded", json.RootElement.GetProperty("checks").GetProperty("cache").GetString());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/events")).StatusCode);
    }

    [Fact]
    public async Task Sql_outage_fails_readiness_but_not_liveness_without_leaking_details()
    {
        await using var api = Api().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddSingleton<IDbContextFactory<EventPilotDbContext>, FailingDbContextFactory>()));
        using var client = Client(api);
        var ready = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        Assert.DoesNotContain("private-sql-secret", await ready.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
    }

    private sealed class CapturedSpans(ConcurrentQueue<Activity> spans) : BaseProcessor<Activity>
    {
        public override void OnEnd(Activity data) => spans.Enqueue(data);
    }

    private sealed class CountingCache : IDistributedCache
    {
        private readonly MemoryDistributedCache inner = new(Options.Create(new MemoryDistributedCacheOptions()));
        public int Hits;
        public byte[]? Get(string key) { var value = inner.Get(key); if (value is not null) Hits++; return value; }
        public Task<byte[]?> GetAsync(string key, CancellationToken ct = default) => Task.FromResult(Get(key));
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => inner.Set(key, value, options);
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken ct = default) { Set(key, value, options); return Task.CompletedTask; }
        public void Refresh(string key) => inner.Refresh(key);
        public Task RefreshAsync(string key, CancellationToken ct = default) => inner.RefreshAsync(key, ct);
        public void Remove(string key) => inner.Remove(key);
        public Task RemoveAsync(string key, CancellationToken ct = default) => inner.RemoveAsync(key, ct);
    }
    private sealed class FailingCache : IDistributedCache
    {
        public int Attempts;
        public byte[]? Get(string key) { Attempts++; throw new TimeoutException(); }
        public Task<byte[]?> GetAsync(string key, CancellationToken ct = default) => Task.FromResult(Get(key));
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => throw new TimeoutException();
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken ct = default) => throw new TimeoutException();
        public void Refresh(string key) => throw new TimeoutException();
        public Task RefreshAsync(string key, CancellationToken ct = default) => throw new TimeoutException();
        public void Remove(string key) => throw new TimeoutException();
        public Task RemoveAsync(string key, CancellationToken ct = default) => throw new TimeoutException();
    }
}

public sealed class RedisFactAttribute : FactAttribute
{
    public RedisFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("EVENTPILOT_TEST_REDIS")))
            Skip = "Set EVENTPILOT_TEST_REDIS to run against a real Redis instance; CI supplies one.";
    }
}
