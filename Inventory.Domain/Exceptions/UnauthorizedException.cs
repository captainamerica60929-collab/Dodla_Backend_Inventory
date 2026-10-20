namespace Inventory.Domain.Exceptions;

/// <summary>
/// Thrown when authentication fails (wrong credentials, locked account, etc.).
/// Maps to HTTP 401 Unauthorized. Always use a generic message to avoid user enumeration.
/// </summary>
public class UnauthorizedException : Exception
{
    public UnauthorizedException(string message) : base(message)
    {
    }
}
