using Inventory.Domain.Common;
using Inventory.Domain.Enums;

namespace Inventory.Domain.Entities;

/// <summary>
/// Immutable audit log row created on every tag print and reprint event.
/// Records the standard audit set: UserId, UserNameSnapshot, ClientIp.
/// Contains no MachineName or PrinterName columns.
/// </summary>
public class TagPrintLog : BaseEntity
{
    public int TagId { get; set; }
    public Tag? Tag { get; set; }

    public TagPrintKind Kind { get; set; }
    public string? Reason { get; set; }

    public DateTime PrintedAt { get; set; }

    public int UserId { get; set; }
    public string UserNameSnapshot { get; set; } = string.Empty;
    public string? ClientIp { get; set; }
}
