using EventPilot.Application.Auth.Commands.ForgotPassword;
using EventPilot.Application.Auth.Commands.Login;
using EventPilot.Application.Auth.Commands.ResetPassword;
using EventPilot.Application.Auth.Dtos;
using EventPilot.Application.Common.Models;
using EventPilot.Application.Users.Dtos;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using EventPilot.Application.Auth.Commands.Logout;
using EventPilot.Application.Users.Commands.CreateUser;

namespace EventPilot.Web.Controllers;

[ApiController]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IMediator _mediator;

    public AuthController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost("login")]
    [EnableRateLimiting("login")]
    [ProducesResponseType(typeof(ApiResult<LoginResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResult), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequestDto req, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new LoginCommand(req.Email, req.Password), ct);

        if (!result.Succeeded)
            return Unauthorized(ApiResult.Failure(result.Error ?? "Invalid credentials"));

        return Ok(ApiResult<LoginResult>.Success(result, "Login successful"));
    }

    [HttpPost("forgot-password")]
    [EnableRateLimiting("recovery")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequestDto req, CancellationToken ct)
    {
        await _mediator.Send(new ForgotPasswordCommand(req.Email), ct);
        return Ok(ApiResult.Success("If the email exists, a reset link has been sent."));
    }

    [HttpPost("reset-password")]
    [EnableRateLimiting("recovery")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequestDto req, CancellationToken ct)
    {
        await _mediator.Send(
            new ResetPasswordCommand(req.Email, req.Token, req.NewPassword), ct);

        return Ok(ApiResult.Success("Password has been reset successfully."));
    }

    [HttpPost("register")]
    [EnableRateLimiting("register")]
    [ProducesResponseType(typeof(ApiResult<int>), StatusCodes.Status201Created)]
    public async Task<IActionResult> Register(CreateUserCommand command, CancellationToken ct)
    {
        var id = await _mediator.Send(command, ct);
        return Created("/api/users/me", ApiResult<int>.Success(id, "Account created. Log in to continue."));
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await _mediator.Send(new LogoutCommand(), ct);
        return Ok(ApiResult.Success("Logged out of all sessions."));
    }
}
