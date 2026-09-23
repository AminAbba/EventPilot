using EventPilot.Application.Abstractions;
using EventPilot.Application.Users.Dtos;
using MediatR;

namespace EventPilot.Application.Auth.Commands.Login;

public sealed class LoginHandler : IRequestHandler<LoginCommand, LoginResult>
{
    private readonly IIdentityService _identity;

    public LoginHandler(IIdentityService identity)
    {
        _identity = identity;
    }

    public async Task<LoginResult> Handle(LoginCommand request, CancellationToken ct)
    {
        return await _identity.LoginAsync( request.Email,request.Password,ct);
    }
}
