namespace Inventory.Application.Employees.DTOs;

public record EmployeeDto(
    int Id,
    string Code,
    string Name,
    string? Department,
    string? Designation,
    string? Phone,
    bool IsActive,
    DateTime CreatedAt,
    int? CreatedBy,
    DateTime? ModifiedAt,
    int? ModifiedBy);

public record CreateEmployeeDto(
    string Code,
    string Name,
    string? Department,
    string? Designation,
    string? Phone);

public record UpdateEmployeeDto(
    string Name,
    string? Department,
    string? Designation,
    string? Phone,
    bool IsActive);
