using Inventory.Application.Common.Interfaces;
using Inventory.Application.Users.DTOs;
using Inventory.Application.Users.Services;
using Inventory.Domain.Common;
using Inventory.Domain.Entities;
using Inventory.Domain.Enums;
using Inventory.Domain.Exceptions;
using Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Infrastructure.Services;

public class UserService : IUserService
{
    private readonly InventoryDbContext _context;
    private readonly IPasswordService _passwordService;
    private readonly ICurrentUser _currentUser;

    public UserService(
        InventoryDbContext context,
        IPasswordService passwordService,
        ICurrentUser currentUser)
    {
        _context = context;
        _passwordService = passwordService;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<UserDto>> GetAllAsync(CancellationToken ct = default)
    {
        return await _context.AppUsers
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(u => u.Employee)
            .Include(u => u.UserBranches)
            .OrderBy(u => u.Username)
            .Select(u => ToDto(u))
            .ToListAsync(ct);
    }

    public async Task<UserDto> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var user = await LoadUserAsync(id, ct);
        return ToDto(user);
    }

    public async Task<UserDto> CreateAsync(CreateUserDto dto, CancellationToken ct = default)
    {
        if (!Enum.TryParse<UserRole>(dto.Role, ignoreCase: true, out var role))
            throw new DomainException($"Invalid role '{dto.Role}'.");

        if (!dto.BranchIds.Any())
            throw new DomainException("A user must have at least one branch assigned.");

        var passwordError = PasswordPolicy.Validate(dto.InitialPassword);
        if (passwordError != null)
            throw new DomainException(passwordError);

        var employee = await _context.Employees
            .FirstOrDefaultAsync(e => e.Id == dto.EmployeeId, ct)
            ?? throw new NotFoundException(nameof(Employee), dto.EmployeeId);

        if (!employee.IsActive)
            throw new DomainException("Cannot create a user for an inactive employee.");

        // Check for duplicate username (case-insensitive via DB collation)
        var usernameExists = await _context.AppUsers
            .IgnoreQueryFilters()
            .AnyAsync(u => u.Username == dto.Username.Trim(), ct);
        if (usernameExists)
            throw new DomainException($"Username '{dto.Username}' is already in use.");

        // Ensure employee doesn't already have a user
        var employeeHasUser = await _context.AppUsers
            .IgnoreQueryFilters()
            .AnyAsync(u => u.EmployeeId == dto.EmployeeId, ct);
        if (employeeHasUser)
            throw new DomainException("This employee already has a user account.");

        // Validate branches exist
        var validBranchIds = await _context.Branches
            .IgnoreQueryFilters()
            .Where(b => dto.BranchIds.Contains(b.Id))
            .Select(b => b.Id)
            .ToListAsync(ct);

        var invalidBranch = dto.BranchIds.FirstOrDefault(id => !validBranchIds.Contains(id));
        if (invalidBranch != 0)
            throw new NotFoundException("Branch", invalidBranch);

        var user = new AppUser
        {
            Username = dto.Username.Trim(),
            EmployeeId = dto.EmployeeId,
            Role = role,
            PasswordHash = _passwordService.Hash(dto.InitialPassword),
            MustChangePassword = true,
            IsActive = true
        };

        foreach (var branchId in dto.BranchIds.Distinct())
            user.UserBranches.Add(new UserBranch { BranchId = branchId });

        _context.AppUsers.Add(user);
        await _context.SaveChangesAsync(ct);

        // Reload with navigation for the response
        return await GetByIdAsync(user.Id, ct);
    }

    public async Task<UserDto> UpdateAsync(int id, UpdateUserDto dto, CancellationToken ct = default)
    {
        if (!Enum.TryParse<UserRole>(dto.Role, ignoreCase: true, out var newRole))
            throw new DomainException($"Invalid role '{dto.Role}'.");

        if (!dto.BranchIds.Any())
            throw new DomainException("A user must have at least one branch assigned.");

        var user = await _context.AppUsers
            .IgnoreQueryFilters()
            .Include(u => u.Employee)
            .Include(u => u.UserBranches)
            .FirstOrDefaultAsync(u => u.Id == id, ct)
            ?? throw new NotFoundException(nameof(AppUser), id);

        // Admin self-guard: an Admin cannot deactivate or demote themselves
        if (user.Id == _currentUser.UserId)
        {
            if (!dto.IsActive)
                throw new DomainException("An administrator cannot deactivate their own account.");
            if (newRole != UserRole.Admin)
                throw new DomainException("An administrator cannot demote their own account.");
        }

        // Last-admin guard: if demoting or deactivating an admin, ensure at least one other active admin remains
        if (user.Role == UserRole.Admin && (newRole != UserRole.Admin || !dto.IsActive))
        {
            var activeAdminCount = await _context.AppUsers
                .IgnoreQueryFilters()
                .CountAsync(u => u.Role == UserRole.Admin && u.IsActive && u.Id != id, ct);
            if (activeAdminCount == 0)
                throw new DomainException("Cannot demote or deactivate the last active administrator.");
        }

        user.Role = newRole;

        // Replace branches
        _context.UserBranches.RemoveRange(user.UserBranches);
        foreach (var branchId in dto.BranchIds.Distinct())
            user.UserBranches.Add(new UserBranch { UserId = user.Id, BranchId = branchId });

        if (!dto.IsActive)
            user.Deactivate();
        else if (!user.IsActive)
        {
            // AppUser.Activate() requires Employee to be loaded
            if (!user.Employee.IsActive)
                throw new DomainException("Cannot activate a user whose employee record is inactive.");
            user.IsActive = true;
        }

        await _context.SaveChangesAsync(ct);
        return await GetByIdAsync(id, ct);
    }

    public async Task ResetPasswordAsync(int id, ResetPasswordDto dto, CancellationToken ct = default)
    {
        var error = PasswordPolicy.Validate(dto.NewPassword);
        if (error != null)
            throw new DomainException(error);

        var user = await _context.AppUsers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == id, ct)
            ?? throw new NotFoundException(nameof(AppUser), id);

        user.PasswordHash = _passwordService.Hash(dto.NewPassword);
        user.MustChangePassword = true;

        await _context.SaveChangesAsync(ct);
    }

    public async Task UnlockAsync(int id, CancellationToken ct = default)
    {
        var user = await _context.AppUsers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == id, ct)
            ?? throw new NotFoundException(nameof(AppUser), id);

        user.Unlock();
        await _context.SaveChangesAsync(ct);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<AppUser> LoadUserAsync(int id, CancellationToken ct) =>
        await _context.AppUsers
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(u => u.Employee)
            .Include(u => u.UserBranches)
            .FirstOrDefaultAsync(u => u.Id == id, ct)
        ?? throw new NotFoundException(nameof(AppUser), id);

    private static UserDto ToDto(AppUser u) => new(
        u.Id,
        u.Username,
        u.EmployeeId,
        u.Employee.Name,
        u.Employee.Code,
        u.Role.ToString(),
        u.MustChangePassword,
        u.IsActive,
        u.FailedLoginCount,
        u.LockoutEnd,
        u.LastLoginAt,
        u.UserBranches.Select(ub => ub.BranchId).ToList(),
        u.CreatedAt,
        u.CreatedBy,
        u.ModifiedAt,
        u.ModifiedBy);
}
