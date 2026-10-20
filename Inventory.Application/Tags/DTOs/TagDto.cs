namespace Inventory.Application.Tags.DTOs;

public record TagDto(
    int TagId,
    string TagNo,
    string Status,
    int PrintCount,
    string GrnNo,
    string? GrnDate,
    string? InvoiceNo,
    string? PartName,
    string? Description,
    int Qty,
    string? Plant,
    string BranchCode);
