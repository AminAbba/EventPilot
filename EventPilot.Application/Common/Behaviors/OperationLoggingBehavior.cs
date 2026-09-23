using System.Diagnostics;
using EventPilot.Application.Abstractions.Auth;
using MediatR;
using Microsoft.Extensions.Logging;

namespace EventPilot.Application.Common.Behaviors;

public sealed class OperationLoggingBehavior<TRequest, TResponse>(ILogger<OperationLoggingBehavior<TRequest, TResponse>> logger,
    ICurrentUser currentUser) : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var start = Stopwatch.GetTimestamp();
        var operation = typeof(TRequest).Name;
        using var activity = EventPilot.Application.Common.Diagnostics.AppTelemetry.Activities.StartActivity(operation);
        try
        {
            var result = await next();
            // Requests and results may contain passwords or tokens.
            logger.LogInformation("Operation {Operation} succeeded for user {UserId} in {ElapsedMs} ms",
                operation, currentUser.UserId, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            activity?.SetTag("error.type", ex.GetType().Name);
            logger.LogWarning("Operation {Operation} failed for user {UserId}; failure type {FailureType}",
                operation, currentUser.UserId, ex.GetType().Name);
            throw;
        }
    }
}
