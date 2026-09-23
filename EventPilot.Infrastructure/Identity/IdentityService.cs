using System.Text;
using EventPilot.Application.Abstractions;
using EventPilot.Application.Abstractions.Auth;
using EventPilot.Application.Abstractions.Email;
using EventPilot.Application.Registrations.Common.Exceptions;
using EventPilot.Application.Users.Dtos;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using EventPilot.Infrastructure.Persistence;

namespace EventPilot.Infrastructure.Identity;

public sealed class IdentityService(
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn,
    IJwtTokenGenerator tokens,
    IEmailSender emailSender,
    ILogger<IdentityService> logger,
    EventPilotDbContext db) : IIdentityService
{
    public async Task<int> CreateUserAsync(string email, string password, string firstName, string lastName,
        string? avatarUrl, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var user = new ApplicationUser
        {
            UserName = email.Trim(),
            Email = email.Trim(),
            FirstName = firstName.Trim(),
            LastName = lastName.Trim(),
            AvatarUrl = avatarUrl
        };
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        EnsureSucceeded(await users.CreateAsync(user, password));
        EnsureSucceeded(await users.AddToRoleAsync(user, AppRoles.User));
        await transaction.CommitAsync(ct);
        logger.LogInformation("Account created for user {UserId}", user.Id);
        return user.Id;
    }

    public async Task<LoginResult> LoginAsync(string email, string password, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var user = await users.FindByEmailAsync(email.Trim());
        if (user is null || !(await signIn.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true)).Succeeded)
        {
            logger.LogWarning("Login rejected");
            return new LoginResult { Succeeded = false, Error = "Invalid credentials." };
        }
        var roles = await users.GetRolesAsync(user);
        var securityStamp = await users.GetSecurityStampAsync(user);
        var token = tokens.GenerateToken(user.Id, user.Email!, roles, securityStamp);
        logger.LogInformation("Login succeeded for user {UserId}", user.Id);
        return new LoginResult
        {
            Succeeded = true,
            AccessToken = token.Token,
            ExpiresAtUtc = token.ExpiresAtUtc,
            UserId = user.Id,
            Email = user.Email
        };
    }

    public async Task LogoutAsync(int userId, CancellationToken ct)
    {
        var user = await RequiredUser(userId, ct);
        EnsureSucceeded(await users.UpdateSecurityStampAsync(user));
        logger.LogInformation("All sessions revoked for user {UserId}", user.Id);
    }

    public async Task<bool> BecomeOrganizerAsync(int userId, CancellationToken ct)
    {
        var user = await RequiredUser(userId, ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (await users.IsInRoleAsync(user, AppRoles.Organizer))
            return false;
        EnsureSucceeded(await users.AddToRoleAsync(user, AppRoles.Organizer));
        // Save the role change and session revocation together.
        EnsureSucceeded(await users.UpdateSecurityStampAsync(user));
        await transaction.CommitAsync(ct);
        logger.LogInformation("Organizer role granted and previous sessions revoked for user {UserId}", user.Id);
        return true;
    }

    public async Task SendPasswordResetLinkAsync(string email, string resetLinkBase, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var user = await users.FindByEmailAsync(email.Trim());
        if (user is null)
            return;
        var rawToken = await users.GeneratePasswordResetTokenAsync(user);
        var token = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(rawToken));
        var resetPage = new Uri(new Uri(resetLinkBase), "ResetPassword");
        var link = QueryHelpers.AddQueryString(resetPage.AbsoluteUri, new Dictionary<string, string?>
        {
            ["email"] = user.Email,
            ["token"] = token
        });
        try
        {
            await emailSender.SendAsync(user.Email!, "Reset your EventPilot password",
                $"Reset your password using this link:\n{link}\nIf you did not request this, ignore this email.", ct);
            logger.LogInformation("Password reset message delivered for user {UserId}", user.Id);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Mail errors can contain credentials or reset links.
            logger.LogError("Password reset delivery failed for user {UserId}; failure type {FailureType}",
                user.Id, ex.GetType().Name);
        }
    }

    public async Task ResetPasswordAsync(string email, string token, string newPassword, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var user = await users.FindByEmailAsync(email.Trim());
        if (user is null)
            throw new ValidationException("Invalid or expired password reset request.");

        string decoded;
        try
        {
            decoded = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token));
        }
        catch (FormatException)
        {
            throw new ValidationException("Invalid or expired password reset request.");
        }
        var result = await users.ResetPasswordAsync(user, decoded, newPassword);
        if (result.Errors.Any(x => x.Code == "InvalidToken"))
            throw new ValidationException("Invalid or expired password reset request.");
        EnsureSucceeded(result);
        // Password reset also changes the security stamp.
        logger.LogInformation("Password reset completed; previous sessions revoked for user {UserId}", user.Id);
    }

    public async Task<MeDto> GetMeAsync(int? userId, CancellationToken ct)
    {
        var user = await RequiredUser(userId, ct);
        return new MeDto
        {
            Id = user.Id,
            Email = user.Email!,
            FirstName = user.FirstName,
            LastName = user.LastName,
            DisplayName = $"{user.FirstName} {user.LastName}".Trim(),
            Roles = (await users.GetRolesAsync(user)).ToList()
        };
    }

    public async Task UpdateUserInfo(int? userId, UpdateUserProfileDto dto, CancellationToken ct)
    {
        var user = await RequiredUser(userId, ct);
        if (dto.FirstName is not null)
            user.FirstName = dto.FirstName.Trim();
        if (dto.LastName is not null)
            user.LastName = dto.LastName.Trim();
        if (dto.PhoneNumber is not null)
            user.PhoneNumber = dto.PhoneNumber.Trim();
        EnsureSucceeded(await users.UpdateAsync(user));
        logger.LogInformation("Profile updated for user {UserId}", user.Id);
    }

    private async Task<ApplicationUser> RequiredUser(int? id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (id is null || id <= 0)
            throw new UnauthorizedAccessException("Authentication required.");

        return await users.FindByIdAsync(id.Value.ToString())
            ?? throw new UnauthorizedAccessException("Account is no longer available.");
    }

    private static void EnsureSucceeded(IdentityResult result)
    {
        if (result.Succeeded)
            return;
        if (result.Errors.Any(x => x.Code is "DuplicateEmail" or "DuplicateUserName"))
            throw new BusinessRuleException("An account with this email already exists.");
        if (result.Errors.Any(x => x.Code == "ConcurrencyFailure"))
            throw new BusinessRuleException("The account changed. Reload and retry.");
        throw new ValidationException(string.Join("; ", result.Errors.Select(x => x.Description)));
    }
}
