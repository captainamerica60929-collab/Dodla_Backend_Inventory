using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Inventory.Application.Auth.DTOs;
using Inventory.Application.Common.Interfaces;
using Inventory.Domain.Common;
using Inventory.Domain.Entities;
using Inventory.Domain.Enums;
using Inventory.Domain.Exceptions;
using Inventory.Infrastructure.Auth;
using Inventory.Infrastructure.Persistence;
using Inventory.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Inventory.Application.Tests;

public class AuthServiceTests
{
    private const string JwtSecret = "super-secret-key-that-is-at-least-32-bytes-long!";

    private (InventoryDbContext context, AuthService authService, FakeClock clock) CreateSut(string dbName)
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        var clock = new FakeClock();
        var context = new InventoryDbContext(options, SystemCurrentUser.Instance, clock);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = JwtSecret,
                ["Jwt:Issuer"] = "WareTrackTest",
                ["Jwt:Audience"] = "WareTrackTestAudience"
            })
            .Build();

        var jwtSettings = new JwtSettings { Issuer = "WareTrackTest", Audience = "WareTrackTestAudience", LifetimeMinutes = 480 };
        var jwtTokenService = new JwtTokenService(jwtSettings, config);
        var passwordService = new PasswordService();
        var authService = new AuthService(context, passwordService, jwtTokenService, clock, SystemCurrentUser.Instance);

        return (context, authService, clock);
    }

    private async Task<(Employee employee, AppUser user)> SeedUserAsync(
        InventoryDbContext context,
        string username = "testuser",
        string password = "Password123!",
        UserRole role = UserRole.StoreKeeper,
        bool isUserActive = true,
        bool isEmployeeActive = true,
        int branchId = 1)
    {
        var pwdService = new PasswordService();

        var branch = await context.Branches.FindAsync(branchId);
        if (branch == null)
        {
            branch = new Branch { Id = branchId, Code = $"B{branchId}", Name = $"Branch {branchId}", IsActive = true };
            context.Branches.Add(branch);
        }

        var employee = new Employee
        {
            Code = $"EMP_{Guid.NewGuid():N}"[..10],
            Name = "Test Employee",
            IsActive = isEmployeeActive
        };
        context.Employees.Add(employee);
        await context.SaveChangesAsync();

        var user = new AppUser
        {
            Username = username,
            EmployeeId = employee.Id,
            Role = role,
            PasswordHash = pwdService.Hash(password),
            IsActive = isUserActive,
            MustChangePassword = false
        };
        user.UserBranches.Add(new UserBranch { BranchId = branchId });

        context.AppUsers.Add(user);
        await context.SaveChangesAsync();

        return (employee, user);
    }

    [Fact]
    public async Task LoginAsync_ValidCredentials_ReturnsTokenAndResetsFailureCount()
    {
        var (context, authService, _) = CreateSut(Guid.NewGuid().ToString());
        var (_, user) = await SeedUserAsync(context);

        user.FailedLoginCount = 2;
        await context.SaveChangesAsync();

        var response = await authService.LoginAsync(new LoginRequest(1, user.Username, "Password123!"));

        Assert.NotNull(response.Token);
        Assert.Equal(user.Username, response.User.Username);
        Assert.Equal(1, response.Branch.Id);

        var updatedUser = await context.AppUsers.FindAsync(user.Id);
        Assert.Equal(0, updatedUser!.FailedLoginCount);
        Assert.Null(updatedUser.LockoutEnd);
        Assert.NotNull(updatedUser.LastLoginAt);
    }

    [Fact]
    public async Task LoginAsync_UnknownUser_ThrowsUnauthorizedExceptionWithGenericMessage()
    {
        var (context, authService, _) = CreateSut(Guid.NewGuid().ToString());

        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            authService.LoginAsync(new LoginRequest(1, "nonexistent", "Password123!")));

        Assert.Equal("Invalid credentials", ex.Message);
    }

    [Fact]
    public async Task LoginAsync_WrongPassword_ThrowsUnauthorizedAndIncrementsFailureCount()
    {
        var (context, authService, _) = CreateSut(Guid.NewGuid().ToString());
        var (_, user) = await SeedUserAsync(context);

        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            authService.LoginAsync(new LoginRequest(1, user.Username, "WrongPassword!")));

        Assert.Equal("Invalid credentials", ex.Message);

        var updatedUser = await context.AppUsers.FindAsync(user.Id);
        Assert.Equal(1, updatedUser!.FailedLoginCount);
    }

    [Fact]
    public async Task LoginAsync_WrongBranch_ThrowsUnauthorizedAndRecordsFailure()
    {
        var (context, authService, _) = CreateSut(Guid.NewGuid().ToString());
        var (_, user) = await SeedUserAsync(context, branchId: 1);

        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            authService.LoginAsync(new LoginRequest(999, user.Username, "Password123!")));

        Assert.Equal("Invalid credentials", ex.Message);

        var updatedUser = await context.AppUsers.FindAsync(user.Id);
        Assert.Equal(1, updatedUser!.FailedLoginCount);
    }

    [Fact]
    public async Task LoginAsync_InactiveUser_ThrowsUnauthorizedWithoutRevealingState()
    {
        var (context, authService, _) = CreateSut(Guid.NewGuid().ToString());
        var (_, user) = await SeedUserAsync(context, isUserActive: false);

        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            authService.LoginAsync(new LoginRequest(1, user.Username, "Password123!")));

        Assert.Equal("Invalid credentials", ex.Message);
    }

    [Fact]
    public async Task LoginAsync_InactiveEmployee_ThrowsUnauthorizedWithoutRevealingState()
    {
        var (context, authService, _) = CreateSut(Guid.NewGuid().ToString());
        var (_, user) = await SeedUserAsync(context, isEmployeeActive: false);

        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            authService.LoginAsync(new LoginRequest(1, user.Username, "Password123!")));

        Assert.Equal("Invalid credentials", ex.Message);
    }

    [Fact]
    public async Task LoginAsync_FiveFailedAttempts_LocksAccountFor15Minutes()
    {
        var (context, authService, clock) = CreateSut(Guid.NewGuid().ToString());
        var (_, user) = await SeedUserAsync(context);

        for (int i = 1; i <= 5; i++)
        {
            await Assert.ThrowsAsync<UnauthorizedException>(() =>
                authService.LoginAsync(new LoginRequest(1, user.Username, "WrongPassword!")));
        }

        var lockedUser = await context.AppUsers.FindAsync(user.Id);
        Assert.Equal(5, lockedUser!.FailedLoginCount);
        Assert.True(lockedUser.IsLockedOut(clock.UtcNow));
        Assert.Equal(clock.UtcNow.AddMinutes(15), lockedUser.LockoutEnd);

        // Even with correct password, login fails while locked
        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            authService.LoginAsync(new LoginRequest(1, user.Username, "Password123!")));
        Assert.Equal("Invalid credentials", ex.Message);

        // Fast forward 16 minutes: lockout expired, successful login resets counter
        clock.UtcNow = clock.UtcNow.AddMinutes(16);
        var successResponse = await authService.LoginAsync(new LoginRequest(1, user.Username, "Password123!"));
        Assert.NotNull(successResponse.Token);

        var resetUser = await context.AppUsers.FindAsync(user.Id);
        Assert.Equal(0, resetUser!.FailedLoginCount);
        Assert.Null(resetUser.LockoutEnd);
    }

    [Fact]
    public async Task LoginAsync_TokenClaims_ContainAllRequiredClaims()
    {
        var (context, authService, _) = CreateSut(Guid.NewGuid().ToString());
        var (employee, user) = await SeedUserAsync(context, role: UserRole.StoreKeeper, branchId: 1);

        var response = await authService.LoginAsync(new LoginRequest(1, user.Username, "Password123!"));

        var handler = new JwtSecurityTokenHandler();
        var token = handler.ReadJwtToken(response.Token);

        Assert.Equal(user.Id.ToString(), token.Claims.First(c => c.Type == JwtRegisteredClaimNames.Sub).Value);
        Assert.Equal("1", token.Claims.First(c => c.Type == "branch_id").Value);
        Assert.Equal("StoreKeeper", token.Claims.First(c => c.Type == "role").Value);
        Assert.Equal(employee.Name, token.Claims.First(c => c.Type == "name").Value);
        Assert.Equal(employee.Id.ToString(), token.Claims.First(c => c.Type == "employee_id").Value);
    }

    [Fact]
    public async Task ChangePasswordAsync_WrongCurrentPassword_ThrowsDomainException422()
    {
        var (context, _, clock) = CreateSut(Guid.NewGuid().ToString());
        var (_, user) = await SeedUserAsync(context);

        var fakeUser = new FakeCurrentUser { UserId = user.Id, BranchId = 1, Role = "StoreKeeper" };
        var pwdService = new PasswordService();
        var jwtSettings = new JwtSettings { Issuer = "WareTrackTest", Audience = "WareTrackTestAudience", LifetimeMinutes = 480 };
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Key"] = JwtSecret }).Build();
        var authServiceWithUser = new AuthService(context, pwdService, new JwtTokenService(jwtSettings, config), clock, fakeUser);

        // Wrong password throws DomainException (mapped to 422), NOT UnauthorizedException (401)
        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            authServiceWithUser.ChangePasswordAsync(new ChangePasswordRequest("WrongCurrentPwd!", "NewPassword123!")));

        Assert.Equal("Current password is incorrect.", ex.Message);
    }

    [Fact]
    public async Task GetActiveBranchesAsync_ReturnsOnlyIdCodeNameForActiveBranches()
    {
        var (context, authService, _) = CreateSut(Guid.NewGuid().ToString());
        context.Branches.AddRange(
            new Branch { Id = 1, Code = "BR1", Name = "Branch 1", IsActive = true },
            new Branch { Id = 2, Code = "BR2", Name = "Branch 2", IsActive = false },
            new Branch { Id = 3, Code = "BR3", Name = "Branch 3", IsActive = true }
        );
        await context.SaveChangesAsync();

        var branches = await authService.GetActiveBranchesAsync();

        Assert.Equal(2, branches.Count);
        Assert.Collection(branches,
            b => { Assert.Equal("BR1", b.Code); Assert.Equal("Branch 1", b.Name); },
            b => { Assert.Equal("BR3", b.Code); Assert.Equal("Branch 3", b.Name); });
    }
}
