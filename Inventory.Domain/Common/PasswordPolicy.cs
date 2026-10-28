namespace Inventory.Domain.Common;

/// <summary>
/// Shared password policy constants enforced by both domain validation and application-layer services.
/// </summary>
public static class PasswordPolicy
{
    public const int MinLength = 10;
    public const bool RequireUppercase = true;
    public const bool RequireLowercase = true;
    public const bool RequireDigit = true;

    /// <summary>Returns null if the password is valid, or a descriptive error message.</summary>
    public static string? Validate(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinLength)
            return $"Password must be at least {MinLength} characters long.";

        if (RequireUppercase && !password.Any(char.IsUpper))
            return "Password must contain at least one uppercase letter.";

        if (RequireLowercase && !password.Any(char.IsLower))
            return "Password must contain at least one lowercase letter.";

        if (RequireDigit && !password.Any(char.IsDigit))
            return "Password must contain at least one digit.";

        return null;
    }
}
