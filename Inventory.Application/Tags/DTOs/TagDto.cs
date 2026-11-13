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
    /// <summary>SAP/ERP material code — used by the @partcode@ / @materialcode@ ZPL placeholder.</summary>
    string? MaterialCode,
    string? Description,
    int Qty,
    string? Plant,
    string BranchCode);
