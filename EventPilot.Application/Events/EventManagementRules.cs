using EventPilot.Application.Common.Exceptions;
using EventPilot.Application.Events.Dtos;
using EventPilot.Application.Events.Dtos.Validators;
using EventPilot.Application.Registrations.Common.Exceptions;
using EventPilot.Domain.Entities;
using FluentValidation;

namespace EventPilot.Application.Events;

public static class EventManagementRules
{
    public static void EnsureOwner(Event ev, int? userId)
    {
        if (userId is null || userId <= 0)
            throw new UnauthorizedAccessException("Authentication required.");

        if (ev.OrganizerUserId != userId)
            throw new ForbiddenException("You may only manage your own events.");
    }

    public static void ApplyUpdate(Event ev, UpdateEventDto dto, int confirmedCount, int? userId)
    {
        EnsureOwner(ev, userId);
        new UpdateEventDtoValidator().ValidateAndThrow(dto);
        if (!ev.RowVersion.SequenceEqual(dto.RowVersion))
            throw new BusinessRuleException("Event was modified by another request. Reload and retry.");

        var nextStatus = dto.Status ?? ev.Status;
        var reopeningCancelledEvent = ev.Status == EventStatus.Cancelled && nextStatus != EventStatus.Cancelled;
        var unpublishingEvent = ev.Status == EventStatus.Published && nextStatus == EventStatus.Draft;
        if (reopeningCancelledEvent || unpublishingEvent)
            throw new BusinessRuleException("This event status transition is not allowed.");

        var publishingOrRescheduling = ev.Status != EventStatus.Published || ev.StartAt != dto.StartAt;
        if (nextStatus == EventStatus.Published && dto.StartAt <= DateTime.UtcNow && publishingOrRescheduling)
            throw new BusinessRuleException("Publishing or rescheduling requires a future start time.");

        if (dto.Capacity < confirmedCount)
            throw new BusinessRuleException("Capacity cannot be lower than confirmed registrations.");

        ev.Title = dto.Title.Trim();
        ev.Description = dto.Description.Trim();
        ev.Location = dto.Location.Trim();
        ev.StartAt = dto.StartAt;
        ev.EndAt = dto.EndAt;
        ev.Capacity = dto.Capacity;
        ev.Price = dto.Price;
        ev.Status = nextStatus;
        ev.Category = dto.Category ?? ev.Category;
    }
}
