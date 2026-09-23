using FluentValidation;

namespace EventPilot.Application.Registrations.Queries.ListEventRegistrations;

public sealed class ListEventRegistrationsQueryValidator : AbstractValidator<ListEventRegistrationsQuery>
{
    public ListEventRegistrationsQueryValidator()
    {
        RuleFor(x => x.EventId).NotEmpty();
        RuleFor(x => x.PageNumber).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x).Must(x => ((long)x.PageNumber - 1) * x.PageSize <= int.MaxValue)
            .WithMessage("The requested page is too large.");
    }
}
