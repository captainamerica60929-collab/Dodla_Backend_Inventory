using Inventory.Domain.Common;

namespace Inventory.Domain.Entities;

/// <summary>
/// Represents currently stored inventory at a specific bin/location for a tag.
/// Branch-scoped entity with optimistic concurrency rowversion.
/// </summary>
public class StockRecord : BranchScopedEntity
{
    public int TagId { get; set; }
    public Tag Tag { get; set; } = null!;

    public int LocationId { get; set; }
    public Location Location { get; set; } = null!;

    public string MaterialCode { get; set; } = string.Empty;
    public string? BatchNo { get; set; }

    /// <summary>
    /// Current quantity residing in this location for this tag.
    /// </summary>
    public int Qty { get; set; }

    /// <summary>
    /// Original inward timestamp — the basis for FIFO ordering.
    /// Preserved on transfers, partial issues, and undo operations.
    /// </summary>
    public DateTime InwardAt { get; set; }

    /// <summary>
    /// RowVersion concurrency token for optimistic locking.
    /// </summary>
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
