using EventPilot.Application.Abstractions;
using EventPilot.Application.Abstractions.Auth;
using MediatR;

namespace EventPilot.Application.Auth.Commands.Logout;

public sealed record LogoutCommand : IRequest;

public sealed class LogoutHandler(IIdentityService identity, ICurrentUser currentUser) : IRequestHandler<LogoutCommand>
{
    public Task Handle(LogoutCommand request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not int id || id <= 0)
            throw new UnauthorizedAccessException("Authentication required.");
        return identity.LogoutAsync(id, ct);
    }
}
