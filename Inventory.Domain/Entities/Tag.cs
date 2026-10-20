using Inventory.Domain.Common;
using Inventory.Domain.Enums;

namespace Inventory.Domain.Entities;

/// <summary>
/// Represents a physical inventory tag (sticker / barcode label) generated for a GRN row.
/// Branch-scoped entity. One tag lives in one bin once inwarded.
/// </summary>
public class Tag : BranchScopedEntity
{
    public int GrnApiResponseId { get; set; }
    public GrnApiResponse? GrnApiResponse { get; set; }

    public string TagNo { get; set; } = string.Empty;

    /// <summary>
    /// Whole-number quantity from the GRN row.
    /// </summary>
    public int Qty { get; set; }

    public TagStatus Status { get; set; } = TagStatus.Generated;

    public int PrintCount { get; set; }

    public DateTime? FirstPrintedAt { get; set; }
    public DateTime? LastPrintedAt { get; set; }

    /// <summary>
    /// RowVersion concurrency token.
    /// </summary>
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public ICollection<TagPrintLog> PrintLogs { get; set; } = new List<TagPrintLog>();
}
