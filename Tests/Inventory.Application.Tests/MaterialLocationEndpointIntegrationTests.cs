using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Inventory.Application.Common.Models;
using Inventory.Application.Locations.DTOs;
using Inventory.Application.Materials.DTOs;
using Inventory.Domain.Entities;
using Inventory.Infrastructure.Persistence;
using Inventory.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Inventory.Application.Tests;

public class MaterialLocationEndpointIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public MaterialLocationEndpointIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _factory.EnsureSeeded();
    }

    private void SetAuthToken(string role, int branchId = 1)
    {
        var token = _factory.CreateTokenForRole(role, branchId);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    [Fact]
    public async Task PolicyMatrix_Operator_Gets403OnBothLists()
    {
        SetAuthToken("Operator", branchId: 1);

        var materialsResponse = await _client.GetAsync("/api/materials");
        Assert.Equal(HttpStatusCode.Forbidden, materialsResponse.StatusCode);

        var locationsResponse = await _client.GetAsync("/api/locations");
        Assert.Equal(HttpStatusCode.Forbidden, locationsResponse.StatusCode);
    }

    [Fact]
    public async Task PolicyMatrix_StoreKeeper_Gets200OnLists_But403OnCreates()
    {
        SetAuthToken("StoreKeeper", branchId: 1);

        var materialsResponse = await _client.GetAsync("/api/materials");
        Assert.Equal(HttpStatusCode.OK, materialsResponse.StatusCode);

        var locationsResponse = await _client.GetAsync("/api/locations");
        Assert.Equal(HttpStatusCode.OK, locationsResponse.StatusCode);

        var createMatResponse = await _client.PostAsJsonAsync("/api/materials", new CreateMaterialDto("SK-MAT", "Test", "pcs", null));
        Assert.Equal(HttpStatusCode.Forbidden, createMatResponse.StatusCode);

        var createLocResponse = await _client.PostAsJsonAsync("/api/locations", new CreateLocationDto("SK-LOC", "Zone-A", 100));
        Assert.Equal(HttpStatusCode.Forbidden, createLocResponse.StatusCode);
    }

    [Fact]
    public async Task PolicyMatrix_Viewer_Gets200OnLists_But403OnModifications()
    {
        SetAuthToken("Viewer", branchId: 1);

        var materialsResponse = await _client.GetAsync("/api/materials");
        Assert.Equal(HttpStatusCode.OK, materialsResponse.StatusCode);

        var locationsResponse = await _client.GetAsync("/api/locations");
        Assert.Equal(HttpStatusCode.OK, locationsResponse.StatusCode);

        var createMatResponse = await _client.PostAsJsonAsync("/api/materials", new CreateMaterialDto("VW-MAT", "Test", "pcs", null));
        Assert.Equal(HttpStatusCode.Forbidden, createMatResponse.StatusCode);

        var deleteMatResponse = await _client.DeleteAsync("/api/materials/1");
        Assert.Equal(HttpStatusCode.Forbidden, deleteMatResponse.StatusCode);
    }

    [Fact]
    public async Task Admin_CanPerformFullCrudOnMaterials()
    {
        SetAuthToken("Admin", branchId: 1);

        var code = "INT-MAT-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var createDto = new CreateMaterialDto(code, "Integration Material", "pcs", "Zone-A");

        // 1. Create
        var postResponse = await _client.PostAsJsonAsync("/api/materials", createDto);
        Assert.Equal(HttpStatusCode.Created, postResponse.StatusCode);

        var created = await postResponse.Content.ReadFromJsonAsync<MaterialDto>();
        Assert.NotNull(created);
        Assert.Equal(code, created.Code);
        Assert.Equal("Integration Material", created.Name);

        // 2. GetById
        var getResponse = await _client.GetAsync($"/api/materials/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var fetched = await getResponse.Content.ReadFromJsonAsync<MaterialDto>();
        Assert.Equal(created.Id, fetched!.Id);

        // 3. Update
        var updateDto = new UpdateMaterialDto("Updated Material Name", "box", "Zone-B", true);
        var putResponse = await _client.PutAsJsonAsync($"/api/materials/{created.Id}", updateDto);
        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);
        var updated = await putResponse.Content.ReadFromJsonAsync<MaterialDto>();
        Assert.Equal("Updated Material Name", updated!.Name);
        Assert.Equal(code, updated.Code); // Code immutable!

        // 4. Soft Delete
        var delResponse = await _client.DeleteAsync($"/api/materials/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delResponse.StatusCode);

        // Verify deactivated
        var afterDelResponse = await _client.GetAsync($"/api/materials/{created.Id}");
        var afterDel = await afterDelResponse.Content.ReadFromJsonAsync<MaterialDto>();
        Assert.False(afterDel!.IsActive);
    }

    [Fact]
    public async Task Admin_DuplicateMaterialCode_Returns422ProblemDetails()
    {
        SetAuthToken("Admin", branchId: 1);

        var code = "DUP-MAT-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var createDto = new CreateMaterialDto(code, "Original Item", "pcs", null);

        var firstResponse = await _client.PostAsJsonAsync("/api/materials", createDto);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);

        var duplicateResponse = await _client.PostAsJsonAsync("/api/materials", createDto);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, duplicateResponse.StatusCode);

        var problem = await duplicateResponse.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(422, problem.Status);
        Assert.Contains(code, problem.Detail);
    }

    [Fact]
    public async Task Admin_DuplicateLocationCodeInSameBranch_Returns422ProblemDetails()
    {
        SetAuthToken("Admin", branchId: 1);

        var code = "DUP-LOC-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var createDto = new CreateLocationDto(code, "Zone-A", 100);

        var firstResponse = await _client.PostAsJsonAsync("/api/locations", createDto);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);

        var duplicateResponse = await _client.PostAsJsonAsync("/api/locations", createDto);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, duplicateResponse.StatusCode);

        var problem = await duplicateResponse.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(422, problem.Status);
        Assert.Contains(code, problem.Detail);
    }

    [Fact]
    public async Task Admin_UpdateLocation_StaleRowVersion_Returns409ConflictProblemDetails()
    {
        SetAuthToken("Admin", branchId: 1);

        var code = "CONCUR-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var createResponse = await _client.PostAsJsonAsync("/api/locations", new CreateLocationDto(code, "Zone-A", 100));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<LocationDto>();
        Assert.NotNull(created);

        // Set simulated rowversion in database
        using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            var loc = await db.Locations.FindAsync(created.Id);
            loc!.RowVersion = new byte[] { 10, 20, 30, 40 };
            await db.SaveChangesAsync();
        }

        // Attempt update with a stale / mismatched rowversion
        var staleToken = new byte[] { 99, 99, 99, 99 };
        var updateDto = new UpdateLocationDto("Zone-B", 150, true, staleToken);

        var putResponse = await _client.PutAsJsonAsync($"/api/locations/{created.Id}", updateDto);
        Assert.Equal(HttpStatusCode.Conflict, putResponse.StatusCode);

        var problem = await putResponse.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(409, problem.Status);
        Assert.Equal("Conflict", problem.Title);
        Assert.Contains("concurrency conflict", problem.Detail, StringComparison.OrdinalIgnoreCase);
    }
}
