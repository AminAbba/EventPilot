using EventPilot.Application.Common.Models;
using EventPilot.Application.Common.Exceptions;
using EventPilot.Application.Registrations.Common.Exceptions;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using EventPilot.Infrastructure.Persistence;

namespace EventPilot.Web;

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        // The current path may already be /Error.
        var originalPath = context.Features.Get<IExceptionHandlerPathFeature>()?.Path ?? context.Request.Path.Value;
        if (!new PathString(originalPath).StartsWithSegments("/api"))
            return false;

        var status = exception switch
        {
            UnauthorizedAccessException => StatusCodes.Status401Unauthorized,
            ForbiddenException => StatusCodes.Status403Forbidden,
            NotFoundException or KeyNotFoundException => StatusCodes.Status404NotFound,
            AlreadyRegisteredException or BusinessRuleException or DbUpdateConcurrencyException => StatusCodes.Status409Conflict,
            DbUpdateException db when SqlServerErrors.IsUniqueConstraintViolation(db) => StatusCodes.Status409Conflict,
            SqlException { Number: 1205 } => StatusCodes.Status409Conflict,
            DbUpdateException { InnerException: SqlException { Number: 1205 } } => StatusCodes.Status409Conflict,
            ValidationException => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status500InternalServerError
        };
        if (status == StatusCodes.Status500InternalServerError)
        {
            logger.LogError("Unhandled API error {ExceptionType}; stack {StackTrace}",
                exception.GetType().FullName, exception.StackTrace);
        }
        else
        {
            logger.LogWarning("API request rejected with status {StatusCode} and reason {ExceptionType}",
                status, exception.GetType().Name);
        }

        context.Response.StatusCode = status;
        var message = GetMessage(exception, status);
        List<string>? errors = null;
        if (exception is ValidationException validation)
        {
            errors = validation.Errors.Select(x => x.ErrorMessage).Distinct().ToList();
            if (errors.Count == 0)
                errors.Add(validation.Message);
        }

        await context.Response.WriteAsJsonAsync(ApiResult.Failure(message, errors), ct);
        return true;
    }

    private static string GetMessage(Exception exception, int status)
    {
        if (status == StatusCodes.Status500InternalServerError)
            return "An unexpected error occurred.";

        if (exception is ValidationException)
            return "Validation failed.";

        if (exception is DbUpdateConcurrencyException)
            return "Event changed. Reload and retry.";

        if (exception is DbUpdateException update && SqlServerErrors.IsUniqueConstraintViolation(update))
            return "A record with these unique values already exists.";

        if (exception is SqlException || exception.InnerException is SqlException)
            return "The resource is busy. Please retry.";

        return exception.Message;
    }
}
