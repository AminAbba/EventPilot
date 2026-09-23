using EventPilot.Application.Events.Dtos.Validators;
using FluentValidation;

namespace EventPilot.Application.Events.Commands.UpdateEvent;

public sealed class UpdateEventCommandValidator : AbstractValidator<UpdateEventCommand>
{
    public UpdateEventCommandValidator()
    {
        RuleFor(x => x.eventId).NotEmpty();
        RuleFor(x => x.updateEventDto).NotNull().SetValidator(new UpdateEventDtoValidator());
    }
}
