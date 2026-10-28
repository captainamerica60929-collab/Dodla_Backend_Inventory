namespace Inventory.Domain.Entities;

/// <summary>
/// Join table: which branches an AppUser is allowed to access.
/// A user must have at least one branch assigned.
/// </summary>
public class UserBranch
{
    public int UserId { get; set; }
    public int BranchId { get; set; }

    // Navigations
    public AppUser AppUser { get; set; } = null!;
    public Branch Branch { get; set; } = null!;
}
