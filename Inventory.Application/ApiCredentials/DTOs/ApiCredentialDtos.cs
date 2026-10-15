using Inventory.Domain.Enums;

namespace Inventory.Application.ApiCredentials.DTOs;

public record ApiCredentialDto(
    int Id,
    int BranchId,
    string Name,
    string BaseUrl,
    string HttpMethod,
    AuthType AuthType,
    string? UserName,
    string? HeaderName,
    bool HasSecret,
    string? ExtraHeadersJson,
    int TimeoutSeconds,
    string? ContentType,
    string? RequestBodyTemplate,
    string? ResponseRootPath,
    bool IsActive,
    DateTime CreatedAt,
    int? CreatedBy,
    DateTime? ModifiedAt,
    int? ModifiedBy);

public record CreateApiCredentialDto(
    string Name,
    string BaseUrl,
    string HttpMethod = "GET",
    AuthType AuthType = AuthType.None,
    string? UserName = null,
    string? HeaderName = null,
    string? Secret = null,
    string? ExtraHeadersJson = null,
    int TimeoutSeconds = 30,
    string? ContentType = "application/json",
    string? RequestBodyTemplate = null,
    string? ResponseRootPath = null);

public record UpdateApiCredentialDto(
    string Name,
    string BaseUrl,
    string HttpMethod,
    AuthType AuthType,
    string? UserName,
    string? HeaderName,
    string? Secret,
    string? ExtraHeadersJson,
    int TimeoutSeconds,
    string? ContentType,
    string? RequestBodyTemplate,
    string? ResponseRootPath,
    bool IsActive);

public record ApiFieldMappingDto(
    int Id,
    int ApiCredentialId,
    string SourceField,
    TargetColumn TargetColumn,
    string? Format,
    string? DefaultValue,
    bool IsRequired);

public record UpsertApiFieldMappingDto(
    string SourceField,
    TargetColumn TargetColumn,
    string? Format,
    string? DefaultValue,
    bool IsRequired);

public record ApiTestResultDto(
    bool Success,
    int StatusCode,
    string ReasonPhrase,
    long ElapsedMs,
    string? BodyPreview);

public record GrnApiResponsePreviewDto(
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
    string? ErrorMessage);

public record MappingPreviewResultDto(
    bool Success,
    int TotalItemsFound,
    int ReturnedPreviewCount,
    List<GrnApiResponsePreviewDto> Rows,
    string? Error);
