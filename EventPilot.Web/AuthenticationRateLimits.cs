using System.Threading.RateLimiting;
using EventPilot.Application.Common.Models;
using Microsoft.AspNetCore.RateLimiting;

namespace EventPilot.Web;

public static class AuthenticationRateLimits
{
    public static void Configure(RateLimiterOptions options)
    {
        foreach (var (name, limit) in new[] { ("login", 10), ("register", 5), ("recovery", 5) })
        {
            options.AddPolicy(name, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions {
                    PermitLimit = limit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true
                }));
        }
        options.OnRejected = async (context, ct) =>
        {
            context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var wait))
                context.HttpContext.Response.Headers.RetryAfter = Math.Ceiling(wait.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
            await context.HttpContext.Response.WriteAsJsonAsync(ApiResult.Failure("Too many requests. Please retry later."), ct);
        };
    }
}
