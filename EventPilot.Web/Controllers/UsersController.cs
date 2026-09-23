using EventPilot.Application.Common.Models;
using EventPilot.Application.Users.Commands.CreateUser;
using EventPilot.Application.Abstractions.Models;
using EventPilot.Application.Registrations.Queries;
using EventPilot.Application.Users.Commands.UpdateUser;
using EventPilot.Application.Users.Dtos;
using EventPilot.Application.Users.Queries.GetMe;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using EventPilot.Application.Users.Commands.BecomeOrganizer;

namespace EventPilot.Web.Controllers;

[ApiController]
[Route("api/users")]
public sealed class UsersController : ControllerBase
{
    private readonly IMediator _mediator;

    public UsersController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [Authorize]
    [HttpPost("me/organizer")]
    [ProducesResponseType(typeof(ApiResult<OrganizerEnrollmentResult>), StatusCodes.Status200OK)]
    public async Task<IActionResult> BecomeOrganizer(CancellationToken ct)
    {
        var result = await _mediator.Send(new BecomeOrganizerCommand(), ct);
        return Ok(ApiResult<OrganizerEnrollmentResult>.Success(result, result.RequiresLogin
            ? "Organizer role granted. Log in again to use organizer features; previous sessions were revoked."
            : "You are already an organizer."));
    }

    [Authorize]
    [HttpGet("me/registrations")]
    public async Task<IActionResult> Registrations([FromQuery] ListMyRegistrationsQuery query, CancellationToken ct)
    {
        var result = await _mediator.Send(query, ct);
        return Ok(ApiResult<PagedResult<MyRegistrationDto>>.Success(result));
    }

    [HttpPost]
    [EnableRateLimiting("register")]
    public async Task<IActionResult> Create([FromBody] CreateUserCommand command, CancellationToken ct)
    {
        var userId = await _mediator.Send(command, ct);

        return Created("/api/users/me", ApiResult<int>.Success(userId, "Account created. Log in to continue."));
    }


    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var me = await _mediator.Send(new GetMeQuery(), ct);
        return Ok(ApiResult<MeDto>.Success(me));
    }

    [Authorize]
    [HttpPatch("me")]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateUserProfileDto dto, CancellationToken ct)
    {
        await _mediator.Send(new UpdateUserCommand(dto), ct);

        return Ok(ApiResult.Success("Profile updated successfully"));
    }
}
