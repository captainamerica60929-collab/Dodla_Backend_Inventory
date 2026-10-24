using Inventory.Application.Stock.DTOs;

namespace Inventory.Application.Stock.Services;

public interface IStockService
{
    /// <summary>
    /// Processes inward scanning of an allocated tag into a specified bin/location.
    /// Updates stock records with concurrency check, changes tag status to InStock,
    /// and records an IN stock movement.
    /// </summary>
    Task<InwardResponseDto> InwardAsync(InwardScanRequestDto request, string? clientIp, CancellationToken ct = default);

    /// <summary>
    /// Issues stock using strict FIFO ordering by material and original InwardAt timestamp.
    /// Supports partial issuance while preserving remaining balance on the same tag.
    /// Records an OUT stock movement.
    /// </summary>
    Task<OutwardResponseDto> OutwardAsync(OutwardRequestDto request, string? clientIp, CancellationToken ct = default);

    /// <summary>
    /// Transfers an InStock tag to another active bin within the same branch,
    /// validating destination bin capacity, updating stock record location,
    /// and recording a TRANSFER stock movement.
    /// </summary>
    Task<TransferResponseDto> TransferAsync(TransferRequestDto request, string? clientIp, CancellationToken ct = default);

    /// <summary>
    /// Confirms acceptance of an issued tag by the receiving operator.
    /// Transitions tag status from Issued to Accepted (or back to InStock if partial balance remains).
    /// Records an ACCEPT stock movement.
    /// </summary>
    Task<AcceptRejectResponseDto> AcceptAsync(AcceptRequestDto request, string? clientIp, CancellationToken ct = default);

    /// <summary>
    /// Records rejection of an issued tag with a mandatory reason.
    /// Transitions tag status from Issued to Rejected.
    /// Records a REJECT stock movement.
    /// </summary>
    Task<AcceptRejectResponseDto> RejectAsync(RejectRequestDto request, string? clientIp, CancellationToken ct = default);

    /// <summary>
    /// Returns stock and tag details for barcode scanner feedback.
    /// </summary>
    Task<TagStockInfoDto> GetTagStockInfoAsync(string tagNo, CancellationToken ct = default);

    /// <summary>
    /// Returns movement audit trail filtered optionally by tag number or location.
    /// </summary>
    Task<IReadOnlyList<StockMovementDto>> GetMovementsAsync(string? tagNo, int? locationId, CancellationToken ct = default);
}
