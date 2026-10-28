using Inventory.Application.GrnResponses.DTOs;
using Inventory.Domain.Entities;
using Inventory.Domain.Enums;
using Inventory.Domain.Exceptions;
using Inventory.Infrastructure.Persistence;
using Inventory.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Application.Tests;

public class GrnResponseQueryAndDiscardTests
{
    private DbContextOptions<InventoryDbContext> CreateOptions(string dbName) =>
        new DbContextOptionsBuilder<InventoryDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

    [Fact]
    public async Task GetPagedAsync_ShouldHideRowsFromOtherBranches()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var options = CreateOptions(dbName);
        var clock = new FakeClock();
        var branch1User = new FakeCurrentUser { UserId = 1, BranchId = 1 };
        var branch2User = new FakeCurrentUser { UserId = 2, BranchId = 2 };

        using (var setup1 = new InventoryDbContext(options, branch1User, clock))
        {
            setup1.GrnApiResponses.Add(new GrnApiResponse
            {
                Id = 1,
                BranchId = 1,
                ApiCredentialId = 1,
                GrnNo = "GRN-B1-01",
                FetchBatchId = Guid.NewGuid(),
                RawJson = "{}",
                Status = GrnResponseStatus.New
            });
            await setup1.SaveChangesAsync();
        }

        using (var setup2 = new InventoryDbContext(options, branch2User, clock))
        {
            setup2.GrnApiResponses.Add(new GrnApiResponse
            {
                Id = 2,
                BranchId = 2,
                ApiCredentialId = 2,
                GrnNo = "GRN-B2-01",
                FetchBatchId = Guid.NewGuid(),
                RawJson = "{}",
                Status = GrnResponseStatus.New
            });
            await setup2.SaveChangesAsync();
        }

        // Act - Query as Branch 1
        using var queryContext = new InventoryDbContext(options, branch1User, clock);
        var service = new GrnResponseService(queryContext);
        var result = await service.GetPagedAsync(new GrnResponseFilterDto());

        // Assert
        Assert.Equal(1, result.Total);
        Assert.Single(result.Items);
        Assert.Equal("GRN-B1-01", result.Items[0].GrnNo);
    }

    [Fact]
    public async Task GetPagedAsync_FiltersAndSorting_ShouldWorkAsExpected()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var options = CreateOptions(dbName);
        var clock = new FakeClock();
        var user = new FakeCurrentUser { UserId = 1, BranchId = 1 };

        using (var setup = new InventoryDbContext(options, user, clock))
        {
            setup.GrnApiResponses.AddRange(
                new GrnApiResponse { Id = 10, BranchId = 1, ApiCredentialId = 1, GrnNo = "50001001", FetchedAt = new DateTime(2026, 10, 10, 10, 0, 0), RawJson = "{}", Status = GrnResponseStatus.New },
                new GrnApiResponse { Id = 20, BranchId = 1, ApiCredentialId = 1, GrnNo = "50001002", FetchedAt = new DateTime(2026, 10, 11, 10, 0, 0), RawJson = "{}", Status = GrnResponseStatus.Error },
                new GrnApiResponse { Id = 30, BranchId = 1, ApiCredentialId = 1, GrnNo = "60002001", FetchedAt = new DateTime(2026, 10, 12, 10, 0, 0), RawJson = "{}", Status = GrnResponseStatus.New }
            );
            await setup.SaveChangesAsync();
        }

        using var queryContext = new InventoryDbContext(options, user, clock);
        var service = new GrnResponseService(queryContext);

        // Act - Prefix filter on GrnNo
        var prefixResult = await service.GetPagedAsync(new GrnResponseFilterDto(GrnNo: "5000"));
        Assert.Equal(2, prefixResult.Total);

        // Act - Inclusive toDate filter
        var toDateResult = await service.GetPagedAsync(new GrnResponseFilterDto(ToDate: new DateTime(2026, 10, 11)));
        Assert.Equal(2, toDateResult.Total);

        // Act - Stable default sort (FetchedAt desc, Id desc)
        var allResult = await service.GetPagedAsync(new GrnResponseFilterDto());
        Assert.Equal(30, allResult.Items[0].Id);
        Assert.Equal(20, allResult.Items[1].Id);
        Assert.Equal(10, allResult.Items[2].Id);
    }

    [Fact]
    public async Task GetPagedAsync_InvalidStatus_ShouldThrowDomainException()
    {
        var options = CreateOptions(Guid.NewGuid().ToString());
        using var context = new InventoryDbContext(options, new FakeCurrentUser(), new FakeClock());
        var service = new GrnResponseService(context);

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            service.GetPagedAsync(new GrnResponseFilterDto(Status: "UnknownStatus")));

        Assert.Contains("Unknown status", ex.Message);
    }

    [Fact]
    public async Task DiscardBatchAsync_WithProcessedRow_ShouldThrowDomainException422()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var options = CreateOptions(dbName);
        var user = new FakeCurrentUser { UserId = 1, BranchId = 1 };
        var clock = new FakeClock();
        var batchId = Guid.NewGuid();

        using (var setup = new InventoryDbContext(options, user, clock))
        {
            setup.GrnApiResponses.AddRange(
                new GrnApiResponse { Id = 1, BranchId = 1, ApiCredentialId = 1, GrnNo = "G1", FetchBatchId = batchId, RawJson = "{}", Status = GrnResponseStatus.Processed },
                new GrnApiResponse { Id = 2, BranchId = 1, ApiCredentialId = 1, GrnNo = "G2", FetchBatchId = batchId, RawJson = "{}", Status = GrnResponseStatus.New }
            );
            await setup.SaveChangesAsync();
        }

        using var context = new InventoryDbContext(options, user, clock);
        var service = new GrnResponseService(context);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            service.DiscardBatchAsync(batchId));

        Assert.Contains("already been processed", ex.Message);
    }

    [Fact]
    public async Task DiscardBatchAsync_WithOnlyNewAndErrorRows_ShouldDeleteSuccessfully()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var options = CreateOptions(dbName);
        var user = new FakeCurrentUser { UserId = 1, BranchId = 1 };
        var clock = new FakeClock();
        var batchId = Guid.NewGuid();

        using (var setup = new InventoryDbContext(options, user, clock))
        {
            setup.GrnApiResponses.AddRange(
                new GrnApiResponse { Id = 1, BranchId = 1, ApiCredentialId = 1, GrnNo = "G1", FetchBatchId = batchId, RawJson = "{}", Status = GrnResponseStatus.New },
                new GrnApiResponse { Id = 2, BranchId = 1, ApiCredentialId = 1, GrnNo = "G2", FetchBatchId = batchId, RawJson = "{}", Status = GrnResponseStatus.Error }
            );
            await setup.SaveChangesAsync();
        }

        using var context = new InventoryDbContext(options, user, clock);
        var service = new GrnResponseService(context);

        // Act
        await service.DiscardBatchAsync(batchId);

        // Assert
        var remaining = await context.GrnApiResponses.Where(r => r.FetchBatchId == batchId).CountAsync();
        Assert.Equal(0, remaining);
    }
}
