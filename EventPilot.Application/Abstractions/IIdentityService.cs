
using EventPilot.Application.Users.Dtos;

namespace EventPilot.Application.Abstractions;

public interface IIdentityService
{
    Task<int> CreateUserAsync(string email, string password, string firstName, string lastName,string? AvatarUrl, CancellationToken ct);
    Task LogoutAsync(int userId, CancellationToken ct);
    Task<bool> BecomeOrganizerAsync(int userId, CancellationToken ct);
    Task<LoginResult> LoginAsync(string username, string password, CancellationToken ct);
    Task SendPasswordResetLinkAsync(string email, string resetLinkBase, CancellationToken ct);
    Task ResetPasswordAsync(string email, string token, string newPassword, CancellationToken ct);
    Task<MeDto> GetMeAsync(int? userId, CancellationToken ct);
    Task UpdateUserInfo(int? userId, UpdateUserProfileDto updateUserProfileDto, CancellationToken ct);

}
