namespace Inventory.Application.Locations.DTOs;

public record LocationDto(
    int Id,
    int BranchId,
    string Code,
    string Zone,
    int Capacity,
    int UsedQty,
    int FreeQty,
    bool IsActive,
    byte[] RowVersion,
    DateTime CreatedAt,
    int? CreatedBy,
    DateTime? ModifiedAt,
    int? ModifiedBy);

public record CreateLocationDto(
    string Code,
    string Zone,
    int Capacity);

public record UpdateLocationDto(
    string Zone,
    int Capacity,
    bool IsActive,
    byte[] RowVersion);

public record LocationFilterDto(
    int Page = 1,
    int PageSize = 20,
    string? Search = null,
    bool? ActiveOnly = null,
    string? Zone = null);
