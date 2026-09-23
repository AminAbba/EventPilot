using EventPilot.Application.Events.Dtos.Validators;
using FluentValidation;

namespace EventPilot.Application.Events.Commands.CreateEvent
{

    public class CreateEventCommandValidator
        : AbstractValidator<CreateEventCommand>
    {
        public CreateEventCommandValidator()
        {
            RuleFor(x => x.ev)
                .NotNull().SetValidator(new CreateEventDtoValidator());
        }
    }
}
