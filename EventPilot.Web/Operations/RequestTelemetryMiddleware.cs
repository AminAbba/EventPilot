using System.Diagnostics;
using EventPilot.Application.Common.Diagnostics;
using Serilog.Context;

namespace EventPilot.Web.Operations;

public sealed class RequestTelemetryMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        using var requestActivity = new RequestActivityScope();
        var activity = requestActivity.Activity;
        var traceId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
        var started = Stopwatch.GetTimestamp();

        using var traceProperty = LogContext.PushProperty("TraceId", traceId);
        using var spanProperty = LogContext.PushProperty("SpanId", Activity.Current?.SpanId.ToString());
        context.Response.OnStarting(() =>
        {
            context.Response.Headers["X-Trace-Id"] = traceId;
            return Task.CompletedTask;
        });

        try
        {
            await next(context);
        }
        finally
        {
            var route = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "(unmatched)";
            var method = context.Request.Method;
            if (method is not ("GET" or "POST" or "PUT" or "PATCH" or "DELETE" or "HEAD" or "OPTIONS"))
                method = "OTHER";

            var status = context.Response.StatusCode;
            activity?.SetTag("http.route", route);
            activity?.SetTag("http.request.method", method);
            activity?.SetTag("http.response.status_code", status);
            activity?.SetStatus(status >= 500 ? ActivityStatusCode.Error : ActivityStatusCode.Unset);

            var tags = new TagList
            {
                { "http.route", route },
                { "http.request.method", method },
                { "http.response.status_code", status }
            };
            AppTelemetry.Requests.Add(1, tags);
            AppTelemetry.RequestDuration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds, tags);
        }
    }
}
