using Inventory.Domain.Common;
using Inventory.Domain.Exceptions;

namespace Inventory.Domain.Entities;

public class Material : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Uom { get; set; } = string.Empty;
    public string? Zone { get; set; }
    public bool IsActive { get; set; } = true;

    public void Update(string name, string uom, string? zone, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Material name is required.");

        if (name.Trim().Length > 150)
            throw new DomainException("Material name cannot exceed 150 characters.");

        if (string.IsNullOrWhiteSpace(uom))
            throw new DomainException("Material UOM is required.");

        if (uom.Trim().Length > 10)
            throw new DomainException("Material UOM cannot exceed 10 characters.");

        if (!string.IsNullOrWhiteSpace(zone) && zone.Trim().Length > 20)
            throw new DomainException("Material zone cannot exceed 20 characters.");

        Name = name.Trim();
        Uom = uom.Trim();
        Zone = string.IsNullOrWhiteSpace(zone) ? null : zone.Trim();
        IsActive = isActive;
    }

    public void Deactivate(int currentStock = 0)
    {
        // TODO: Deactivation is blocked later when stock exists (domain method hook)
        if (currentStock > 0)
            throw new DomainException("Cannot deactivate material with existing stock.");

        IsActive = false;
    }
}
