using FluentValidation;

namespace EventPilot.Application.Events.Dtos.Validators
{
    public class CreateEventDtoValidator
        : AbstractValidator<CreateEventDto>
    {
        public CreateEventDtoValidator()
        {
            RuleFor(x => x.Title)
                .NotEmpty()
                .MaximumLength(100);

            RuleFor(x => x.Description)
                .NotEmpty()
                .MaximumLength(1000);

            RuleFor(x => x.Location)
                .NotEmpty().MaximumLength(50);

            RuleFor(x => x.Capacity)
                .GreaterThan(0);

            RuleFor(x => x.StartAt)
                .Must(value => value > DateTime.UtcNow).WithMessage("StartAt must be in the future.");
            RuleFor(x => x.StartAt).Must(value => value.Kind == DateTimeKind.Utc).WithMessage("StartAt must be UTC.");
            RuleFor(x => x.EndAt).Must(value => value.Kind == DateTimeKind.Utc).WithMessage("EndAt must be UTC.");

            RuleFor(x => x.Price).InclusiveBetween(0, 9999999999999999.99m).PrecisionScale(18, 2, true);
            RuleFor(x => x.Status).IsInEnum();
            RuleFor(x => x.Category).IsInEnum();

            RuleFor(x => x.EndAt)
                .GreaterThan(x => x.StartAt);
        }
    }
}
