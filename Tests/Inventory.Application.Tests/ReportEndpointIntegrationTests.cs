using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Inventory.Application.Common.Models;
using Inventory.Application.Reports.DTOs;
using Inventory.Application.Stock.DTOs;
using Inventory.Domain.Entities;
using Inventory.Domain.Enums;
using Inventory.Infrastructure.Persistence;
using Inventory.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Inventory.Application.Tests;

public class ReportEndpointIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ReportEndpointIntegrationTests(TestWebApplicationFactory factory)
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

    private async Task<string> SeedMovementDataAsync(int branchId, string tagPrefix, string materialCode)
    {
        using var scope = _factory.Services.CreateSystemScope();
        var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        var grn = new GrnApiResponse
        {
            BranchId = branchId,
            GrnNo = $"GRN-{Guid.NewGuid():N}"[..10],
            MaterialCode = materialCode,
            BatchNo = "B-01",
            Qty = 100,
            Status = GrnResponseStatus.Processed
        };
        context.GrnApiResponses.Add(grn);
        await context.SaveChangesAsync();

        var tag = new Tag
        {
            BranchId = branchId,
            GrnApiResponseId = grn.Id,
            TagNo = $"{tagPrefix}-{Guid.NewGuid():N}"[..12],
            Qty = 100,
            RemainingQty = 100,
            Status = TagStatus.InStock,
            PrintCount = 1
        };
        context.Tags.Add(tag);

        var locCode = $"LOC-{Guid.NewGuid():N}"[..8];
        var loc = new Location
        {
            BranchId = branchId,
            Code = locCode,
            Zone = "MAIN",
            Capacity = 1000,
            IsActive = true
        };
        context.Locations.Add(loc);
        await context.SaveChangesAsync();

        var sr = new StockRecord
        {
            BranchId = branchId,
            TagId = tag.Id,
            LocationId = loc.Id,
            MaterialCode = materialCode,
            Qty = 100,
            InwardAt = DateTime.UtcNow
        };
        context.StockRecords.Add(sr);

        var inMovement = new StockMovement
        {
            BranchId = branchId,
            TagId = tag.Id,
            LocationId = loc.Id,
            MaterialCode = materialCode,
            MovementType = StockMovementType.In,
            Qty = 100,
            InwardAt = DateTime.UtcNow,
            UserId = 1,
            UserNameSnapshot = "Admin User",
            ClientIp = "127.0.0.1"
        };
        context.StockMovements.Add(inMovement);

        var outMovement = new StockMovement
        {
            BranchId = branchId,
            TagId = tag.Id,
            LocationId = loc.Id,
            MaterialCode = materialCode,
            MovementType = StockMovementType.Out,
            Qty = 30,
            InwardAt = inMovement.InwardAt,
            UserId = 1,
            UserNameSnapshot = "Admin User",
            ClientIp = "127.0.0.1"
        };
        context.StockMovements.Add(outMovement);

        var acceptMovement = new StockMovement
        {
            BranchId = branchId,
            TagId = tag.Id,
            LocationId = loc.Id,
            MaterialCode = materialCode,
            MovementType = StockMovementType.Accept,
            Qty = 30,
            UserId = 1,
            UserNameSnapshot = "Inspector 1",
            ClientIp = "127.0.0.1"
        };
        context.StockMovements.Add(acceptMovement);

        var transferMovement = new StockMovement
        {
            BranchId = branchId,
            TagId = tag.Id,
            LocationId = loc.Id,
            MaterialCode = materialCode,
            MovementType = StockMovementType.Transfer,
            Qty = 70,
            Reason = $"Transferred from LOC-A to {loc.Code}",
            UserId = 1,
            UserNameSnapshot = "Operator 1",
            ClientIp = "127.0.0.1"
        };
        context.StockMovements.Add(transferMovement);

        await context.SaveChangesAsync();
        return tag.TagNo;
    }

    [Fact]
    public async Task PolicyMatrix_AnonymousGets401_ViewerGets200()
    {
        // 1. Anonymous access gets 401 Unauthorized
        _client.DefaultRequestHeaders.Authorization = null;
        var anonResp = await _client.GetAsync("/api/reports/inward");
        Assert.Equal(HttpStatusCode.Unauthorized, anonResp.StatusCode);

        // 2. Viewer role gets 200 OK
        SetAuthToken("Viewer", branchId: 1);
        var viewerResp = await _client.GetAsync("/api/reports/inward");
        Assert.Equal(HttpStatusCode.OK, viewerResp.StatusCode);
    }

    [Fact]
    public async Task BranchIsolation_StoreKeeperRestrictedToBranch_CrossBranchBlocked()
    {
        SetAuthToken("StoreKeeper", branchId: 1);

        // Attempting crossBranch = true -> 422 Unprocessable Entity
        var crossResp = await _client.GetAsync("/api/reports/inward?crossBranch=true");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, crossResp.StatusCode);

        // Attempting to query another branchId -> 422 Unprocessable Entity
        var otherBranchResp = await _client.GetAsync("/api/reports/inward?branchId=2");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, otherBranchResp.StatusCode);

        // Standard branch query -> 200 OK
        var okResp = await _client.GetAsync("/api/reports/inward");
        Assert.Equal(HttpStatusCode.OK, okResp.StatusCode);
    }

    [Fact]
    public async Task Admin_CanQueryCrossBranchReports()
    {
        SetAuthToken("Admin", branchId: 1);

        var crossResp = await _client.GetAsync("/api/reports/inward?crossBranch=true");
        Assert.Equal(HttpStatusCode.OK, crossResp.StatusCode);
        var paged = await crossResp.Content.ReadFromJsonAsync<PagedResult<InwardReportRowDto>>();
        Assert.NotNull(paged);
    }

    [Fact]
    public async Task Endpoints_ReturnSuccessfulPaginatedData()
    {
        var matCode = $"RPT-MAT-{Guid.NewGuid():N}"[..12];
        var tagNo = await SeedMovementDataAsync(1, "TAG-RPT", matCode);
        SetAuthToken("StoreKeeper", branchId: 1);

        // 1. Inward Report
        var inResp = await _client.GetAsync($"/api/reports/inward?materialCode={matCode}");
        Assert.Equal(HttpStatusCode.OK, inResp.StatusCode);
        var inData = await inResp.Content.ReadFromJsonAsync<PagedResult<InwardReportRowDto>>();
        Assert.NotNull(inData);
        Assert.True(inData.TotalCount >= 1);
        Assert.Contains(inData.Items, i => i.MaterialCode == matCode);

        // 2. Outward Report
        var outResp = await _client.GetAsync($"/api/reports/outward?materialCode={matCode}");
        Assert.Equal(HttpStatusCode.OK, outResp.StatusCode);
        var outData = await outResp.Content.ReadFromJsonAsync<PagedResult<OutwardReportRowDto>>();
        Assert.NotNull(outData);
        Assert.True(outData.TotalCount >= 1);

        // 3. In-Stock Report
        var stockResp = await _client.GetAsync($"/api/reports/in-stock?materialCode={matCode}");
        Assert.Equal(HttpStatusCode.OK, stockResp.StatusCode);
        var stockData = await stockResp.Content.ReadFromJsonAsync<PagedResult<InStockReportRowDto>>();
        Assert.NotNull(stockData);
        Assert.True(stockData.TotalCount >= 1);

        // 4. Label Scan Report
        var scanResp = await _client.GetAsync($"/api/reports/label-scan?tagNo={tagNo}");
        Assert.Equal(HttpStatusCode.OK, scanResp.StatusCode);
        var scanData = await scanResp.Content.ReadFromJsonAsync<PagedResult<LabelScanReportRowDto>>();
        Assert.NotNull(scanData);

        // 5. Overall Report
        var overallResp = await _client.GetAsync("/api/reports/overall?groupBy=material");
        Assert.Equal(HttpStatusCode.OK, overallResp.StatusCode);
        var overallData = await overallResp.Content.ReadFromJsonAsync<PagedResult<OverallReportRowDto>>();
        Assert.NotNull(overallData);
        Assert.True(overallData.TotalCount >= 1);

        // 6. Accept / Reject Report
        var arResp = await _client.GetAsync($"/api/reports/accept-reject?materialCode={matCode}");
        Assert.Equal(HttpStatusCode.OK, arResp.StatusCode);
        var arData = await arResp.Content.ReadFromJsonAsync<PagedResult<AcceptRejectReportRowDto>>();
        Assert.NotNull(arData);
        Assert.True(arData.TotalCount >= 1);

        // 7. Location Transfer Report
        var trResp = await _client.GetAsync($"/api/reports/location-transfer?tagNo={tagNo}");
        Assert.Equal(HttpStatusCode.OK, trResp.StatusCode);
        var trData = await trResp.Content.ReadFromJsonAsync<PagedResult<LocationTransferReportRowDto>>();
        Assert.NotNull(trData);
        Assert.True(trData.TotalCount >= 1);

        // 8. Live Dashboard API
        var dashResp = await _client.GetAsync("/api/reports/dashboard-live");
        Assert.Equal(HttpStatusCode.OK, dashResp.StatusCode);
        var dashData = await dashResp.Content.ReadFromJsonAsync<DashboardLiveMetricsDto>();
        Assert.NotNull(dashData);
        Assert.True(dashData.TotalRegisteredItems >= 1);
        Assert.True(dashData.TotalInStockUnits >= 1);
        Assert.NotNull(dashData.RecentTransactions);
    }

    [Theory]
    [InlineData("/api/reports/inward")]
    [InlineData("/api/reports/outward")]
    [InlineData("/api/reports/in-stock")]
    [InlineData("/api/reports/label-scan")]
    [InlineData("/api/reports/overall")]
    [InlineData("/api/reports/accept-reject")]
    [InlineData("/api/reports/location-transfer")]
    [InlineData("/api/reports/dashboard-live")]
    public async Task AllReportEndpoints_OnFormLoadWithoutParameters_Return200OK(string endpoint)
    {
        SetAuthToken("Viewer", branchId: 1);

        var response = await _client.GetAsync(endpoint);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/reports/inward")]
    [InlineData("/api/reports/outward")]
    public async Task ReportEndpoints_InvalidDateRange_Returns422UnprocessableEntity(string endpoint)
    {
        SetAuthToken("Viewer", branchId: 1);

        var url = $"{endpoint}?fromDate=2026-12-31&toDate=2026-01-01";
        var response = await _client.GetAsync(url);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }
}
