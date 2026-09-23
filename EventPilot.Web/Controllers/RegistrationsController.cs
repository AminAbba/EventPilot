using EventPilot.Application.Common.Models;
using EventPilot.Application.Registrations.Commands.CreateRegistration;
using EventPilot.Application.Registrations.Commands.CancelRegistration;
using EventPilot.Application.Registrations.Dtos;
using EventPilot.Application.Registrations.Queries.ListEventRegistrations;
using MediatR;
using EventPilot.Application.Abstractions.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using EventPilot.Web.Auth;

namespace EventPilot.Web.Controllers;

[Authorize]
[ApiController]
[Route("api/events/{eventId:guid}/registrations")]
public sealed class RegistrationsController : ControllerBase
{
    private readonly IMediator _mediator;

    public RegistrationsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost()]
    public async Task<IActionResult> RegisterForEvent(
    [FromRoute] Guid eventId,
    CancellationToken ct)
    {
        var command = new CreateRegistrationCommand(eventId);

        var result = await _mediator.Send(command, ct);


        return Ok(ApiResult<RegistrationResponse>.Success(result, "Registration confirmed."));
    }
    [HttpDelete("me")]
    public async Task<IActionResult> Cancel([FromRoute] Guid eventId, CancellationToken ct)
    {
        var result = await _mediator.Send(new CancelRegistrationCommand(eventId), ct);
        return Ok(ApiResult<RegistrationResponse>.Success(result, "Registration cancelled."));
    }

    [Authorize(Policy = AuthorizationPolicies.CanManageEvents)]
    [HttpGet()]
    [ProducesResponseType(typeof(ApiResult<PagedResult<ListEventRegistrationsDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListForEvent([FromRoute] Guid eventId, CancellationToken ct,
        [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20)
    {
        var result = await _mediator.Send(
            new ListEventRegistrationsQuery(eventId, pageNumber, pageSize), ct);

        return Ok(ApiResult<PagedResult<ListEventRegistrationsDto>>.Success(result));
    }
}
