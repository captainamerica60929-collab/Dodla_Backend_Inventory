using Inventory.Application.Common.Interfaces;
using Inventory.Domain.Common;
using Inventory.Domain.Exceptions;
using Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Application.Tests;

public class TestBranchEntity : BranchScopedEntity
{
    public string Name { get; set; } = string.Empty;
}

public class TestInventoryDbContext : InventoryDbContext
{
    public TestInventoryDbContext(
        DbContextOptions<InventoryDbContext> options,
        ICurrentUser currentUser,
        IClock clock) : base(options, currentUser, clock)
    {
    }

    public DbSet<TestBranchEntity> TestBranchEntities => Set<TestBranchEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TestBranchEntity>();
        base.OnModelCreating(modelBuilder);
    }
}

public class FakeCurrentUser : ICurrentUser
{
    public int UserId { get; set; } = 10;
    public int BranchId { get; set; } = 1;
}

public class FakeClock : IClock
{
    public DateTime UtcNow { get; set; } = new(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);
}

public class BranchQueryFilterTests
{
    private DbContextOptions<InventoryDbContext> CreateInMemoryOptions(string dbName)
    {
        return new DbContextOptionsBuilder<InventoryDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
    }

    [Fact]
    public async Task QueryFilter_ShouldHideRowsFromOtherBranches()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var options = CreateInMemoryOptions(dbName);
        var clock = new FakeClock();
        var branch1User = new FakeCurrentUser { UserId = 10, BranchId = 1 };

        // Seed data under bypass
        using (var seedContext = new TestInventoryDbContext(options, branch1User, clock))
        {
            seedContext.TestBranchEntities.AddRange(
                new TestBranchEntity { Id = 1, BranchId = 1, Name = "Bin A - Branch 1" },
                new TestBranchEntity { Id = 2, BranchId = 1, Name = "Bin B - Branch 1" }
            );
            await seedContext.SaveChangesAsync();
        }

        // Seed branch 2 data using branch 2 user
        var branch2User = new FakeCurrentUser { UserId = 20, BranchId = 2 };
        using (var seedContext2 = new TestInventoryDbContext(options, branch2User, clock))
        {
            seedContext2.TestBranchEntities.AddRange(
                new TestBranchEntity { Id = 3, BranchId = 2, Name = "Bin C - Branch 2" },
                new TestBranchEntity { Id = 4, BranchId = 2, Name = "Bin D - Branch 2" }
            );
            await seedContext2.SaveChangesAsync();
        }

        // Act - Query as Branch 1 user
        using var queryContext = new TestInventoryDbContext(options, branch1User, clock);
        var branch1Results = await queryContext.TestBranchEntities.ToListAsync();

        // Assert - Only Branch 1 rows visible
        Assert.Equal(2, branch1Results.Count);
        Assert.All(branch1Results, item => Assert.Equal(1, item.BranchId));

        // Act - Admin bypass with IgnoreQueryFilters
        // NOTE: Admin cross-branch reads must use IgnoreQueryFilters or Dapper explicitly.
        var allResults = await queryContext.TestBranchEntities.IgnoreQueryFilters().ToListAsync();

        // Assert - All rows visible with filter ignored
        Assert.Equal(4, allResults.Count);
    }

    [Fact]
    public async Task SaveChangesAsync_ShouldSetBranchId_WhenBranchIdIsZero()
    {
        // Arrange
        var options = CreateInMemoryOptions(Guid.NewGuid().ToString());
        var user = new FakeCurrentUser { UserId = 42, BranchId = 3 };
        var clock = new FakeClock();

        using var context = new TestInventoryDbContext(options, user, clock);
        var entity = new TestBranchEntity { Id = 1, BranchId = 0, Name = "Auto branch item" };
        context.TestBranchEntities.Add(entity);

        // Act
        await context.SaveChangesAsync();

        // Assert
        Assert.Equal(3, entity.BranchId);
        Assert.Equal(42, entity.CreatedBy);
        Assert.Equal(clock.UtcNow, entity.CreatedAt);
    }

    [Fact]
    public async Task SaveChangesAsync_ShouldThrowDomainException_WhenBranchIdDiffersFromCurrentUser()
    {
        // Arrange
        var options = CreateInMemoryOptions(Guid.NewGuid().ToString());
        var user = new FakeCurrentUser { UserId = 42, BranchId = 1 };
        var clock = new FakeClock();

        using var context = new TestInventoryDbContext(options, user, clock);
        var entity = new TestBranchEntity { Id = 1, BranchId = 2, Name = "Mismatched branch item" };
        context.TestBranchEntities.Add(entity);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<DomainException>(() => context.SaveChangesAsync());
        Assert.Contains("Cannot create branch-scoped entity for branch 2", exception.Message);
    }
}
