using System.Security.Claims;
using EventPilot.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;

namespace EventPilot.Web.Auth;

public static class JwtAccountValidation
{
    public static async Task Validate(TokenValidatedContext context)
    {
        var id = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var stamp = context.Principal?.FindFirstValue("security_stamp");
        if (!int.TryParse(id, out var userId) || userId <= 0 || string.IsNullOrEmpty(stamp))
        {
            context.Fail("Invalid session.");
            return;
        }
        var users = context.HttpContext.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByIdAsync(id!);
        if (user is null || !string.Equals(user.SecurityStamp, stamp, StringComparison.Ordinal)
            || await users.IsLockedOutAsync(user))
        {
            context.Fail("Session expired or revoked.");
            return;
        }
        // Reject tokens whose roles no longer match the account.
        var tokenRoles = context.Principal!.FindAll(ClaimTypes.Role).Select(x => x.Value).ToHashSet(StringComparer.Ordinal);
        if (!tokenRoles.SetEquals(await users.GetRolesAsync(user)))
            context.Fail("Account permissions changed. Log in again.");
    }
}
