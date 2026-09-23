using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using EventPilot.Application.Common.Models;
using EventPilot.Application.Events.Dtos;
using EventPilot.Domain.Entities;
using EventPilot.Infrastructure.Auth;
using EventPilot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Xunit;

namespace EventPilot.Tests;

public sealed partial class RegistrationCapacityTests
{
    private const string TestJwtKey = "OnlyForTests-0123456789-0123456789-0123456789";

    [Theory]
    [InlineData("/")]
    [InlineData("/Login")]
    [InlineData("/Register")]
    public async Task Public_pages_render_with_an_anonymous_session(string path)
    {
        await using var api = Api();
        using var client = Client(api);

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("EventPilot", await response.Content.ReadAsStringAsync());
    }

    private WebApplicationFactory<Program> Api() => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
    {
        builder.UseEnvironment("Production");
        builder.UseSetting("Jwt:Key", TestJwtKey);
        builder.UseSetting("Jwt:Issuer", "EventPilot.Tests");
        builder.UseSetting("Jwt:Audience", "EventPilot.Tests");
        builder.UseSetting("App:BaseUrl", "https://localhost/");
        builder.UseSetting("Logging:FileEnabled", "false");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<EventPilotDbContext>();
            services.RemoveAll<IDbContextFactory<EventPilotDbContext>>();
            services.AddSingleton<IDbContextFactory<EventPilotDbContext>>(new Factory(options));
            services.AddScoped(_ => new EventPilotDbContext(options));
        });
    });
    private static HttpClient Client(WebApplicationFactory<Program> api, int? user = null)
    {
        var client = api.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        if (user.HasValue)
        {
            var generator = new JwtTokenGenerator(Options.Create(new JwtOptions {
                Key = TestJwtKey, Issuer = "EventPilot.Tests", Audience = "EventPilot.Tests", ExpireMinutes = 30
            }));
            string[] roles = user <= 2 ? ["User", "Organizer"] : ["User"];
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", generator.GenerateToken(user.Value, "test@example.com", roles, "integration-test-stamp").Token);
        }
        return client;
    }

    [Fact]
    public async Task Api_details_visibility_seats_and_missing_events()
    {
        var id = await Event(2);
        await Register(id, 2);
        await using var api = Api();
        using var anonymous = Client(api);
        var detail = await anonymous.GetFromJsonAsync<ApiResult<EventDto>>($"/api/events/{id}");
        Assert.Equal(1, detail!.Data!.RemainingSeats);
        Assert.False(detail.Data.IsFull);
        Assert.Equal(8, detail.Data.RowVersion.Length);
        await Register(id, 3);
        Assert.True((await anonymous.GetFromJsonAsync<ApiResult<EventDto>>($"/api/events/{id}"))!.Data!.IsFull);
        await using var db = new EventPilotDbContext(options);
        var ev = await db.Events.SingleAsync(x => x.Id == id);
        foreach (var status in new[] { EventStatus.Draft, EventStatus.Cancelled })
        {
            ev.Status = status;
            await db.SaveChangesAsync();
            Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/events/{id}")).StatusCode);
            using var other = Client(api, 2);
            Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/events/{id}")).StatusCode);
            using var owner = Client(api, 1);
            Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/events/{id}")).StatusCode);
        }
        ev.IsDeleted = true;
        await db.SaveChangesAsync();
        using var organizer = Client(api, 1);
        Assert.Equal(HttpStatusCode.NotFound, (await organizer.GetAsync($"/api/events/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/events/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Api_publish_and_stale_edits_use_client_version()
    {
        var id = await Event();
        await using (var db = new EventPilotDbContext(options))
        {
            var ev = await db.Events.SingleAsync(x => x.Id == id);
            ev.Status = EventStatus.Draft;
            await db.SaveChangesAsync();
        }
        await using var api = Api();
        using var owner = Client(api, 1);
        var detail = (await owner.GetFromJsonAsync<ApiResult<EventDto>>($"/api/events/{id}"))!.Data!;
        var dto = UpdatedEvent();
        dto.RowVersion = detail.RowVersion;
        dto.Status = EventStatus.Published;
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync("/api/events", new { eventId = id, updateEventDto = dto })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PutAsJsonAsync("/api/events", new { eventId = id, updateEventDto = dto })).StatusCode);
        using var anonymous = Client(api);
        var published = (await anonymous.GetFromJsonAsync<ApiResult<EventDto>>($"/api/events/{id}"))!.Data!;
        Assert.Equal(EventStatus.Published, published.Status);
        Assert.NotEqual(detail.RowVersion, published.RowVersion);
        dto.RowVersion = published.RowVersion;
        dto.Status = EventStatus.Draft;
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PutAsJsonAsync("/api/events", new { eventId = id, updateEventDto = dto })).StatusCode);
        dto.Status = EventStatus.Cancelled;
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync("/api/events", new { eventId = id, updateEventDto = dto })).StatusCode);
        dto.RowVersion = (await owner.GetFromJsonAsync<ApiResult<EventDto>>($"/api/events/{id}"))!.Data!.RowVersion;
        dto.Status = EventStatus.Published;
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PutAsJsonAsync("/api/events", new { eventId = id, updateEventDto = dto })).StatusCode);
    }

    [Fact]
    public async Task Api_attendee_pagination_and_error_envelopes()
    {
        var id = await Event(3);
        await Register(id, 2); await Register(id, 3); await Register(id, 4);
        await using var api = Api();
        using var owner = Client(api, 1);
        using var other = Client(api, 2);
        using var anonymous = Client(api);
        var url = $"/api/events/{id}/registrations";
        await AssertError(await anonymous.GetAsync(url), HttpStatusCode.Unauthorized);
        await AssertError(await other.GetAsync(url), HttpStatusCode.Forbidden);
        await AssertError(await other.DeleteAsync($"/api/events/{id}"), HttpStatusCode.Forbidden);
        await AssertError(await owner.GetAsync(url + "?pageSize=101"), HttpStatusCode.BadRequest);
        await AssertError(await owner.GetAsync(url + "?pageNumber=abc"), HttpStatusCode.BadRequest);
        await AssertError(await anonymous.GetAsync("/api/not-a-route"), HttpStatusCode.NotFound);
        var first = await owner.GetStringAsync(url + "?pageSize=2");
        var second = await owner.GetStringAsync(url + "?pageSize=2&pageNumber=2");
        using var page1 = JsonDocument.Parse(first); using var page2 = JsonDocument.Parse(second);
        Assert.Equal(3, page1.RootElement.GetProperty("data").GetProperty("totalCount").GetInt32());
        var items1 = page1.RootElement.GetProperty("data").GetProperty("items");
        var items2 = page2.RootElement.GetProperty("data").GetProperty("items");
        Assert.Equal(2, items1.GetArrayLength()); Assert.Equal(1, items2.GetArrayLength());
        Assert.DoesNotContain(items2[0].GetProperty("registrationId").GetInt32(), items1.EnumerateArray().Select(x => x.GetProperty("registrationId").GetInt32()));
        await AssertError(await owner.PutAsync("/api/events", new StringContent("{broken", Encoding.UTF8, "application/json")), HttpStatusCode.BadRequest);
        await AssertError(await owner.PutAsJsonAsync("/api/events", new { eventId = id, updateEventDto = UpdatedEvent() }), HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Api_identity_failures_are_validation_or_conflict_not_server_errors()
    {
        await using var api = Api();
        using var client = Client(api);
        var user = new { email = "signup@example.com", password = "StrongPassword1!", firstName = "Test", lastName = "User", avatarUrl = (string?)null };
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/users", user)).StatusCode);
        await AssertError(await client.PostAsJsonAsync("/api/users", user), HttpStatusCode.Conflict);
        await AssertError(await client.PostAsJsonAsync("/api/users", user with { email = "weak@example.com", password = "abcdefgh" }), HttpStatusCode.BadRequest);
        await AssertError(await client.PostAsJsonAsync("/api/auth/reset-password", new { email = user.email, token = "invalid", newPassword = user.password }), HttpStatusCode.BadRequest);
    }

    private static async Task AssertError(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(json.RootElement.GetProperty("isSuccess").GetBoolean());
        Assert.True(json.RootElement.TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task Api_simultaneous_duplicate_signups_return_created_and_conflict()
    {
        await using var api = Api();
        using var client = Client(api);
        var user = new { email = "race@example.com", password = "StrongPassword1!", firstName = "Test", lastName = "User", avatarUrl = (string?)null };
        var responses = await Task.WhenAll(client.PostAsJsonAsync("/api/users", user), client.PostAsJsonAsync("/api/users", user));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Created);
        var conflict = Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
        await AssertError(conflict, HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Api_unexpected_failure_returns_safe_json_in_production()
    {
        await using var api = Api().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<EventPilot.Application.Events.Interfaces.IEventRepository>();
            services.AddScoped<EventPilot.Application.Events.Interfaces.IEventRepository, FailingEventRepository>();
        }));
        using var client = Client(api);
        var response = await client.GetAsync("/api/events");
        await AssertError(response, HttpStatusCode.InternalServerError);
        Assert.Matches("^[0-9a-f]{32}$", response.Headers.GetValues("X-Trace-Id").Single());
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("private-database-diagnostic", body);
        Assert.DoesNotContain("InvalidOperationException", body);
    }

    [Fact]
    public async Task Api_publishing_past_event_and_editing_another_owners_event_are_rejected()
    {
        var id = await Event();
        await using (var db = new EventPilotDbContext(options))
        {
            var ev = await db.Events.SingleAsync(x => x.Id == id);
            ev.Status = EventStatus.Draft;
            await db.SaveChangesAsync();
        }
        await using var api = Api();
        using var owner = Client(api, 1);
        using var other = Client(api, 2);
        var detail = (await owner.GetFromJsonAsync<ApiResult<EventDto>>($"/api/events/{id}"))!.Data!;
        var dto = UpdatedEvent();
        dto.RowVersion = detail.RowVersion;
        dto.Status = EventStatus.Published;
        dto.StartAt = DateTime.UtcNow.AddDays(-2);
        dto.EndAt = DateTime.UtcNow.AddDays(-1);
        await AssertError(await owner.PutAsJsonAsync("/api/events", new { eventId = id, updateEventDto = dto }), HttpStatusCode.Conflict);
        await AssertError(await other.PutAsJsonAsync("/api/events", new { eventId = id, updateEventDto = dto }), HttpStatusCode.Forbidden);
    }
}
