namespace EventPilot.Application.Abstractions.Auth;

public interface ICurrentUser
{
    int? UserId { get; }
    string? Email { get; }
    bool IsAuthenticated { get; }
}