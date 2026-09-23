using EventPilot.Application.Abstractions;
using MediatR;

namespace EventPilot.Application.Auth.Commands.ResetPassword;

public sealed class ResetPasswordHandler : IRequestHandler<ResetPasswordCommand>
{
    private readonly IIdentityService _identity;

    public ResetPasswordHandler(IIdentityService identity)
    {
        _identity = identity;
    }

    public async Task Handle(ResetPasswordCommand request, CancellationToken ct)
    {
        await _identity.ResetPasswordAsync(request.Email, request.Token, request.NewPassword, ct);
    }
}
