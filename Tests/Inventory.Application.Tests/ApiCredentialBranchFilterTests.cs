using Inventory.Domain.Entities;
using Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Application.Tests;

public class ApiCredentialBranchFilterTests
{
    private DbContextOptions<InventoryDbContext> CreateOptions(string dbName) =>
        new DbContextOptionsBuilder<InventoryDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

    [Fact]
    public async Task QueryFilter_ShouldHideApiCredentialsFromOtherBranches()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var options = CreateOptions(dbName);
        var clock = new FakeClock();
        var branch1User = new FakeCurrentUser { UserId = 10, BranchId = 1 };
        var branch2User = new FakeCurrentUser { UserId = 20, BranchId = 2 };

        // Seed Branch 1 credential
        using (var context1 = new InventoryDbContext(options, branch1User, clock))
        {
            context1.ApiCredentials.Add(new ApiCredential
            {
                Id = 1,
                BranchId = 1,
                Name = "Branch 1 SAP Client",
                BaseUrl = "https://b1.sap.local/api"
            });
            await context1.SaveChangesAsync();
        }

        // Seed Branch 2 credential
        using (var context2 = new InventoryDbContext(options, branch2User, clock))
        {
            context2.ApiCredentials.Add(new ApiCredential
            {
                Id = 2,
                BranchId = 2,
                Name = "Branch 2 SAP Client",
                BaseUrl = "https://b2.sap.local/api"
            });
            await context2.SaveChangesAsync();
        }

        // Act - Query as Branch 1 user
        using var queryContext = new InventoryDbContext(options, branch1User, clock);
        var branch1Credentials = await queryContext.ApiCredentials.ToListAsync();

        // Assert - Only Branch 1 credential is visible
        Assert.Single(branch1Credentials);
        Assert.Equal(1, branch1Credentials[0].Id);
        Assert.Equal("Branch 1 SAP Client", branch1Credentials[0].Name);

        // Act - Admin query with IgnoreQueryFilters
        var allCredentials = await queryContext.ApiCredentials.IgnoreQueryFilters().ToListAsync();

        // Assert - Both credentials visible
        Assert.Equal(2, allCredentials.Count);
    }
}
