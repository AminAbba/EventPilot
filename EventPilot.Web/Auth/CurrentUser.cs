using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using EventPilot.Application.Abstractions.Auth;
using JwtRegisteredClaimNames = System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames;

namespace EventPilot.Web.Auth;

public sealed class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _http;

    public CurrentUser(IHttpContextAccessor http)
    {
        _http = http;
    }

    public bool IsAuthenticated => _http.HttpContext?.User?.Identity?.IsAuthenticated == true;

    public int? UserId
    {
        get
        {
            var user = _http.HttpContext?.User;
            if (user is null) return null;

            var sub = user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
            if (int.TryParse(sub, out var idFromSub)) return idFromSub;

            var nameId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (int.TryParse(nameId, out var idFromNameId)) return idFromNameId;

            return null;
        }
    }

    public string? Email
    {
        get
        {
            var user = _http.HttpContext?.User;
            if (user is null) return null;

            var email = user.FindFirst(JwtRegisteredClaimNames.Email)?.Value;
            return email ?? user.FindFirst(ClaimTypes.Email)?.Value;
        }
    }
}
