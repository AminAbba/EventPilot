using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EventPilot.Application.Abstractions.Email;
using EventPilot.Application.Common.Models;
using EventPilot.Application.Users.Dtos;
using EventPilot.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace EventPilot.Tests;

public sealed partial class RegistrationCapacityTests
{
    private WebApplicationFactory<Program> AccountApi(CapturedMail mail, CapturedLogs? logs = null) =>
        Api().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(mail);
            if (logs is not null) services.AddSingleton<ILogEventSink>(logs);
        }));

    private static async Task RegisterAccount(HttpClient client, string email = "account@example.com", string password = "StrongPassword1!")
    {
        var response = await client.PostAsJsonAsync("/api/auth/register", new { email, password, firstName = "First", lastName = "Last" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
    private static async Task<LoginResult> LoginAccount(HttpClient client, string password = "StrongPassword1!", string email = "account@example.com")
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<ApiResult<LoginResult>>())!.Data!;
        Assert.True(result.ExpiresAtUtc > DateTime.UtcNow);
        Assert.Equal("Bearer", result.TokenType);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        return result;
    }
    private static void Authorize(HttpClient client, string token) => client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    private static string ResetToken(CapturedMail mail)
    {
        var link = mail.Messages.Last().Split('\n').Single(x => x.StartsWith("https://"));
        var uri = new Uri(link);
        Assert.Equal("localhost", uri.Host);
        return QueryHelpers.ParseQuery(uri.Query)["token"].ToString();
    }

    [Fact]
    public async Task Account_signup_login_reset_and_logout_work_end_to_end_and_revoke_old_tokens()
    {
        var mail = new CapturedMail();
        await using var api = AccountApi(mail);
        using var client = Client(api);
        await RegisterAccount(client);
        var before = await LoginAccount(client);
        Authorize(client, before.AccessToken!);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/users/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/forgot-password", new { email = "account@example.com" })).StatusCode);
        var resetToken = ResetToken(mail);
        const string newPassword = "ReplacementPassword2!";
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/reset-password", new { email = "account@example.com", token = resetToken, newPassword })).StatusCode);
        await AssertError(await client.GetAsync("/api/users/me"), HttpStatusCode.Unauthorized);
        await AssertError(await client.PostAsJsonAsync("/api/auth/login", new { email = "account@example.com", password = "StrongPassword1!" }), HttpStatusCode.Unauthorized);
        await AssertError(await client.PostAsJsonAsync("/api/auth/reset-password", new { email = "account@example.com", token = resetToken, newPassword }), HttpStatusCode.BadRequest);
        var after = await LoginAccount(client, newPassword);
        using var secondDevice = Client(api);
        var second = await LoginAccount(secondDevice, newPassword);
        Authorize(client, after.AccessToken!); Authorize(secondDevice, second.AccessToken!);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
        await AssertError(await client.GetAsync("/api/users/me"), HttpStatusCode.Unauthorized);
        await AssertError(await secondDevice.GetAsync("/api/users/me"), HttpStatusCode.Unauthorized);
        var fresh = await LoginAccount(client, newPassword);
        Authorize(client, fresh.AccessToken!);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/users/me")).StatusCode);
    }

    [Fact]
    public async Task Account_recovery_failure_is_generic_rate_limited_and_does_not_log_secrets()
    {
        const string secret = "NeverLogThisPassword1!";
        var logs = new CapturedLogs();
        var mail = new CapturedMail { Fail = true };
        await using var api = AccountApi(mail, logs);
        using var client = Client(api);
        await RegisterAccount(client, password: secret);
        var login = await LoginAccount(client, secret);
        var known = await client.PostAsJsonAsync("/api/auth/forgot-password", new { email = "account@example.com" });
        var unknown = await client.PostAsJsonAsync("/api/auth/forgot-password", new { email = "unknown@example.com" });
        Assert.Equal(HttpStatusCode.OK, known.StatusCode);
        Assert.Equal(await known.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());
        for (var i = 0; i < 3; i++) await client.PostAsJsonAsync("/api/auth/forgot-password", new { email = "unknown@example.com" });
        var limited = await client.PostAsJsonAsync("/api/auth/forgot-password", new { email = "unknown@example.com" });
        await AssertError(limited, HttpStatusCode.TooManyRequests);
        Assert.NotNull(limited.Headers.RetryAfter);
        Authorize(client, login.AccessToken!);
        await client.GetAsync("/api/users/me?token=query-secret-marker");
        await client.GetAsync("/api/path-secret-marker");
        var output = string.Join('\n', logs.Events.Select(x => x.RenderMessage() + " " + x.Exception));
        Assert.Contains("Password reset delivery failed", output);
        Assert.DoesNotContain(secret, output);
        Assert.DoesNotContain(login.AccessToken!, output);
        Assert.DoesNotContain(ResetToken(mail), output);
        Assert.DoesNotContain("smtp-secret-marker", output);
        Assert.DoesNotContain("query-secret-marker", output);
        Assert.DoesNotContain("path-secret-marker", output);
        Assert.DoesNotContain("account@example.com", output);
        Assert.Contains(logs.Events, x => x.Properties.ContainsKey("TraceId") && x.Properties.ContainsKey("Elapsed"));
    }

    [Fact]
    public async Task Account_invalid_and_expired_reset_tokens_are_rejected()
    {
        var mail = new CapturedMail();
        await using var api = AccountApi(mail).WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.Configure<DataProtectionTokenProviderOptions>(options => options.TokenLifespan = TimeSpan.Zero)));
        using var client = Client(api);
        await RegisterAccount(client);
        await client.PostAsJsonAsync("/api/auth/forgot-password", new { email = "account@example.com" });
        var token = ResetToken(mail);
        await AssertError(await client.PostAsJsonAsync("/api/auth/reset-password", new { email = "account@example.com", token, newPassword = "ReplacementPassword2!" }), HttpStatusCode.BadRequest);
        await AssertError(await client.PostAsJsonAsync("/api/auth/reset-password", new { email = "account@example.com", token = "!invalid!", newPassword = "ReplacementPassword2!" }), HttpStatusCode.BadRequest);
        await LoginAccount(client);
    }

    [Fact]
    public async Task Account_profile_validates_fields_and_preserves_omitted_values()
    {
        await using var api = AccountApi(new CapturedMail());
        using var client = Client(api);
        await RegisterAccount(client);
        Authorize(client, (await LoginAccount(client)).AccessToken!);
        await AssertError(await client.PatchAsJsonAsync("/api/users/me", new { firstName = " " }), HttpStatusCode.BadRequest);
        await AssertError(await client.PatchAsJsonAsync("/api/users/me", new { lastName = new string('x', 101) }), HttpStatusCode.BadRequest);
        await AssertError(await client.PatchAsJsonAsync("/api/users/me", new { phoneNumber = "not-a-phone" }), HttpStatusCode.BadRequest);
        Assert.Equal(HttpStatusCode.OK, (await client.PatchAsJsonAsync("/api/users/me", new { firstName = " Updated " })).StatusCode);
        var me = (await client.GetFromJsonAsync<ApiResult<MeDto>>("/api/users/me"))!.Data!;
        Assert.Equal("Updated", me.FirstName); Assert.Equal("Last", me.LastName);
        await AssertError(await client.GetAsync("/api/users/999999"), HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Account_lockout_rejects_login_after_repeated_failures()
    {
        await using var api = AccountApi(new CapturedMail());
        using var client = Client(api);
        await RegisterAccount(client);
        for (var i = 0; i < 5; i++)
            await AssertError(await client.PostAsJsonAsync("/api/auth/login", new { email = "account@example.com", password = "wrong" }), HttpStatusCode.Unauthorized);
        await AssertError(await client.PostAsJsonAsync("/api/auth/login", new { email = "account@example.com", password = "StrongPassword1!" }), HttpStatusCode.Unauthorized);
    }

    private sealed class CapturedMail : IEmailSender
    {
        public bool Fail;
        public ConcurrentQueue<string> Messages { get; } = new();
        public Task SendAsync(string to, string subject, string body, CancellationToken ct)
        {
            Messages.Enqueue(body);
            if (Fail) throw new InvalidOperationException("smtp-secret-marker " + body);
            return Task.CompletedTask;
        }
    }
    [Fact]
    public async Task Account_swagger_marks_only_protected_operations_as_requiring_bearer()
    {
        await using var api = AccountApi(new CapturedMail());
        using var client = Client(api);
        using var document = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json"));
        var paths = document.RootElement.GetProperty("paths");
        Assert.False(paths.GetProperty("/api/auth/login").GetProperty("post").TryGetProperty("security", out _));
        Assert.False(paths.GetProperty("/api/auth/register").GetProperty("post").TryGetProperty("security", out _));
        Assert.NotEqual(0, paths.GetProperty("/api/auth/logout").GetProperty("post").GetProperty("security").GetArrayLength());
        Assert.NotEqual(0, paths.GetProperty("/api/users/me").GetProperty("get").GetProperty("security").GetArrayLength());
    }
    private sealed class CapturedLogs : ILogEventSink
    {
        public ConcurrentQueue<LogEvent> Events { get; } = new();
        public void Emit(LogEvent logEvent) => Events.Enqueue(logEvent);
    }
}
