using System.Diagnostics;
using EventPilot.Application.Common.Diagnostics;

namespace EventPilot.Web.Operations;

public sealed class RequestActivityScope : IDisposable
{
    private readonly Activity? frameworkActivity = System.Diagnostics.Activity.Current;
    public Activity? Activity { get; }

    public RequestActivityScope()
    {
        ActivityContext.TryParse(frameworkActivity?.ParentId, frameworkActivity?.TraceStateString,
            isRemote: true, out var incoming);
        // Use the incoming parent for sampling.
        System.Diagnostics.Activity.Current = null;
        try
        {
            Activity = AppTelemetry.Activities.StartActivity("HTTP request", ActivityKind.Server, incoming);
        }
        finally
        {
            System.Diagnostics.Activity.Current = Activity ?? frameworkActivity;
        }
    }

    public void Dispose()
    {
        Activity?.Dispose();
        System.Diagnostics.Activity.Current = frameworkActivity;
    }
}
