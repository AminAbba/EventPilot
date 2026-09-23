namespace EventPilot.Infrastructure.Auth;

public sealed class JwtOptions
{
    public string Issuer { get; set; } = null!;
    public string Audience { get; set; } = null!;
    public string Key { get; set; } = null!;
    public int ExpireMinutes { get; set; } = 60;
}
