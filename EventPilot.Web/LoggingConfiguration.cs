using Serilog;
using Serilog.Events;
using Serilog.Formatting.Json;
using Serilog.AspNetCore;
using System.Diagnostics;

namespace EventPilot.Web;

public static class LoggingConfiguration
{
    public static void ConfigureRequestLogging(RequestLoggingOptions options)
    {
        options.GetMessageTemplateProperties = (context, path, elapsed, status) =>
        [
            new LogEventProperty("RequestMethod", new ScalarValue(context.Request.Method)),
            new LogEventProperty("RequestPath", new ScalarValue(GetRoute(context))),
            new LogEventProperty("StatusCode", new ScalarValue(status)),
            new LogEventProperty("Elapsed", new ScalarValue(elapsed))
        ];
        options.GetLevel = (context, elapsed, error) =>
        {
            if (context.Response.StatusCode >= 500)
                return LogEventLevel.Error;
            if (context.Response.StatusCode >= 400)
                return LogEventLevel.Warning;
            return LogEventLevel.Information;
        };
        options.EnrichDiagnosticContext = (diagnostic, context) =>
        {
            // Log route templates to avoid user-supplied URLs.
            diagnostic.Set("RequestPath", GetRoute(context));
            diagnostic.Set("TraceId", Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier);
        };
    }

    private static string GetRoute(HttpContext context) =>
        (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "(unmatched)";

    public static void Configure(LoggerConfiguration logger, IServiceProvider services, IConfiguration configuration)
    {
        var level = Enum.TryParse<LogEventLevel>(configuration["Logging:MinimumLevel"], true, out var configured)
            ? configured : LogEventLevel.Information;
        logger.MinimumLevel.Is(level)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            // These logs can include SQL values or tokens.
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Fatal)
            .MinimumLevel.Override("Microsoft.AspNetCore.Diagnostics", LogEventLevel.Fatal)
            .MinimumLevel.Override("Microsoft.AspNetCore.Authentication", LogEventLevel.Fatal)
            .MinimumLevel.Override("Microsoft.IdentityModel", LogEventLevel.Fatal)
            .Enrich.FromLogContext().Enrich.WithProperty("Application", "EventPilot")
            .ReadFrom.Services(services)
            .WriteTo.Console(new JsonFormatter(renderMessage: true));
        if (configuration.GetValue("Logging:FileEnabled", true))
            logger.WriteTo.File(new JsonFormatter(renderMessage: true), "logs/eventpilot-.json",
                rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14,
                fileSizeLimitBytes: 10_000_000, rollOnFileSizeLimit: true, shared: true);
    }
}
