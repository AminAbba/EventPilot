using EventPilot.Application.Abstractions.Auth;
using EventPilot.Application.Abstractions.Models;
using EventPilot.Application.Registrations.Interfaces;
using EventPilot.Domain.Entities;
using EventPilot.Domain.Enums;
using FluentValidation;
using MediatR;

namespace EventPilot.Application.Registrations.Queries;

public sealed record MyRegistrationDto(int RegistrationId, Guid EventId, string EventTitle, string Location,
    DateTime StartAt, DateTime EndAt, DateTime RegisteredAt, RegistrationStatus Status,
    EventStatus EventStatus, bool EventIsDeleted);
public sealed record ListMyRegistrationsQuery(int PageNumber = 1, int PageSize = 20) : IRequest<PagedResult<MyRegistrationDto>>;

public sealed class ListMyRegistrationsHandler(IRegistrationRepository registrations, ICurrentUser currentUser)
    : IRequestHandler<ListMyRegistrationsQuery, PagedResult<MyRegistrationDto>>
{
    public Task<PagedResult<MyRegistrationDto>> Handle(ListMyRegistrationsQuery request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not int userId || userId <= 0)
            throw new UnauthorizedAccessException("Authentication required.");
        return registrations.ListByUserAsync(userId, request.PageNumber, request.PageSize, ct);
    }
}
public sealed class ListMyRegistrationsValidator : AbstractValidator<ListMyRegistrationsQuery>
{
    public ListMyRegistrationsValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x).Must(x => ((long)x.PageNumber - 1) * x.PageSize <= int.MaxValue)
            .WithMessage("The requested page is too large.");
    }
}
