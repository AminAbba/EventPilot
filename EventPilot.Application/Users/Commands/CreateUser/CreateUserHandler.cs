using EventPilot.Application.Abstractions;
using MediatR;

namespace EventPilot.Application.Users.Commands.CreateUser;

public sealed class CreateUserHandler : IRequestHandler<CreateUserCommand, int>
{
    private readonly IIdentityService _identity;

    public CreateUserHandler(IIdentityService identity)
    {
        _identity = identity;
    }

    public Task<int> Handle(CreateUserCommand request, CancellationToken ct)
        => _identity.CreateUserAsync(request.Email, request.Password, request.FirstName, request.LastName,request.AvatarUrl, ct);
}
