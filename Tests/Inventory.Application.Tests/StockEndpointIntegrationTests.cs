using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Inventory.Application.Stock.DTOs;
using Inventory.Domain.Entities;
using Inventory.Domain.Enums;
using Inventory.Infrastructure.Persistence;
using Inventory.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Inventory.Application.Tests;

public class StockEndpointIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public StockEndpointIntegrationTests(TestWebApplicationFactory factory)
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

    private async Task<(string tagNo, string locationCode)> SeedInwardItemAsync(
        string tagNo,
        string locationCode,
        int qty = 100,
        string materialCode = "STEEL-001")
    {
        using var scope = _factory.Services.CreateSystemScope();
        var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        var grn = new GrnApiResponse
        {
            BranchId = 1,
            GrnNo = $"GRN-{Guid.NewGuid():N}"[..10],
            MaterialCode = materialCode,
            BatchNo = "B-01",
            Qty = qty,
            Status = GrnResponseStatus.Processed
        };
        context.GrnApiResponses.Add(grn);
        await context.SaveChangesAsync();

        var tag = new Tag
        {
            BranchId = 1,
            GrnApiResponseId = grn.Id,
            TagNo = tagNo,
            Qty = qty,
            RemainingQty = qty,
            Status = TagStatus.Printed,
            PrintCount = 1
        };
        context.Tags.Add(tag);

        if (!context.Locations.Any(l => l.Code == locationCode && l.BranchId == 1))
        {
            var location = new Location
            {
                BranchId = 1,
                Code = locationCode,
                Zone = "DRY",
                Capacity = 1000,
                IsActive = true
            };
            context.Locations.Add(location);
        }

        await context.SaveChangesAsync();
        return (tagNo, locationCode);
    }

    [Fact]
    public async Task PolicyMatrix_ViewerGets403_OperatorAndStoreKeeperGetAllowed()
    {
        var tagNo = $"TAG-POL-{Guid.NewGuid():N}"[..12];
        await SeedInwardItemAsync(tagNo, "LOC-POL-1", 50);

        // Viewer should get 403 Forbidden
        SetAuthToken("Viewer", branchId: 1);
        var viewerResp = await _client.PostAsJsonAsync("/api/stock/inward", new InwardScanRequestDto(tagNo, "LOC-POL-1"));
        Assert.Equal(HttpStatusCode.Forbidden, viewerResp.StatusCode);

        // Anonymous should get 401 Unauthorized
        _client.DefaultRequestHeaders.Authorization = null;
        var anonResp = await _client.PostAsJsonAsync("/api/stock/inward", new InwardScanRequestDto(tagNo, "LOC-POL-1"));
        Assert.Equal(HttpStatusCode.Unauthorized, anonResp.StatusCode);

        // Operator should be allowed (StockOps)
        SetAuthToken("Operator", branchId: 1);
        var opResp = await _client.PostAsJsonAsync("/api/stock/inward", new InwardScanRequestDto(tagNo, "LOC-POL-1"));
        Assert.Equal(HttpStatusCode.OK, opResp.StatusCode);
    }

    [Fact]
    public async Task EndToEnd_Inward_Outward_Accept_Flow()
    {
        SetAuthToken("StoreKeeper", branchId: 1);
        var tagNo = $"TAG-E2E-{Guid.NewGuid():N}"[..12];
        var locCode = "LOC-E2E-1";
        await SeedInwardItemAsync(tagNo, locCode, 100, materialCode: "ALUM-100");

        // 1. Inward Putaway
        var inwardResp = await _client.PostAsJsonAsync("/api/stock/inward", new InwardScanRequestDto(tagNo, locCode));
        Assert.Equal(HttpStatusCode.OK, inwardResp.StatusCode);
        var inwardDto = await inwardResp.Content.ReadFromJsonAsync<InwardResponseDto>();
        Assert.NotNull(inwardDto);
        Assert.Equal(tagNo, inwardDto.TagNo);
        Assert.Equal(100, inwardDto.Qty);

        // 2. Outward FIFO issue: 60 units (partial issue)
        var outwardResp = await _client.PostAsJsonAsync("/api/stock/outward", new OutwardIssueRequestDto(tagNo, 60));
        Assert.Equal(HttpStatusCode.OK, outwardResp.StatusCode);
        var outwardDto = await outwardResp.Content.ReadFromJsonAsync<OutwardResponseDto>();
        Assert.NotNull(outwardDto);
        Assert.Equal(60, outwardDto.IssuedQty);
        Assert.Equal(40, outwardDto.RemainingQty);

        // 3. User Accept
        var acceptResp = await _client.PostAsJsonAsync("/api/stock/accept", new AcceptRequestDto(tagNo));
        Assert.Equal(HttpStatusCode.OK, acceptResp.StatusCode);
        var acceptDto = await acceptResp.Content.ReadFromJsonAsync<AcceptRejectResponseDto>();
        Assert.NotNull(acceptDto);
        Assert.Equal(TagStatus.InStock.ToString(), acceptDto.NewStatus); // Remaining 40 returns to InStock
        Assert.Equal(40, acceptDto.RemainingQty);

        // 4. Issue the remaining 40
        var outwardResp2 = await _client.PostAsJsonAsync("/api/stock/outward", new OutwardIssueRequestDto(tagNo, 40));
        Assert.Equal(HttpStatusCode.OK, outwardResp2.StatusCode);
        var outwardDto2 = await outwardResp2.Content.ReadFromJsonAsync<OutwardResponseDto>();
        Assert.Equal(0, outwardDto2!.RemainingQty);

        // 5. User Accept on full consumption -> status Accepted
        var acceptResp2 = await _client.PostAsJsonAsync("/api/stock/accept", new AcceptRequestDto(tagNo));
        Assert.Equal(HttpStatusCode.OK, acceptResp2.StatusCode);
        var acceptDto2 = await acceptResp2.Content.ReadFromJsonAsync<AcceptRejectResponseDto>();
        Assert.Equal(TagStatus.Accepted.ToString(), acceptDto2!.NewStatus);
        Assert.Equal(0, acceptDto2.RemainingQty);
    }

    [Fact]
    public async Task EndToEnd_Outward_Reject_WithMandatoryReason()
    {
        SetAuthToken("Operator", branchId: 1);
        var tagNo = $"TAG-REJ-{Guid.NewGuid():N}"[..12];
        var locCode = "LOC-REJ-1";
        await SeedInwardItemAsync(tagNo, locCode, 50, materialCode: "COPPER-200");

        // Inward and Outward
        await _client.PostAsJsonAsync("/api/stock/inward", new InwardScanRequestDto(tagNo, locCode));
        await _client.PostAsJsonAsync("/api/stock/outward", new OutwardIssueRequestDto(tagNo, 50));

        // Reject without reason -> 422 Unprocessable Entity
        var blankReject = await _client.PostAsJsonAsync("/api/stock/reject", new RejectRequestDto(tagNo, ""));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, blankReject.StatusCode);

        // Reject with valid reason -> 200 OK
        var validReject = await _client.PostAsJsonAsync("/api/stock/reject", new RejectRequestDto(tagNo, "Material scratched and bent"));
        Assert.Equal(HttpStatusCode.OK, validReject.StatusCode);
        var rejectDto = await validReject.Content.ReadFromJsonAsync<AcceptRejectResponseDto>();
        Assert.Equal(TagStatus.Rejected.ToString(), rejectDto!.NewStatus);
        Assert.Equal("Material scratched and bent", rejectDto.Reason);
    }

    [Fact]
    public async Task Outward_BypassFifoFlag_EnforcesOrBypassesFifoValidation()
    {
        SetAuthToken("StoreKeeper", branchId: 1);
        var material = $"FIFO-MAT-{Guid.NewGuid():N}"[..12];
        var oldTag = $"TAG-OLD-{Guid.NewGuid():N}"[..12];
        var newTag = $"TAG-NEW-{Guid.NewGuid():N}"[..12];

        await SeedInwardItemAsync(oldTag, "LOC-FIFO-1", 50, materialCode: material);
        await SeedInwardItemAsync(newTag, "LOC-FIFO-2", 50, materialCode: material);

        // Inward old tag first
        var in1 = await _client.PostAsJsonAsync("/api/stock/inward", new InwardScanRequestDto(oldTag, "LOC-FIFO-1"));
        Assert.Equal(HttpStatusCode.OK, in1.StatusCode);

        await Task.Delay(20);

        // Inward new tag second
        var in2 = await _client.PostAsJsonAsync("/api/stock/inward", new InwardScanRequestDto(newTag, "LOC-FIFO-2"));
        Assert.Equal(HttpStatusCode.OK, in2.StatusCode);

        // 1. Attempt to issue NEW tag with BypassFifo = false -> 422 Unprocessable Entity
        var fifoBlockedResp = await _client.PostAsJsonAsync(
            "/api/stock/outward",
            new OutwardIssueRequestDto(newTag, 20, BypassFifo: false));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, fifoBlockedResp.StatusCode);

        // 2. Issue NEW tag with BypassFifo = true -> 200 OK
        var bypassResp = await _client.PostAsJsonAsync(
            "/api/stock/outward",
            new OutwardIssueRequestDto(newTag, 20, BypassFifo: true));
        Assert.Equal(HttpStatusCode.OK, bypassResp.StatusCode);
        var outwardDto = await bypassResp.Content.ReadFromJsonAsync<OutwardResponseDto>();
        Assert.NotNull(outwardDto);
        Assert.Equal(20, outwardDto.IssuedQty);
        Assert.Equal(30, outwardDto.RemainingQty);
    }

    [Fact]
    public async Task Transfer_ValidRequest_ReturnsOkAndUpdatesBin()
    {
        SetAuthToken("StoreKeeper", branchId: 1);
        var tagNo = $"TAG-TR-{Guid.NewGuid():N}"[..12];
        var fromLoc = "LOC-TR-FROM";
        var toLoc = "LOC-TR-TO";

        await SeedInwardItemAsync(tagNo, fromLoc, 75, materialCode: $"TR-MAT-{Guid.NewGuid():N}"[..12]);

        // Seed destination location directly
        using (var scope = _factory.Services.CreateSystemScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            if (!ctx.Locations.Any(l => l.Code == toLoc && l.BranchId == 1))
            {
                ctx.Locations.Add(new Location
                {
                    BranchId = 1,
                    Code = toLoc,
                    Capacity = 500,
                    IsActive = true
                });
                await ctx.SaveChangesAsync();
            }
        }

        // Inward putaway
        var inResp = await _client.PostAsJsonAsync("/api/stock/inward", new InwardScanRequestDto(tagNo, fromLoc));
        Assert.Equal(HttpStatusCode.OK, inResp.StatusCode);

        // Transfer to destination
        var trResp = await _client.PostAsJsonAsync(
            "/api/stock/transfer",
            new TransferRequestDto(tagNo, toLoc));
        Assert.Equal(HttpStatusCode.OK, trResp.StatusCode);

        var trDto = await trResp.Content.ReadFromJsonAsync<TransferResponseDto>();
        Assert.NotNull(trDto);
        Assert.Equal(tagNo, trDto.TagNo);
        Assert.Equal(fromLoc, trDto.FromLocationCode);
        Assert.Equal(toLoc, trDto.ToLocationCode);
        Assert.Equal(75, trDto.Qty);

        // Verify via scan lookup that location is updated
        var scanResp = await _client.GetAsync($"/api/stock/scan/{tagNo}");
        Assert.Equal(HttpStatusCode.OK, scanResp.StatusCode);
        var scanDto = await scanResp.Content.ReadFromJsonAsync<TagStockInfoDto>();
        Assert.NotNull(scanDto);
        Assert.Equal(toLoc, scanDto.LocationCode);
        Assert.Equal(75, scanDto.Qty);
    }

    [Fact]
    public async Task ScanTag_AfterPartialOutward_ReturnsRemainingQtyForBothQtyFields()
    {
        SetAuthToken("StoreKeeper", branchId: 1);
        var tagNo = $"TAG-DYN-{Guid.NewGuid():N}"[..12];
        var loc = "LOC-DYN-1";

        await SeedInwardItemAsync(tagNo, loc, 100, materialCode: $"DYN-MAT-{Guid.NewGuid():N}"[..12]);

        // Inward 100
        await _client.PostAsJsonAsync("/api/stock/inward", new InwardScanRequestDto(tagNo, loc));

        // Outward 45 (remaining is 55)
        var outResp = await _client.PostAsJsonAsync(
            "/api/stock/outward",
            new OutwardRequestDto(tagNo, 45));
        Assert.Equal(HttpStatusCode.OK, outResp.StatusCode);

        // Scan lookup
        var scanResp = await _client.GetAsync($"/api/stock/scan/{tagNo}");
        Assert.Equal(HttpStatusCode.OK, scanResp.StatusCode);
        var scanDto = await scanResp.Content.ReadFromJsonAsync<TagStockInfoDto>();
        Assert.NotNull(scanDto);

        // Qty should return the remaining dynamic balance 55 (not initial 100)
        Assert.Equal(55, scanDto.Qty);
        Assert.Equal(55, scanDto.RemainingQty);
    }
}
