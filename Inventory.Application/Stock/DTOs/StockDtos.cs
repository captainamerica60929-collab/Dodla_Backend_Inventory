namespace Inventory.Application.Stock.DTOs;

public record InwardScanRequestDto(
    string TagNo,
    string LocationCode);

public record InwardResponseDto(
    int StockRecordId,
    int TagId,
    string TagNo,
    int LocationId,
    string LocationCode,
    string MaterialCode,
    int Qty,
    int LocationUsedCapacity,
    int LocationCapacity,
    DateTime InwardAt);

public record OutwardRequestDto(
    string TagNo,
    int Qty,
    bool BypassFifo = false);

public record OutwardIssueRequestDto(
    string TagNo,
    int Qty,
    bool BypassFifo = false) : OutwardRequestDto(TagNo, Qty, BypassFifo);

public record TransferRequestDto(
    string TagNo,
    string TargetLocationCode);

public record TransferResponseDto(
    int StockRecordId,
    int TagId,
    string TagNo,
    string MaterialCode,
    int Qty,
    int FromLocationId,
    string FromLocationCode,
    int ToLocationId,
    string ToLocationCode,
    int TargetLocationUsedCapacity,
    int TargetLocationCapacity,
    DateTime TransferredAt);

public record OutwardResponseDto(
    int TagId,
    string TagNo,
    string MaterialCode,
    int IssuedQty,
    int RemainingQty,
    string TagStatus,
    int LocationId,
    string LocationCode,
    DateTime InwardAt);

public record AcceptRequestDto(
    string TagNo);

public record RejectRequestDto(
    string TagNo,
    string Reason);

public record AcceptRejectResponseDto(
    int TagId,
    string TagNo,
    string MaterialCode,
    string PreviousStatus,
    string NewStatus,
    int RemainingQty,
    string? Reason,
    DateTime ProcessedAt);

public record TagStockInfoDto(
    int TagId,
    string TagNo,
    string Status,
    int Qty,
    int RemainingQty,
    string MaterialCode,
    string? PartName,
    string? BatchNo,
    int? LocationId,
    string? LocationCode,
    string? LocationZone,
    DateTime? InwardAt);

public record StockMovementDto(
    int Id,
    int TagId,
    string TagNo,
    int? LocationId,
    string? LocationCode,
    string MaterialCode,
    string MovementType,
    int Qty,
    DateTime? InwardAt,
    string? Reason,
    int UserId,
    string? UserNameSnapshot,
    string? ClientIp,
    DateTime CreatedAt);
