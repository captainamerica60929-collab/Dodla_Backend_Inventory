using System.Security.Claims;
using Inventory.Application.Common.Interfaces;
using Inventory.Application.Locations.DTOs;
using Inventory.Domain.Entities;
using Inventory.Domain.Exceptions;
using Inventory.Infrastructure.Persistence;
using Inventory.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Inventory.Application.Tests;

public class LocationServiceTests
{
    private class TestCurrentUser : ICurrentUser
    {
        public bool IsSystem { get; set; }
        public int UserId { get; set; } = 10;
        public int BranchId { get; set; } = 1;
        public string Role { get; set; } = "Admin";
        public string Name { get; set; } = "Test";
        public int? EmployeeId { get; set; } = 1;
    }

    private (InventoryDbContext Context, LocationService Service) CreateService(string dbName, int branchId)
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        var currentUser = new TestCurrentUser { BranchId = branchId };
        var context = new InventoryDbContext(options, currentUser);
        var service = new LocationService(context);
        return (context, service);
    }

    [Fact]
    public async Task CreateAsync_NormalisesCodeToUppercaseAndTrimmed()
    {
        var (context, service) = CreateService(Guid.NewGuid().ToString(), branchId: 1);

        var dto = new CreateLocationDto("  loc-a01  ", "  Zone-A  ", 100);
        var result = await service.CreateAsync(dto);

        Assert.Equal("LOC-A01", result.Code);
        Assert.Equal("Zone-A", result.Zone);
        Assert.Equal(100, result.Capacity);
        Assert.Equal(0, result.UsedQty);
        Assert.Equal(100, result.FreeQty);
        Assert.True(result.IsActive);
        Assert.Equal(1, result.BranchId);

        var saved = await context.Locations.FirstAsync(l => l.Id == result.Id);
        Assert.Equal("LOC-A01", saved.Code);
    }

    [Theory]
    [InlineData("", "Zone-A", 10)]
    [InlineData("   ", "Zone-A", 10)]
    [InlineData(null, "Zone-A", 10)]
    public async Task CreateAsync_EmptyCode_ThrowsDomainException(string? code, string zone, int capacity)
    {
        var (_, service) = CreateService(Guid.NewGuid().ToString(), branchId: 1);
        var dto = new CreateLocationDto(code!, zone, capacity);

        var ex = await Assert.ThrowsAsync<DomainException>(() => service.CreateAsync(dto));
        Assert.Contains("Location code is required", ex.Message);
    }

    [Fact]
    public async Task CreateAsync_CodeExceeds30Chars_ThrowsDomainException()
    {
        var (_, service) = CreateService(Guid.NewGuid().ToString(), branchId: 1);
        var dto = new CreateLocationDto(new string('L', 31), "Zone-A", 50);

        var ex = await Assert.ThrowsAsync<DomainException>(() => service.CreateAsync(dto));
        Assert.Contains("cannot exceed 30 characters", ex.Message);
    }

    [Theory]
    [InlineData("", 10)]
    [InlineData("   ", 10)]
    [InlineData(null, 10)]
    public async Task CreateAsync_EmptyZone_ThrowsDomainException(string? zone, int capacity)
    {
        var (_, service) = CreateService(Guid.NewGuid().ToString(), branchId: 1);
        var dto = new CreateLocationDto("LOC-01", zone!, capacity);

        var ex = await Assert.ThrowsAsync<DomainException>(() => service.CreateAsync(dto));
        Assert.Contains("Location zone is required", ex.Message);
    }

    [Fact]
    public async Task CreateAsync_ZoneExceeds20Chars_ThrowsDomainException()
    {
        var (_, service) = CreateService(Guid.NewGuid().ToString(), branchId: 1);
        var dto = new CreateLocationDto("LOC-01", new string('Z', 21), 50);

        var ex = await Assert.ThrowsAsync<DomainException>(() => service.CreateAsync(dto));
        Assert.Contains("cannot exceed 20 characters", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task CreateAsync_InvalidCapacity_ThrowsDomainException(int capacity)
    {
        var (_, service) = CreateService(Guid.NewGuid().ToString(), branchId: 1);
        var dto = new CreateLocationDto("LOC-01", "Zone-A", capacity);

        var ex = await Assert.ThrowsAsync<DomainException>(() => service.CreateAsync(dto));
        Assert.Contains("greater than zero", ex.Message);
    }

    [Fact]
    public async Task CreateAsync_DuplicateCodeInSameBranch_ThrowsDomainException()
    {
        var (_, service) = CreateService(Guid.NewGuid().ToString(), branchId: 1);

        await service.CreateAsync(new CreateLocationDto("BIN-01", "Zone-A", 100));

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            service.CreateAsync(new CreateLocationDto("bin-01", "Zone-B", 50)));

        Assert.Equal("Location with code 'BIN-01' already exists in this branch.", ex.Message);
    }

    [Fact]
    public async Task CreateAsync_SameCodeInDifferentBranches_Succeeds()
    {
        var dbName = Guid.NewGuid().ToString();
        var (_, serviceBranch1) = CreateService(dbName, branchId: 1);
        var (_, serviceBranch2) = CreateService(dbName, branchId: 2);

        // Branch 1 creates BIN-01
        var loc1 = await serviceBranch1.CreateAsync(new CreateLocationDto("BIN-01", "Zone-A", 100));
        Assert.Equal("BIN-01", loc1.Code);
        Assert.Equal(1, loc1.BranchId);

        // Branch 2 creates BIN-01 (same code in different branch is allowed!)
        var loc2 = await serviceBranch2.CreateAsync(new CreateLocationDto("BIN-01", "Zone-A", 200));
        Assert.Equal("BIN-01", loc2.Code);
        Assert.Equal(2, loc2.BranchId);
        Assert.NotEqual(loc1.Id, loc2.Id);
    }

    [Fact]
    public async Task BranchFilter_UserOnlySeesTheirOwnBranchLocations()
    {
        var dbName = Guid.NewGuid().ToString();
        var (_, serviceBranch1) = CreateService(dbName, branchId: 1);
        var (_, serviceBranch2) = CreateService(dbName, branchId: 2);

        var loc1 = await serviceBranch1.CreateAsync(new CreateLocationDto("LOC-B1", "Zone-A", 100));
        var loc2 = await serviceBranch2.CreateAsync(new CreateLocationDto("LOC-B2", "Zone-A", 100));

        // User in Branch 1 lists locations
        var list1 = await serviceBranch1.GetPagedAsync(new LocationFilterDto());
        Assert.Single(list1.Items);
        Assert.Equal(loc1.Id, list1.Items[0].Id);

        // User in Branch 1 cannot get Branch 2 location by ID
        await Assert.ThrowsAsync<NotFoundException>(() => serviceBranch1.GetByIdAsync(loc2.Id));

        // User in Branch 2 lists locations
        var list2 = await serviceBranch2.GetPagedAsync(new LocationFilterDto());
        Assert.Single(list2.Items);
        Assert.Equal(loc2.Id, list2.Items[0].Id);
    }

    [Fact]
    public async Task UpdateAsync_StaleRowVersion_ThrowsConcurrencyException()
    {
        var (context, service) = CreateService(Guid.NewGuid().ToString(), branchId: 1);

        var created = await service.CreateAsync(new CreateLocationDto("LOC-STALE", "Zone-A", 100));

        // Simulate DB holding a rowversion
        var loc = await context.Locations.FindAsync(created.Id);
        loc!.RowVersion = new byte[] { 1, 2, 3, 4 };
        await context.SaveChangesAsync();

        var staleToken = new byte[] { 9, 9, 9, 9 };
        var updateDto = new UpdateLocationDto("Zone-B", 150, true, staleToken);

        var ex = await Assert.ThrowsAsync<ConcurrencyException>(() => service.UpdateAsync(created.Id, updateDto));
        Assert.Contains("concurrency conflict", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdateAsync_MatchingRowVersion_Succeeds()
    {
        var (context, service) = CreateService(Guid.NewGuid().ToString(), branchId: 1);

        var created = await service.CreateAsync(new CreateLocationDto("LOC-OK", "Zone-A", 100));

        var currentToken = new byte[] { 1, 2, 3, 4 };
        var loc = await context.Locations.FindAsync(created.Id);
        loc!.RowVersion = currentToken;
        await context.SaveChangesAsync();

        var updateDto = new UpdateLocationDto("Zone-B", 150, true, currentToken);
        var result = await service.UpdateAsync(created.Id, updateDto);

        Assert.Equal("Zone-B", result.Zone);
        Assert.Equal(150, result.Capacity);
    }

    [Fact]
    public void Update_CapacityBelowUsedQty_ThrowsDomainException()
    {
        var location = new Location
        {
            Code = "LOC-CAP",
            Zone = "Zone-A",
            Capacity = 100,
            IsActive = true
        };

        // When usedQty is 50, reducing capacity to 40 is blocked
        var ex = Assert.Throws<DomainException>(() => location.Update("Zone-A", 40, true, usedQty: 50));
        Assert.Contains("cannot be reduced below used quantity", ex.Message);
    }

    [Fact]
    public void DeactivateDomainHook_WhenStockExists_ThrowsDomainException()
    {
        var location = new Location
        {
            Code = "LOC-STOCK",
            Zone = "Zone-A",
            Capacity = 100,
            IsActive = true
        };

        var ex = Assert.Throws<DomainException>(() => location.Deactivate(currentStock: 10));
        Assert.Contains("Cannot deactivate location with existing stock", ex.Message);
        Assert.True(location.IsActive);
    }

    [Fact]
    public async Task DeleteAsync_SoftDeactivatesLocation()
    {
        var (context, service) = CreateService(Guid.NewGuid().ToString(), branchId: 1);

        var created = await service.CreateAsync(new CreateLocationDto("LOC-DEL", "Zone-A", 100));
        Assert.True(created.IsActive);

        await service.DeleteAsync(created.Id);

        var fromDb = await context.Locations.FindAsync(created.Id);
        Assert.NotNull(fromDb);
        Assert.False(fromDb.IsActive);
    }
}
