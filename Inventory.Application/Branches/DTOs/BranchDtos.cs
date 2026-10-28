namespace Inventory.Application.Branches.DTOs;

public record BranchDto(
    int Id,
    string Code,
    string Name,
    string? PlantCode,
    bool IsActive,
    DateTime CreatedAt,
    int? CreatedBy,
    DateTime? ModifiedAt,
    int? ModifiedBy);

public record CreateBranchDto(
    string Code,
    string Name,
    string? PlantCode);

public record UpdateBranchDto(
    string Name,
    string? PlantCode,
    bool IsActive);
