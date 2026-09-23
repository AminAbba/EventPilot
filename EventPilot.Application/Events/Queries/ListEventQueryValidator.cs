using FluentValidation;

namespace EventPilot.Application.Events.Queries;

public sealed class ListEventQueryValidator : AbstractValidator<ListEventQuery>
{
    public ListEventQueryValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x).Must(x => ((long)x.PageNumber - 1) * x.PageSize <= int.MaxValue)
            .WithMessage("The requested page is too large.");
        RuleFor(x => x.Search).MaximumLength(100);
        RuleFor(x => x.Price).InclusiveBetween(0, 9999999999999999.99m).PrecisionScale(18, 2, true);
    }
}

public sealed class ListMyEventsQueryValidator : AbstractValidator<ListMyEventsQuery>
{
    public ListMyEventsQueryValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x).Must(x => ((long)x.PageNumber - 1) * x.PageSize <= int.MaxValue)
            .WithMessage("The requested page is too large.");
        RuleFor(x => x.Search).MaximumLength(100);
        RuleFor(x => x.Price).InclusiveBetween(0, 9999999999999999.99m).PrecisionScale(18, 2, true);
    }
}

