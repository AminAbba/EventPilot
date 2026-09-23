using FluentValidation;

namespace EventPilot.Application.Users.Commands.UpdateUser;

public sealed class UpdateUserCommandValidator : AbstractValidator<UpdateUserCommand>
{
    public UpdateUserCommandValidator()
    {
        RuleFor(x => x.updateUserProfileDto).NotNull();
        When(x => x.updateUserProfileDto is not null, () =>
        {
            RuleFor(x => x.updateUserProfileDto.FirstName).NotEmpty().MaximumLength(100)
                .When(x => x.updateUserProfileDto.FirstName is not null);
            RuleFor(x => x.updateUserProfileDto.LastName).NotEmpty().MaximumLength(100)
                .When(x => x.updateUserProfileDto.LastName is not null);
            RuleFor(x => x.updateUserProfileDto.PhoneNumber).NotEmpty().MaximumLength(30)
                .Matches(@"^\+?(?=(?:\D*\d){5,})[0-9 ()-]{5,30}$").When(x => x.updateUserProfileDto.PhoneNumber is not null);
        });
    }
}
