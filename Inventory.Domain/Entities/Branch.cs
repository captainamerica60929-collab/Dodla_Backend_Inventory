using Inventory.Domain.Common;

namespace Inventory.Domain.Entities;

/// <summary>
/// Warehouse Branch Master (Global master entity, not branch-scoped).
/// </summary>
public class Branch : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? PlantCode { get; set; }
    public bool IsActive { get; set; } = true;

    public void Deactivate()
    {
        IsActive = false;
    }

    public void Activate()
    {
        IsActive = true;
    }
}
