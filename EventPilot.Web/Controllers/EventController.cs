using EventPilot.Application.Abstractions.Models;
using EventPilot.Application.Common.Models;
using EventPilot.Application.Events.Commands.CreateEvent;
using EventPilot.Application.Events.Commands.DeleteEvent;
using EventPilot.Application.Events.Commands.UpdateEvent;
using EventPilot.Application.Events.Dtos;
using EventPilot.Application.Events.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using EventPilot.Web.Auth;

namespace EventPilot.Web.Controllers;

[Authorize(Policy = AuthorizationPolicies.CanManageEvents)]
[ApiController]
[Route("api/events")]
public sealed class EventsController : ControllerBase
{
    private readonly IMediator _mediator;

    public EventsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateEventCommand command, CancellationToken ct)
    {
        var id = await _mediator.Send(command, ct);

        return CreatedAtAction(nameof(GetById), new { id }, ApiResult<Guid>.Success(id, "Event created successfully"));
    }

    [AllowAnonymous]
    [HttpGet]
    [ProducesResponseType(typeof(ApiResult<PagedResult<EventDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] ListEventQuery query, CancellationToken ct)
    {
        var result = await _mediator.Send(query, ct);

        return Ok(ApiResult<PagedResult<EventDto>>.Success(result));
    }

    [HttpGet("mine")]
    [ProducesResponseType(typeof(ApiResult<PagedResult<EventDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Mine([FromQuery] ListMyEventsQuery query, CancellationToken ct)
    {
        var result = await _mediator.Send(query, ct);
        return Ok(ApiResult<PagedResult<EventDto>>.Success(result));
    }

    [AllowAnonymous]
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResult<EventDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetById([FromRoute] Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetEventByIdQuery(id), ct);
        return Ok(ApiResult<EventDto>.Success(result));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteEvent(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new DeleteEventCommand(id), ct);

        return Ok(ApiResult.Success("Event deleted successfully"));
    }

    [HttpPut]
    public async Task<IActionResult> UpdateEvent([FromBody] UpdateEventCommand command, CancellationToken ct)
    {
        await _mediator.Send(command, ct);

        return Ok(ApiResult.Success("Event updated successfully"));
    }
}
