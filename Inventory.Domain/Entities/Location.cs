using Inventory.Domain.Common;
using Inventory.Domain.Exceptions;

namespace Inventory.Domain.Entities;

public class Location : BranchScopedEntity
{
    public string Code { get; set; } = string.Empty;
    public string Zone { get; set; } = string.Empty;
    public int Capacity { get; set; }
    public bool IsActive { get; set; } = true;
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public void Update(string zone, int capacity, bool isActive, int usedQty = 0)
    {
        if (string.IsNullOrWhiteSpace(zone))
            throw new DomainException("Location zone is required.");

        if (zone.Trim().Length > 20)
            throw new DomainException("Location zone cannot exceed 20 characters.");

        if (capacity <= 0)
            throw new DomainException("Location capacity must be greater than zero.");

        // Rule ready for later: Capacity may not be reduced below used qty
        if (capacity < usedQty)
            throw new DomainException($"Location capacity ({capacity}) cannot be reduced below used quantity ({usedQty}).");

        Zone = zone.Trim();
        Capacity = capacity;
        IsActive = isActive;
    }

    public void Deactivate(int currentStock = 0)
    {
        // TODO: Deactivation is blocked later when stock exists (domain method hook)
        if (currentStock > 0)
            throw new DomainException("Cannot deactivate location with existing stock.");

        IsActive = false;
    }
}
