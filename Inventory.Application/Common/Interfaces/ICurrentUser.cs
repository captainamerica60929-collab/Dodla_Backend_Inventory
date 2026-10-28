namespace Inventory.Application.Common.Interfaces;

public interface ICurrentUser
{
    int UserId { get; }
    int BranchId { get; }

    /// <summary>
    /// The user's role as a string (e.g. "Admin", "StoreKeeper").
    /// Use Enum.TryParse&lt;UserRole&gt; when enum comparison is needed.
    /// Kept as string to avoid coupling Application/Domain to ASP.NET conventions.
    /// </summary>
    string Role { get; }

    /// <summary>
    /// Indicates whether this is a system/seeder execution context.
    /// When true, branch scoping guards in SaveChangesAsync are bypassed.
    /// </summary>
    bool IsSystem { get; }
}
