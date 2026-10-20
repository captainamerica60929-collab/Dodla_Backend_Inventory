using Inventory.Application.Common.Interfaces;
using Inventory.Domain.Entities;
using Inventory.Domain.Enums;
using Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Inventory.Infrastructure.Services;

/// <summary>
/// Idempotent seeder that ensures at least one Admin user exists in ALL environments.
/// If an admin already exists, this seeder does nothing.
/// If Seed:AdminPassword is missing from user-secrets, logs a warning and skips.
/// Never logs the password value.
/// </summary>
public class AppUserSeeder
{
    private readonly InventoryDbContext _context;
    private readonly IPasswordService _passwordService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AppUserSeeder> _logger;

    public AppUserSeeder(
        InventoryDbContext context,
        IPasswordService passwordService,
        IConfiguration configuration,
        ILogger<AppUserSeeder> logger)
    {
        _context = context;
        _passwordService = passwordService;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        // Idempotent: skip if any admin already exists
        var adminExists = await _context.AppUsers
            .IgnoreQueryFilters()
            .AnyAsync(u => u.Role == UserRole.Admin, ct);

        if (adminExists)
            return;

        // Ensure the seed password is available — never log the value
        var adminPassword = _configuration["Seed:AdminPassword"];
        if (string.IsNullOrWhiteSpace(adminPassword))
        {
            _logger.LogWarning(
                "Seed:AdminPassword is not configured. Admin seeding skipped. " +
                "Set it via: dotnet user-secrets set \"Seed:AdminPassword\" \"<your-password>\"");
            return;
        }

        _logger.LogInformation("No admin user found. Seeding default admin employee and user...");

        // System scope: BranchId=0 bypasses branch guard in SaveChangesAsync
        // Employee is a global entity (not branch-scoped), so no issue there.
        var employee = new Employee
        {
            Code = "E001",
            Name = "System Admin",
            Department = "Administration",
            Designation = "System Administrator",
            IsActive = true
        };

        _context.Employees.Add(employee);
        await _context.SaveChangesAsync(ct); // get the employee Id

        var user = new AppUser
        {
            Username = "admin",
            EmployeeId = employee.Id,
            Role = UserRole.Admin,
            PasswordHash = _passwordService.Hash(adminPassword),
            MustChangePassword = true,
            IsActive = true
        };

        // Assign to branch MAIN (Id=1) — seeded by BranchConfiguration
        user.UserBranches.Add(new UserBranch { BranchId = 1 });

        _context.AppUsers.Add(user);
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation("Default admin user seeded successfully (username: admin).");
    }
}
