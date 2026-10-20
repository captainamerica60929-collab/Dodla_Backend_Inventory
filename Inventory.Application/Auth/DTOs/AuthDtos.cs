namespace Inventory.Application.Auth.DTOs;

public record LoginRequest(int BranchId, string Username, string Password);

public record LoginResponse(
    string Token,
    DateTime ExpiresAt,
    LoginUserDto User,
    LoginBranchDto Branch,
    bool MustChangePassword);

public record LoginUserDto(int Id, string Username, string Name, string Role);
public record LoginBranchDto(int Id, string Code, string Name);

public record BranchLookupDto(int Id, string Code, string Name);

public record MeDto(
    int Id,
    string Username,
    string Name,
    string Role,
    int EmployeeId,
    int BranchId,
    bool MustChangePassword);

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
