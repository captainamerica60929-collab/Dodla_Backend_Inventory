using Inventory.Application.Common.Interfaces;
using Inventory.Infrastructure.Persistence;
using Inventory.Infrastructure.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Inventory.Application.Tests;

public class DevSeedIdempotencyTests
{
    private readonly ISecretProtector _protector = new SecretProtector(new EphemeralDataProtectionProvider());

    private DbContextOptions<InventoryDbContext> CreateOptions(string dbName) =>
        new DbContextOptionsBuilder<InventoryDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

    [Fact]
    public async Task SeedAsync_InDevelopment_ShouldBeIdempotent()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var options = CreateOptions(dbName);
        var clock = new FakeClock();
        var user = new FakeCurrentUser { UserId = 1, BranchId = 1 };
        var devEnv = new DummyHostEnvironment { EnvironmentName = Environments.Development };

        using var context = new InventoryDbContext(options, user, clock);
        var seeder = new DevDataSeeder(context, _protector, devEnv, NullLogger<DevDataSeeder>.Instance);

        // Act 1 - First run
        await seeder.SeedAsync();

        // Assert 1 - Seeded
        var cred1 = await context.ApiCredentials.Include(c => c.FieldMappings).FirstOrDefaultAsync();
        Assert.NotNull(cred1);
        Assert.Equal("Mock GRN API (dev)", cred1.Name);
        Assert.Equal(7, cred1.FieldMappings.Count);

        // Act 2 - Second run (idempotency check)
        await seeder.SeedAsync();

        // Assert 2 - Count remains exactly 1
        var totalCreds = await context.ApiCredentials.CountAsync();
        Assert.Equal(1, totalCreds);
    }

    [Fact]
    public async Task SeedAsync_InProduction_ShouldDoNothing()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var options = CreateOptions(dbName);
        var clock = new FakeClock();
        var user = new FakeCurrentUser { UserId = 1, BranchId = 1 };
        var prodEnv = new DummyHostEnvironment { EnvironmentName = Environments.Production };

        using var context = new InventoryDbContext(options, user, clock);
        var seeder = new DevDataSeeder(context, _protector, prodEnv, NullLogger<DevDataSeeder>.Instance);

        // Act
        await seeder.SeedAsync();

        // Assert
        var totalCreds = await context.ApiCredentials.CountAsync();
        Assert.Equal(0, totalCreds);
    }
}
