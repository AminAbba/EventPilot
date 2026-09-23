namespace EventPilot.Application.Users.Dtos;
public sealed class LoginResult
{
    public bool Succeeded { get; init; }
    public string? AccessToken { get; init; }
    public DateTime? ExpiresAtUtc { get; init; }
    public string TokenType { get; init; } = "Bearer";
    public string? Error { get; init; }
    public int? UserId { get; init; }
    public string? Email { get; init; }
}
