using EventPilot.Application.Abstractions;
using EventPilot.Application.Abstractions.Auth;
using MediatR;

namespace EventPilot.Application.Users.Commands.UpdateUser
{
    public class UpdateUserCommandHandler : IRequestHandler<UpdateUserCommand>
    {
        private readonly IIdentityService _identity;
        private readonly ICurrentUser _currentUser;
        public UpdateUserCommandHandler(IIdentityService identity, ICurrentUser currentUser)
        {
            _identity = identity;
            _currentUser = currentUser;
        }
        public async Task Handle(UpdateUserCommand request, CancellationToken cancellationToken)
        {
            var userId = _currentUser.UserId;
            await _identity.UpdateUserInfo(userId, request.updateUserProfileDto, cancellationToken);
        }
    }
}
