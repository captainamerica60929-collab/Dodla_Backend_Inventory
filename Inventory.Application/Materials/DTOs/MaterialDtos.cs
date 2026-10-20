namespace Inventory.Application.Materials.DTOs;

public record MaterialDto(
    int Id,
    string Code,
    string Name,
    string Uom,
    string? Zone,
    bool IsActive,
    DateTime CreatedAt,
    int? CreatedBy,
    DateTime? ModifiedAt,
    int? ModifiedBy);

public record CreateMaterialDto(
    string Code,
    string Name,
    string Uom,
    string? Zone);

public record UpdateMaterialDto(
    string Name,
    string Uom,
    string? Zone,
    bool IsActive);

public record MaterialFilterDto(
    int Page = 1,
    int PageSize = 20,
    string? Search = null,
    bool? ActiveOnly = null,
    string? Zone = null);
