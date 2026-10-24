using Inventory.Domain.Common;
using Inventory.Domain.Enums;

namespace Inventory.Domain.Entities;

/// <summary>
/// Immutable audit log row representing every physical inventory movement or status transition.
/// Append-only table capturing user, branch, timestamp, and client IP snapshot.
/// </summary>
public class StockMovement : BranchScopedEntity
{
    public int TagId { get; set; }
    public Tag Tag { get; set; } = null!;

    public int? LocationId { get; set; }
    public Location? Location { get; set; }

    public string MaterialCode { get; set; } = string.Empty;

    public StockMovementType MovementType { get; set; }

    public int Qty { get; set; }

    public DateTime? InwardAt { get; set; }

    /// <summary>
    /// Mandatory reason for rejection, transfer, or void movements.
    /// </summary>
    public string? Reason { get; set; }

    public int UserId { get; set; }
    public string? UserNameSnapshot { get; set; }
    public string? ClientIp { get; set; }
}
