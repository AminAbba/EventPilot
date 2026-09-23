using MediatR;
using EventPilot.Application.Abstractions;
using EventPilot.Application.Users.Dtos;
using EventPilot.Application.Abstractions.Auth;

namespace EventPilot.Application.Users.Queries.GetMe;

public sealed class GetMeQueryHandler : IRequestHandler<GetMeQuery, MeDto>
{
    private readonly IIdentityService _identity;
    private readonly ICurrentUser _currentUser;

    public GetMeQueryHandler(IIdentityService identity, ICurrentUser currentUser)
    {
        _identity = identity;
        _currentUser = currentUser;
    }

    public async Task<MeDto> Handle(GetMeQuery request, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        return await _identity.GetMeAsync(userId, ct);

    }
}
