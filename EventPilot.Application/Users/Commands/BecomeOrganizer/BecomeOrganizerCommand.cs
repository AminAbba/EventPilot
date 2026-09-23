using EventPilot.Application.Abstractions;
using EventPilot.Application.Abstractions.Auth;
using MediatR;

namespace EventPilot.Application.Users.Commands.BecomeOrganizer;

public sealed record OrganizerEnrollmentResult(bool RoleChanged, bool RequiresLogin);
public sealed record BecomeOrganizerCommand : IRequest<OrganizerEnrollmentResult>;

public sealed class BecomeOrganizerHandler(IIdentityService identity, ICurrentUser currentUser)
    : IRequestHandler<BecomeOrganizerCommand, OrganizerEnrollmentResult>
{
    public async Task<OrganizerEnrollmentResult> Handle(BecomeOrganizerCommand request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not int id || id <= 0)
            throw new UnauthorizedAccessException("Authentication required.");
        var changed = await identity.BecomeOrganizerAsync(id, ct);
        return new(changed, changed);
    }
}
