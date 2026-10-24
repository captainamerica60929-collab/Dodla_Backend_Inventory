using Inventory.Domain.Common;
using Inventory.Domain.Enums;

namespace Inventory.Domain.Entities;

public class GrnApiResponse : BranchScopedEntity
{
    public int? ApiCredentialId { get; set; }
    public ApiCredential? ApiCredential { get; set; }

    public Guid FetchBatchId { get; set; }
    public DateTime FetchedAt { get; set; }
    public int? FetchedBy { get; set; }

    /// <summary>
    /// Only GrnNo is strictly required by the landing table.
    /// </summary>
    public string GrnNo { get; set; } = string.Empty;

    public int? GrnYear { get; set; }
    public int? GrnItemNo { get; set; }

    /// <summary>
    /// Preserves date and time from the API response (datetime2).
    /// </summary>
    public DateTime? GrnDate { get; set; }

    public string? InvoiceNo { get; set; }
    public DateOnly? InvoiceDate { get; set; }

    public decimal? Qty { get; set; }
    public string? Uom { get; set; }
    public string? MaterialCode { get; set; }
    public string? PartName { get; set; }
    public string? Description { get; set; }
    public string? Plant { get; set; }
    public string? StorageLocation { get; set; }
    public string? BatchNo { get; set; }
    public string? PoNo { get; set; }
    public string? PoItem { get; set; }
    public string? VendorCode { get; set; }
    public string? VendorName { get; set; }

    /// <summary>
    /// Indexed source key for correlation (e.g. GrnNo-Year-Item), not unique yet.
    /// </summary>
    public string? SourceKey { get; set; }

    /// <summary>
    /// Always stores the complete raw JSON item.
    /// </summary>
    public string RawJson { get; set; } = string.Empty;

    public GrnResponseStatus Status { get; set; } = GrnResponseStatus.New;
    public string? ErrorMessage { get; set; }
    public DateTime? ProcessedAt { get; set; }
}
