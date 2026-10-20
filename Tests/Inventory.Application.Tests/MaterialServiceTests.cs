using Inventory.Application.Materials.DTOs;
using Inventory.Domain.Entities;
using Inventory.Domain.Exceptions;
using Inventory.Infrastructure.Persistence;
using Inventory.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Inventory.Application.Tests;

public class MaterialServiceTests
{
    private (InventoryDbContext Context, MaterialService Service) CreateService(string dbName)
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        var context = new InventoryDbContext(options, SystemCurrentUser.Instance);
        var service = new MaterialService(context);
        return (context, service);
    }

    [Fact]
    public async Task CreateAsync_NormalisesCodeToUppercaseAndTrimmed()
    {
        var (context, service) = CreateService(Guid.NewGuid().ToString());

        var dto = new CreateMaterialDto("  mat-001  ", "  Steel Bolt  ", "  pcs  ", "  Zone-A  ");
        var result = await service.CreateAsync(dto);

        Assert.Equal("MAT-001", result.Code);
        Assert.Equal("Steel Bolt", result.Name);
        Assert.Equal("pcs", result.Uom);
        Assert.Equal("Zone-A", result.Zone);
        Assert.True(result.IsActive);

        var saved = await context.Materials.FirstAsync(m => m.Id == result.Id);
        Assert.Equal("MAT-001", saved.Code);
        Assert.Equal("Steel Bolt", saved.Name);
    }

    [Theory]
    [InlineData("", "Name", "pcs")]
    [InlineData("   ", "Name", "pcs")]
    [InlineData(null, "Name", "pcs")]
    public async Task CreateAsync_EmptyCode_ThrowsDomainException(string? code, string name, string uom)
    {
        var (_, service) = CreateService(Guid.NewGuid().ToString());
        var dto = new CreateMaterialDto(code!, name, uom, null);

        var ex = await Assert.ThrowsAsync<DomainException>(() => service.CreateAsync(dto));
        Assert.Contains("Material code is required", ex.Message);
    }

    [Fact]
    public async Task CreateAsync_CodeExceeds30Chars_ThrowsDomainException()
    {
        var (_, service) = CreateService(Guid.NewGuid().ToString());
        var dto = new CreateMaterialDto(new string('A', 31), "Valid Name", "pcs", null);

        var ex = await Assert.ThrowsAsync<DomainException>(() => service.CreateAsync(dto));
        Assert.Contains("cannot exceed 30 characters", ex.Message);
    }

    [Theory]
    [InlineData("", "pcs")]
    [InlineData("   ", "pcs")]
    [InlineData(null, "pcs")]
    public async Task CreateAsync_EmptyName_ThrowsDomainException(string? name, string uom)
    {
        var (_, service) = CreateService(Guid.NewGuid().ToString());
        var dto = new CreateMaterialDto("MAT-01", name!, uom, null);

        var ex = await Assert.ThrowsAsync<DomainException>(() => service.CreateAsync(dto));
        Assert.Contains("Material name is required", ex.Message);
    }

    [Fact]
    public async Task CreateAsync_NameExceeds150Chars_ThrowsDomainException()
    {
        var (_, service) = CreateService(Guid.NewGuid().ToString());
        var dto = new CreateMaterialDto("MAT-01", new string('N', 151), "pcs", null);

        var ex = await Assert.ThrowsAsync<DomainException>(() => service.CreateAsync(dto));
        Assert.Contains("cannot exceed 150 characters", ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task CreateAsync_EmptyUom_ThrowsDomainException(string? uom)
    {
        var (_, service) = CreateService(Guid.NewGuid().ToString());
        var dto = new CreateMaterialDto("MAT-01", "Valid Name", uom!, null);

        var ex = await Assert.ThrowsAsync<DomainException>(() => service.CreateAsync(dto));
        Assert.Contains("Material UOM is required", ex.Message);
    }

    [Fact]
    public async Task CreateAsync_UomExceeds10Chars_ThrowsDomainException()
    {
        var (_, service) = CreateService(Guid.NewGuid().ToString());
        var dto = new CreateMaterialDto("MAT-01", "Valid Name", new string('U', 11), null);

        var ex = await Assert.ThrowsAsync<DomainException>(() => service.CreateAsync(dto));
        Assert.Contains("cannot exceed 10 characters", ex.Message);
    }

    [Fact]
    public async Task CreateAsync_ZoneExceeds20Chars_ThrowsDomainException()
    {
        var (_, service) = CreateService(Guid.NewGuid().ToString());
        var dto = new CreateMaterialDto("MAT-01", "Valid Name", "pcs", new string('Z', 21));

        var ex = await Assert.ThrowsAsync<DomainException>(() => service.CreateAsync(dto));
        Assert.Contains("cannot exceed 20 characters", ex.Message);
    }

    [Fact]
    public async Task CreateAsync_DuplicateCode_ThrowsDomainExceptionWithClearMessage()
    {
        var (_, service) = CreateService(Guid.NewGuid().ToString());

        await service.CreateAsync(new CreateMaterialDto("MAT-UNIQUE", "First", "pcs", null));

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            service.CreateAsync(new CreateMaterialDto("mat-unique", "Second", "kg", null)));

        Assert.Equal("Material with code 'MAT-UNIQUE' already exists.", ex.Message);
    }

    [Fact]
    public async Task UpdateAsync_UpdatesPropertiesAndPreservesImmutableCode()
    {
        var (context, service) = CreateService(Guid.NewGuid().ToString());

        var created = await service.CreateAsync(new CreateMaterialDto("ORIG-CODE", "Original Name", "pcs", "Zone-1"));

        var updated = await service.UpdateAsync(created.Id, new UpdateMaterialDto("Updated Name", "box", "Zone-2", true));

        Assert.Equal("ORIG-CODE", updated.Code); // Code remains unchanged
        Assert.Equal("Updated Name", updated.Name);
        Assert.Equal("box", updated.Uom);
        Assert.Equal("Zone-2", updated.Zone);

        var fromDb = await context.Materials.FindAsync(created.Id);
        Assert.Equal("ORIG-CODE", fromDb!.Code);
        Assert.Equal("Updated Name", fromDb.Name);
    }

    [Fact]
    public async Task DeleteAsync_SoftDeactivatesMaterial()
    {
        var (context, service) = CreateService(Guid.NewGuid().ToString());

        var created = await service.CreateAsync(new CreateMaterialDto("MAT-DEL", "To Deactivate", "pcs", null));
        Assert.True(created.IsActive);

        await service.DeleteAsync(created.Id);

        var fromDb = await context.Materials.FindAsync(created.Id);
        Assert.NotNull(fromDb);
        Assert.False(fromDb.IsActive);
    }

    [Fact]
    public void DeactivateDomainHook_WhenStockExists_ThrowsDomainException()
    {
        var material = new Material
        {
            Code = "MAT-STOCK",
            Name = "Has Stock",
            Uom = "pcs",
            IsActive = true
        };

        var ex = Assert.Throws<DomainException>(() => material.Deactivate(currentStock: 10));
        Assert.Contains("Cannot deactivate material with existing stock", ex.Message);
        Assert.True(material.IsActive);
    }

    [Fact]
    public async Task GetPagedAsync_PagingAndFilteringWorkCorrectly()
    {
        var (_, service) = CreateService(Guid.NewGuid().ToString());

        await service.CreateAsync(new CreateMaterialDto("MAT-A1", "Alpha Item", "pcs", "Zone-1"));
        await service.CreateAsync(new CreateMaterialDto("MAT-A2", "Alpha Plus", "pcs", "Zone-1"));
        await service.CreateAsync(new CreateMaterialDto("MAT-B1", "Beta Item", "kg", "Zone-2"));
        var inactive = await service.CreateAsync(new CreateMaterialDto("MAT-C1", "Gamma Item", "pcs", "Zone-2"));
        await service.DeleteAsync(inactive.Id);

        // Search by name
        var searchResult = await service.GetPagedAsync(new MaterialFilterDto(Search: "Alpha"));
        Assert.Equal(2, searchResult.TotalCount);
        Assert.All(searchResult.Items, i => Assert.Contains("Alpha", i.Name));

        // Filter by zone
        var zoneResult = await service.GetPagedAsync(new MaterialFilterDto(Zone: "Zone-2"));
        Assert.Equal(2, zoneResult.TotalCount);

        // Active only
        var activeOnlyResult = await service.GetPagedAsync(new MaterialFilterDto(ActiveOnly: true));
        Assert.Equal(3, activeOnlyResult.TotalCount);

        // Paging clamps pageSize to 200
        var pagedResult = await service.GetPagedAsync(new MaterialFilterDto(Page: 1, PageSize: 500));
        Assert.Equal(200, pagedResult.PageSize);
    }

    [Fact]
    public async Task GetByIdAsync_NonExistent_ThrowsNotFoundException()
    {
        var (_, service) = CreateService(Guid.NewGuid().ToString());

        await Assert.ThrowsAsync<NotFoundException>(() => service.GetByIdAsync(99999));
    }
}
