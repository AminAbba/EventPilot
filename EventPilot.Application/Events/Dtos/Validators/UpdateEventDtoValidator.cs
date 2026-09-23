using FluentValidation;

namespace EventPilot.Application.Events.Dtos.Validators;

public sealed class UpdateEventDtoValidator : AbstractValidator<UpdateEventDto>
{
    public UpdateEventDtoValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).NotEmpty().MaximumLength(1000);
        RuleFor(x => x.Location).NotEmpty().MaximumLength(50);
        RuleFor(x => x.StartAt).NotEmpty();
        RuleFor(x => x.StartAt).Must(value => value.Kind == DateTimeKind.Utc).WithMessage("StartAt must be UTC.");
        RuleFor(x => x.EndAt).Must(value => value.Kind == DateTimeKind.Utc).WithMessage("EndAt must be UTC.");
        RuleFor(x => x.EndAt).GreaterThan(x => x.StartAt);
        RuleFor(x => x.Capacity).GreaterThan(0);
        RuleFor(x => x.Price).InclusiveBetween(0, 9999999999999999.99m).PrecisionScale(18, 2, true);
        RuleFor(x => x.RowVersion).Must(value => value is { Length: 8 })
            .WithMessage("RowVersion must be the eight-byte version returned by the API (base64 in JSON).");
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.Category).IsInEnum();
    }
}
