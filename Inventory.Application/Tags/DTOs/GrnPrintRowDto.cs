namespace Inventory.Application.Tags.DTOs;

public record GrnPrintRowDto(
    int Id,
    string GrnNo,
    string? GrnDate,
    string? InvoiceNo,
    string? PartName,
    string? Description,
    decimal? Qty,
    string? Uom,
    string? Plant,
    string Status,
    TagSummaryDto? Tag);

public record TagSummaryDto(
    int TagId,
    string TagNo,
    string Status,
    int PrintCount);
