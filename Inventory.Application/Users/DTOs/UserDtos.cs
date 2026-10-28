namespace Inventory.Application.Users.DTOs;

public record UserDto(
    int Id,
    string Username,
    int EmployeeId,
    string EmployeeName,
    string EmployeeCode,
    string Role,
    bool MustChangePassword,
    bool IsActive,
    int FailedLoginCount,
    DateTime? LockoutEnd,
    DateTime? LastLoginAt,
    IReadOnlyList<int> BranchIds,
    DateTime CreatedAt,
    int? CreatedBy,
    DateTime? ModifiedAt,
    int? ModifiedBy);

public record CreateUserDto(
    int EmployeeId,
    string Username,
    string Role,
    IReadOnlyList<int> BranchIds,
    string InitialPassword);

public record UpdateUserDto(
    string Role,
    IReadOnlyList<int> BranchIds,
    bool IsActive);

public record ResetPasswordDto(string NewPassword);
