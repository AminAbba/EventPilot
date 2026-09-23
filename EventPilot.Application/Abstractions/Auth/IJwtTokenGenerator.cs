using System.Security.Claims;

namespace EventPilot.Application.Abstractions.Auth;
public interface IJwtTokenGenerator
{
    AccessTokenResult GenerateToken(int userId, string email, IEnumerable<string> roles, string securityStamp);
}

public sealed record AccessTokenResult(string Token, DateTime ExpiresAtUtc);
