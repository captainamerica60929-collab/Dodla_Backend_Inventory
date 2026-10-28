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
    string? ClientIp,
    string? LocationZone = null);

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
    DateTime IssuedAt,
    DateTime? OriginalInwardAt,
    string? OperatorName,
    string? ClientIp,
    int RemainingQty = 0);

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
    DateTime? NewestInwardAt);

public record LabelScanReportFilter(
    string? TagNo = null,
    string? EventType = null,
    int? BranchId = null,
    bool CrossBranch = false,
    int Page = 1,
    int PageSize = 20);

public record LabelScanReportRowDto(
    int Id,
    int BranchId,
    string BranchCode,
    string TagNo,
    string EventType,
    string? Details,
    string? Reason,
    string? OperatorName,
    string? ClientIp,
    DateTime Timestamp,
    string? MaterialCode = null,
    int Qty = 0);

public record OverallReportFilter(
    string GroupBy = "date",
    int? BranchId = null,
    bool CrossBranch = false,
    int Page = 1,
    int PageSize = 20);

public record OverallReportRowDto(
    string GroupKey,
    int TotalInwardQty,
    int TotalOutwardQty,
    int CurrentStockQty,
    int TotalMovements);

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
    DateTime ProcessedAt);

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
    DateTime TransferredAt);

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
