using Inventory.Application.Auth.DTOs;
using Inventory.Application.Auth.Services;
using Inventory.Application.Common.Interfaces;
using Inventory.Domain.Common;
using Inventory.Domain.Exceptions;
using Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Infrastructure.Services;

/// <summary>
/// Handles login with lockout, branch validation, and token issuance.
/// All failures return the same "Invalid credentials" message to prevent user enumeration.
/// </summary>
public class AuthService : IAuthService
{
    private const string InvalidCredentials = "Invalid credentials";

    private readonly InventoryDbContext _context;
    private readonly IPasswordService _passwordService;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;

    public AuthService(
        InventoryDbContext context,
        IPasswordService passwordService,
        IJwtTokenService jwtTokenService,
        IClock clock,
        ICurrentUser currentUser)
    {
        _context = context;
        _passwordService = passwordService;
        _jwtTokenService = jwtTokenService;
        _clock = clock;
        _currentUser = currentUser;
    }

    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var utcNow = _clock.UtcNow;

        // Load user with Employee and UserBranches
        var user = await _context.AppUsers
            .IgnoreQueryFilters()
            .Include(u => u.Employee)
            .Include(u => u.UserBranches)
            .FirstOrDefaultAsync(u => u.Username == request.Username, ct);

        // Generic failure: user not found
        if (user == null)
            throw new UnauthorizedException(InvalidCredentials);

        // Generic failure: user inactive
        if (!user.IsActive)
            throw new UnauthorizedException(InvalidCredentials);

        // Generic failure: employee inactive
        if (!user.Employee.IsActive)
            throw new UnauthorizedException(InvalidCredentials);

        // Generic failure: locked out
        if (user.IsLockedOut(utcNow))
            throw new UnauthorizedException(InvalidCredentials);

        // Generic failure: branch not in user's allowed branches
        var branchAllowed = user.UserBranches.Any(ub => ub.BranchId == request.BranchId);
        if (!branchAllowed)
        {
            // Don't reveal why login failed — still record failure
            user.RecordFailedLogin(utcNow);
            await _context.SaveChangesAsync(ct);
            throw new UnauthorizedException(InvalidCredentials);
        }

        // Verify password
        if (!_passwordService.Verify(request.Password, user.PasswordHash))
        {
            user.RecordFailedLogin(utcNow);
            await _context.SaveChangesAsync(ct);
            throw new UnauthorizedException(InvalidCredentials);
        }

        // Success
        user.ResetFailedLogins(utcNow);
        await _context.SaveChangesAsync(ct);

        var (token, expiresAt) = _jwtTokenService.GenerateToken(user, request.BranchId);

        // Load branch details
        var branch = await _context.Branches
            .IgnoreQueryFilters()
            .Where(b => b.Id == request.BranchId)
            .Select(b => new LoginBranchDto(b.Id, b.Code, b.Name))
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Branch", request.BranchId);

        return new LoginResponse(
            token,
            expiresAt,
            new LoginUserDto(user.Id, user.Username, user.Employee.Name, user.Role.ToString()),
            branch,
            user.MustChangePassword);
    }

    public async Task<IReadOnlyList<BranchLookupDto>> GetActiveBranchesAsync(CancellationToken ct = default)
    {
        return await _context.Branches
            .IgnoreQueryFilters()
            .Where(b => b.IsActive)
            .OrderBy(b => b.Code)
            .Select(b => new BranchLookupDto(b.Id, b.Code, b.Name))
            .ToListAsync(ct);
    }

    public async Task<MeDto> GetMeAsync(CancellationToken ct = default)
    {
        var userId = _currentUser.UserId;
        var user = await _context.AppUsers
            .IgnoreQueryFilters()
            .Include(u => u.Employee)
            .FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new NotFoundException("AppUser", userId);

        return new MeDto(
            user.Id,
            user.Username,
            user.Employee.Name,
            user.Role.ToString(),
            user.EmployeeId,
            _currentUser.BranchId,
            user.MustChangePassword);
    }

    public async Task ChangePasswordAsync(ChangePasswordRequest request, CancellationToken ct = default)
    {
        var userId = _currentUser.UserId;
        var user = await _context.AppUsers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new NotFoundException("AppUser", userId);

        if (!_passwordService.Verify(request.CurrentPassword, user.PasswordHash))
            throw new DomainException("Current password is incorrect.");

        var error = PasswordPolicy.Validate(request.NewPassword);
        if (error != null)
            throw new DomainException(error);

        user.PasswordHash = _passwordService.Hash(request.NewPassword);
        user.MustChangePassword = false;

        await _context.SaveChangesAsync(ct);
    }
}
