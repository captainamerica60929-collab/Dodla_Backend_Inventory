using Inventory.Domain.Enums;

namespace Inventory.Application.GrnResponses.DTOs;

public record GrnResponseFilterDto(
    string? Status = null,
    Guid? FetchBatchId = null,
    string? GrnNo = null,
    DateTime? FromDate = null,
    DateTime? ToDate = null,
    int Page = 1,
    int PageSize = 50);

public record GrnResponseListItemDto(
    int Id,
    int BranchId,
    int? ApiCredentialId,
    Guid FetchBatchId,
    DateTime FetchedAt,
    int? FetchedBy,
    string GrnNo,
    int? GrnYear,
    int? GrnItemNo,
    DateTime? GrnDate,
    string? InvoiceNo,
    DateOnly? InvoiceDate,
    decimal? Qty,
    string? Uom,
    string? MaterialCode,
    string? PartName,
    string? Description,
    string? Plant,
    string? StorageLocation,
    string? BatchNo,
    string? PoNo,
    string? PoItem,
    string? VendorCode,
    string? VendorName,
    string? SourceKey,
    GrnResponseStatus Status,
    string? ErrorMessage,
    DateTime? ProcessedAt);

public record GrnResponseDetailDto(
    int Id,
    int BranchId,
    int? ApiCredentialId,
    Guid FetchBatchId,
    DateTime FetchedAt,
    int? FetchedBy,
    string GrnNo,
    int? GrnYear,
    int? GrnItemNo,
    DateTime? GrnDate,
    string? InvoiceNo,
    DateOnly? InvoiceDate,
    decimal? Qty,
    string? Uom,
    string? MaterialCode,
    string? PartName,
    string? Description,
    string? Plant,
    string? StorageLocation,
    string? BatchNo,
    string? PoNo,
    string? PoItem,
    string? VendorCode,
    string? VendorName,
    string? SourceKey,
    string RawJson,
    GrnResponseStatus Status,
    string? ErrorMessage,
    DateTime? ProcessedAt);

public record PagedResultDto<T>(
    IReadOnlyList<T> Items,
    int Total);

public record PreviewSampleRequestDto(
    string Json,
    string? ResponseRootPath,
    List<Inventory.Application.ApiCredentials.DTOs.UpsertApiFieldMappingDto> Mappings);

public record SamplePreviewResultDto(
    int TotalRows,
    int ReturnedPreviewCount,
    int ErrorRowsCount,
    List<Inventory.Application.ApiCredentials.DTOs.GrnApiResponsePreviewDto> Rows);

public record FetchGrnRequestDto(
    string? FromDate,
    string? ToDate,
    string? Plant);

public record FetchResultDto(
    Guid FetchBatchId,
    int Total,
    int Saved,
    int ErrorRows);
