using EventPilot.Application.Abstractions;
using EventPilot.Application.Abstractions.Auth;
using MediatR;
using Microsoft.Extensions.Options;

namespace EventPilot.Application.Auth.Commands.ForgotPassword;

public sealed class ForgotPasswordHandler : IRequestHandler<ForgotPasswordCommand>
{
    private readonly IIdentityService _identity;
    private readonly PasswordResetOptions _opt;

    public ForgotPasswordHandler(IIdentityService identity, IOptions<PasswordResetOptions> opt)
    {
        _identity = identity;
        _opt = opt.Value;
    }

    public async Task Handle(ForgotPasswordCommand request, CancellationToken ct)
    {
        await _identity.SendPasswordResetLinkAsync(request.Email, _opt.ResetLinkBase, ct);
    }
}
