using Inventory.Application.Common.Interfaces;
using Inventory.Application.Employees.DTOs;
using Inventory.Application.Users.DTOs;
using Inventory.Domain.Entities;
using Inventory.Domain.Enums;
using Inventory.Domain.Exceptions;
using Inventory.Infrastructure.Persistence;
using Inventory.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Inventory.Application.Tests;

public class UserAndEmployeeServiceTests
{
    private (InventoryDbContext context, UserService userService, EmployeeService employeeService, FakeCurrentUser currentUser) CreateSut(string dbName)
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        var currentUser = new FakeCurrentUser { UserId = 1, BranchId = 1, Role = "Admin" };
        var clock = new FakeClock();
        var context = new InventoryDbContext(options, currentUser, clock);

        var passwordService = new PasswordService();
        var userService = new UserService(context, passwordService, currentUser);
        var employeeService = new EmployeeService(context);

        return (context, userService, employeeService, currentUser);
    }

    private async Task<(Employee emp, AppUser user)> CreateAdminAsync(InventoryDbContext context, int id, string username)
    {
        var emp = new Employee { Id = id, Code = $"EMP{id}", Name = $"Admin {id}", IsActive = true };
        context.Employees.Add(emp);
        await context.SaveChangesAsync();

        var user = new AppUser
        {
            Id = id,
            Username = username,
            EmployeeId = emp.Id,
            Role = UserRole.Admin,
            PasswordHash = "hashed",
            IsActive = true
        };
        user.UserBranches.Add(new UserBranch { BranchId = 1 });
        context.AppUsers.Add(user);
        await context.SaveChangesAsync();

        return (emp, user);
    }

    [Fact]
    public async Task EmployeeDeactivation_CascadesToLinkedUsers()
    {
        var (context, _, employeeService, _) = CreateSut(Guid.NewGuid().ToString());

        // Setup: Admin 1, and Employee 2 with a StoreKeeper user
        await CreateAdminAsync(context, 1, "admin1");

        var emp2 = new Employee { Id = 2, Code = "EMP2", Name = "Worker", IsActive = true };
        context.Employees.Add(emp2);
        await context.SaveChangesAsync();

        var user2 = new AppUser
        {
            Id = 2,
            Username = "worker",
            EmployeeId = emp2.Id,
            Role = UserRole.StoreKeeper,
            PasswordHash = "hash",
            IsActive = true
        };
        context.AppUsers.Add(user2);
        await context.SaveChangesAsync();

        // Deactivate employee
        await employeeService.DeactivateAsync(emp2.Id);

        var updatedEmp = await context.Employees.FindAsync(emp2.Id);
        var updatedUser = await context.AppUsers.FindAsync(user2.Id);

        Assert.False(updatedEmp!.IsActive);
        Assert.False(updatedUser!.IsActive);
    }

    [Fact]
    public async Task EmployeeDeactivation_ThrowsWhenLinkedToLastActiveAdmin()
    {
        var (context, _, employeeService, _) = CreateSut(Guid.NewGuid().ToString());

        // Only 1 Admin in system
        var (adminEmp, _) = await CreateAdminAsync(context, 1, "admin1");

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            employeeService.DeactivateAsync(adminEmp.Id));

        Assert.Contains("last active administrator", ex.Message);
    }

    [Fact]
    public async Task UserService_DemoteLastAdmin_ThrowsDomainException()
    {
        var (context, userService, _, currentUser) = CreateSut(Guid.NewGuid().ToString());

        // Current user is Admin 999. Admin 1 is the ONLY OTHER admin, and is targeted for demotion.
        // Wait, if Admin 1 is the ONLY admin in system:
        var (_, admin1) = await CreateAdminAsync(context, 1, "admin1");
        currentUser.UserId = 999; // Current user is someone else

        var updateDto = new UpdateUserDto("StoreKeeper", new List<int> { 1 }, true);

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            userService.UpdateAsync(admin1.Id, updateDto));

        Assert.Contains("last active administrator", ex.Message);
    }

    [Fact]
    public async Task UserService_DeactivateLastAdmin_ThrowsDomainException()
    {
        var (context, userService, _, currentUser) = CreateSut(Guid.NewGuid().ToString());

        var (_, admin1) = await CreateAdminAsync(context, 1, "admin1");
        currentUser.UserId = 999;

        var updateDto = new UpdateUserDto("Admin", new List<int> { 1 }, false);

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            userService.UpdateAsync(admin1.Id, updateDto));

        Assert.Contains("last active administrator", ex.Message);
    }

    [Fact]
    public async Task UserService_AdminCannotDeactivateThemselves()
    {
        var (context, userService, _, currentUser) = CreateSut(Guid.NewGuid().ToString());

        await CreateAdminAsync(context, 1, "admin1");
        await CreateAdminAsync(context, 2, "admin2");

        currentUser.UserId = 1; // Admin 1 is logged in

        var updateDto = new UpdateUserDto("Admin", new List<int> { 1 }, false);

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            userService.UpdateAsync(1, updateDto));

        Assert.Contains("cannot deactivate their own account", ex.Message);
    }

    [Fact]
    public async Task UserService_AdminCannotDemoteThemselves()
    {
        var (context, userService, _, currentUser) = CreateSut(Guid.NewGuid().ToString());

        await CreateAdminAsync(context, 1, "admin1");
        await CreateAdminAsync(context, 2, "admin2");

        currentUser.UserId = 1; // Admin 1 is logged in

        var updateDto = new UpdateUserDto("StoreKeeper", new List<int> { 1 }, true);

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            userService.UpdateAsync(1, updateDto));

        Assert.Contains("cannot demote their own account", ex.Message);
    }

    [Fact]
    public async Task UserService_DemoteAdmin_SucceedsIfAnotherActiveAdminExists()
    {
        var (context, userService, _, currentUser) = CreateSut(Guid.NewGuid().ToString());

        await CreateAdminAsync(context, 1, "admin1");
        var (_, admin2) = await CreateAdminAsync(context, 2, "admin2");

        currentUser.UserId = 1; // Admin 1 demotes Admin 2

        var updateDto = new UpdateUserDto("StoreKeeper", new List<int> { 1 }, true);
        var result = await userService.UpdateAsync(admin2.Id, updateDto);

        Assert.Equal("StoreKeeper", result.Role);
    }
}
