using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using EventPilot.Application.Auth.Commands.Login;
using EventPilot.Application.Auth.Dtos;
using EventPilot.Application.Common.Models;
using EventPilot.Application.Users.Dtos;
using EventPilot.Web.Controllers;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace EventPilot.Tests;

public sealed class AuthControllerTests
{
    [Fact]
    public async Task Successful_login_returns_generated_token_in_response_data()
    {
        var login = new LoginResult
        {
            Succeeded = true, AccessToken = "generated.jwt.token",
            UserId = 42, Email = "attendee@example.com"
        };
        using var cancellation = new CancellationTokenSource();
        var stub = new LoginHandlerStub { Result = login };
        using var services = CreateServices(stub);
        var mediator = services.GetRequiredService<IMediator>();

        var result = await new AuthController(mediator).Login(
            new LoginRequestDto(login.Email, "test-password"), cancellation.Token);

        var response = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<ApiResult<LoginResult>>(response.Value);
        Assert.True(body.IsSuccess);
        Assert.Equal("Login successful", body.Message);
        Assert.Same(login, body.Data);
        Assert.Equal(new LoginCommand(login.Email, "test-password"), stub.Command);
        Assert.Equal(cancellation.Token, stub.CancellationToken);

        // Check the serialized response.
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(body, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var data = json.RootElement.GetProperty("data");
        Assert.Equal(login.AccessToken, data.GetProperty("accessToken").GetString());
        Assert.Equal(42, data.GetProperty("userId").GetInt32());
        Assert.Equal(login.Email, data.GetProperty("email").GetString());
    }

    [Theory]
    [InlineData("Invalid credentials.", "Invalid credentials.")]
    [InlineData(null, "Invalid credentials")]
    public async Task Failed_login_returns_401_without_token(string? error, string expectedMessage)
    {
        var stub = new LoginHandlerStub
        {
            Result = new LoginResult
            {
                Succeeded = false, Error = error, AccessToken = "must-not-be-returned"
            }
        };
        using var services = CreateServices(stub);
        var mediator = services.GetRequiredService<IMediator>();

        var result = await new AuthController(mediator).Login(
            new LoginRequestDto("attendee@example.com", "wrong-password"), default);

        var response = Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal(401, response.StatusCode);
        var body = Assert.IsType<ApiResult>(response.Value);
        Assert.False(body.IsSuccess);
        Assert.Equal(expectedMessage, body.Message);
        Assert.DoesNotContain("must-not-be-returned", JsonSerializer.Serialize(body));
    }

    private static ServiceProvider CreateServices(LoginHandlerStub handler)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatR(config => config.RegisterServicesFromAssemblyContaining<AuthControllerTests>());
        services.AddSingleton<IRequestHandler<LoginCommand, LoginResult>>(handler);
        return services.BuildServiceProvider();
    }

    public sealed class LoginHandlerStub : IRequestHandler<LoginCommand, LoginResult>
    {
        public LoginResult Result { get; set; } = null!;
        public LoginCommand? Command { get; private set; }
        public CancellationToken CancellationToken { get; private set; }

        public Task<LoginResult> Handle(LoginCommand request, CancellationToken ct)
        {
            Command = request;
            CancellationToken = ct;
            return Task.FromResult(Result);
        }
    }
}
