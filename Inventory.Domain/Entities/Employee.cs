using Inventory.Domain.Common;

namespace Inventory.Domain.Entities;

/// <summary>
/// Employee master — global (not branch-scoped).
/// Domain rule: deactivating an employee must cascade to deactivate all linked AppUsers.
/// </summary>
public class Employee : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Department { get; set; }
    public string? Designation { get; set; }
    public string? Phone { get; set; }
    public bool IsActive { get; set; } = true;

    // Navigation
    public AppUser? AppUser { get; set; }

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;
}
