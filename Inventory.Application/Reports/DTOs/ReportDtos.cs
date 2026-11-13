namespace Inventory.Application.Reports.DTOs;

public record InwardReportFilter(
    DateTime? FromDate = null,
    DateTime? ToDate = null,
    string? MaterialCode = null,
    string? LocationCode = null,
    int? BranchId = null,
    bool CrossBranch = false,
    int Page = 1,
    int PageSize = 20);

public record InwardReportRowDto(
    int Id,
    int BranchId,
    string BranchCode,
    string TagNo,
    string MaterialCode,
    string? PartName,
    string? BatchNo,
    string? LocationCode,
    int Qty,
    DateTime InwardAt,
    string? OperatorName,
    string? ClientIp);

public record OutwardReportFilter(
    DateTime? FromDate = null,
    DateTime? ToDate = null,
    string? MaterialCode = null,
    string? TagNo = null,
    int? BranchId = null,
    bool CrossBranch = false,
    int Page = 1,
    int PageSize = 20);

public record OutwardReportRowDto(
    int Id,
    int BranchId,
    string BranchCode,
    string TagNo,
    string MaterialCode,
    string? PartName,
    string? LocationCode,
    int Qty,
    int RemainingQty,
    DateTime IssuedAt,
    DateTime? OriginalInwardAt,
    string? OperatorName,
    string? ClientIp)
{
    public int IssuedQty => Qty;
    public DateTime DispatchedAt => IssuedAt;
    public string? UserNameSnapshot => OperatorName;
}

public record InStockReportFilter(
    string? MaterialCode = null,
    string? LocationCode = null,
    int? BranchId = null,
    bool CrossBranch = false,
    int Page = 1,
    int PageSize = 20);

public record InStockReportRowDto(
    int BranchId,
    string BranchCode,
    string MaterialCode,
    string? PartName,
    int LocationId,
    string LocationCode,
    string? LocationZone,
    int TotalTags,
    int TotalInStockQty,
    DateTime? OldestInwardAt,
    DateTime? NewestInwardAt)
{
    public int TotalStockQty => TotalInStockQty;
    public int TotalQty => TotalInStockQty;
    public int ActiveTags => TotalTags;
    public int TagCount => TotalTags;
}

public record LabelScanReportFilter(
    string? TagNo = null,
    string? EventType = null,
    string? ActionType = null,
    string? Search = null,
    DateTime? FromDate = null,
    DateTime? ToDate = null,
    int? BranchId = null,
    bool CrossBranch = false,
    int Page = 1,
    int PageSize = 20);

public record LabelScanReportRowDto(
    int Id,
    int BranchId,
    string BranchCode,
    string TagNo,
    string MaterialCode,
    int Qty,
    string EventType,
    string? PrinterIp,
    string? Details,
    string? Reason,
    string? OperatorName,
    string? ClientIp,
    DateTime Timestamp)
{
    public string ActionType => EventType;
    public string? Operator => OperatorName ?? UserNameSnapshot;
    public string? UserNameSnapshot => OperatorName;
}

public record OverallReportFilter(
    string GroupBy = "material",
    string? Search = null,
    string? MaterialCode = null,
    DateTime? FromDate = null,
    DateTime? ToDate = null,
    int? BranchId = null,
    bool CrossBranch = false,
    int Page = 1,
    int PageSize = 20);

public record OverallReportRowDto(
    string GroupKey,
    string MaterialCode,
    string? PartName,
    int TotalInwardQty,
    int TotalOutwardQty,
    int CurrentStockQty,
    int TotalMovements,
    DateTime? LastMovementAt = null)
{
    public int TotalInwarded => TotalInwardQty;
    public int TotalIssued => TotalOutwardQty;
    public int CurrentStock => CurrentStockQty;
    public int MovementCount => TotalMovements;
}

public record AcceptRejectReportFilter(
    string? Status = null,
    string? MaterialCode = null,
    int? BranchId = null,
    bool CrossBranch = false,
    int Page = 1,
    int PageSize = 20);

public record AcceptRejectReportRowDto(
    int Id,
    int BranchId,
    string BranchCode,
    string TagNo,
    string MaterialCode,
    string? PartName,
    string Status,
    int Qty,
    string? Reason,
    string? InspectorName,
    string? ClientIp,
    DateTime ProcessedAt)
{
    public string? Inspector => InspectorName;
    public string? UserNameSnapshot => InspectorName;
}

public record LocationTransferReportFilter(
    string? TagNo = null,
    string? MaterialCode = null,
    int? BranchId = null,
    bool CrossBranch = false,
    int Page = 1,
    int PageSize = 20);

public record LocationTransferReportRowDto(
    int Id,
    int BranchId,
    string BranchCode,
    string TagNo,
    string MaterialCode,
    string? PartName,
    int Qty,
    string? FromLocationCode,
    string? ToLocationCode,
    string? Reason,
    string? OperatorName,
    string? ClientIp,
    DateTime TransferredAt)
{
    public string? Operator => OperatorName ?? UserNameSnapshot;
    public string? UserNameSnapshot => OperatorName;
}

public record DashboardLiveMetricsDto(
    int TotalRegisteredItems,
    int PendingInwardItems,
    int TotalInStockItems,
    int TotalInStockUnits,
    int RejectedItemsCount,
    int ActiveAlertsCount,
    IReadOnlyList<RecentTransactionDto> RecentTransactions);

public record RecentTransactionDto(
    int Id,
    int BranchId,
    string BranchCode,
    string TagNo,
    string MovementType,
    int Qty,
    string? LocationCode,
    string? OperatorName,
    DateTime CreatedAt);
