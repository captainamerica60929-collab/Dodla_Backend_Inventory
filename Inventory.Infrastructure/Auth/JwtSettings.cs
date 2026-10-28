namespace Inventory.Infrastructure.Auth;

public class JwtSettings
{
    public const string Section = "Jwt";

    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public int LifetimeMinutes { get; set; } = 480;

    // Key is injected from user-secrets (Jwt:Key) — NEVER in config files.
    // Validated at startup: must exist and be >= 32 bytes.
}
