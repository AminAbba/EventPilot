using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace EventPilot.Application.Common.Diagnostics;

public static class AppTelemetry
{
    public const string Name = "EventPilot";
    public static readonly ActivitySource Activities = new(Name);
    public static readonly Meter Meter = new(Name);
    public static readonly Histogram<double> RequestDuration = Meter.CreateHistogram<double>("eventpilot.http.duration", "s");
    public static readonly Counter<long> Requests = Meter.CreateCounter<long>("eventpilot.http.requests");
    public static readonly Counter<long> CacheRequests = Meter.CreateCounter<long>("eventpilot.cache.requests");
    public static readonly Counter<long> SeatRetries = Meter.CreateCounter<long>("eventpilot.registration.retries");
}
