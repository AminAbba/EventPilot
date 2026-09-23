using EventPilot.Application.Registrations.Common.Exceptions;
using EventPilot.Web;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EventPilot.Tests;

public sealed class ApiExceptionHandlerTests
{
    [Fact]
    public async Task Validation_errors_are_distinct_and_keep_the_standard_message()
    {
        using var services = new ServiceCollection().AddLogging().AddOptions().BuildServiceProvider();
        var context = ApiContext(services);
        var error = new ValidationException(new[]
        {
            new ValidationFailure("Title", "Title is required."),
            new ValidationFailure("Title", "Title is required."),
            new ValidationFailure("Capacity", "Capacity must be positive.")
        });

        await Handle(context, error);

        Assert.Equal(400, context.Response.StatusCode);
        using var body = await ReadBody(context);
        Assert.Equal("Validation failed.", body.RootElement.GetProperty("message").GetString());
        Assert.Equal(new[] { "Title is required.", "Capacity must be positive." },
            body.RootElement.GetProperty("errors").EnumerateArray().Select(x => x.GetString()));
    }

    [Fact]
    public async Task Validation_without_field_errors_keeps_its_message_in_errors()
    {
        using var services = new ServiceCollection().AddLogging().AddOptions().BuildServiceProvider();
        var context = ApiContext(services);

        await Handle(context, new ValidationException("Invalid reset request."));

        using var body = await ReadBody(context);
        Assert.Equal("Invalid reset request.", body.RootElement.GetProperty("errors")[0].GetString());
    }

    [Theory]
    [InlineData(true, 409, "Event changed. Reload and retry.")]
    [InlineData(false, 500, "An unexpected error occurred.")]
    public async Task Internal_exception_messages_are_not_exposed(bool concurrency, int status, string message)
    {
        using var services = new ServiceCollection().AddLogging().AddOptions().BuildServiceProvider();
        var context = ApiContext(services);
        Exception error = concurrency
            ? new DbUpdateConcurrencyException("private database details")
            : new InvalidOperationException("private database details");

        await Handle(context, error);

        Assert.Equal(status, context.Response.StatusCode);
        using var body = await ReadBody(context);
        Assert.Equal(message, body.RootElement.GetProperty("message").GetString());
        Assert.DoesNotContain("private database details", body.RootElement.ToString());
    }

    [Fact]
    public async Task Reexecuted_api_errors_use_the_original_path()
    {
        using var services = new ServiceCollection().AddLogging().AddOptions().BuildServiceProvider();
        var context = ApiContext(services);
        context.Request.Path = "/Error";
        context.Features.Set<IExceptionHandlerPathFeature>(new ExceptionHandlerFeature
        {
            Path = "/api/events",
            Error = new InvalidOperationException()
        });

        Assert.True(await Handle(context, new NotFoundException("Event not found.")));
        Assert.Equal(404, context.Response.StatusCode);
    }

    [Fact]
    public async Task Page_errors_are_left_to_the_page_handler()
    {
        using var services = new ServiceCollection().AddLogging().AddOptions().BuildServiceProvider();
        var context = ApiContext(services);
        context.Request.Path = "/Login";

        Assert.False(await Handle(context, new InvalidOperationException()));
        Assert.Equal(0, context.Response.Body.Length);
    }

    private static DefaultHttpContext ApiContext(IServiceProvider services)
    {
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Path = "/api/events";
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static ValueTask<bool> Handle(HttpContext context, Exception error) =>
        new ApiExceptionHandler(NullLogger<ApiExceptionHandler>.Instance).TryHandleAsync(context, error, default);

    private static Task<JsonDocument> ReadBody(HttpContext context)
    {
        context.Response.Body.Position = 0;
        return JsonDocument.ParseAsync(context.Response.Body);
    }

    [Theory]
    [InlineData(409)]
    [InlineData(404)]
    [InlineData(401)]
    public async Task Registration_failures_have_expected_json_status(int expected)
    {
        using var services = new ServiceCollection().AddLogging().AddOptions().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Path = "/api/events/test/registrations";
        context.Response.Body = new MemoryStream();
        Exception error = expected switch {
            409 => new BusinessRuleException("Event is full."),
            404 => new NotFoundException("Registration not found."),
            _ => new UnauthorizedAccessException("Authentication required.")
        };
        Assert.True(await new ApiExceptionHandler(NullLogger<ApiExceptionHandler>.Instance)
            .TryHandleAsync(context, error, default));
        Assert.Equal(expected, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.Contains("\"isSuccess\":false", body);
        Assert.Contains(error.Message, body);
    }
}
