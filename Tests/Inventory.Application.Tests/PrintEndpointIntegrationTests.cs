using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Inventory.Application.Common.Models;
using Inventory.Application.Tags.DTOs;
using Inventory.Domain.Entities;
using Inventory.Domain.Enums;
using Inventory.Infrastructure.Persistence;
using Inventory.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.Application.Tests;

public class PrintEndpointIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public PrintEndpointIntegrationTests(TestWebApplicationFactory factory)
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
    public async Task PolicyMatrix_OperatorAndViewer_Get403OnPrintEndpoints()
    {
        SetAuthToken("Operator", branchId: 1);
        var opResponse = await _client.GetAsync("/api/print/grn-rows");
        Assert.Equal(HttpStatusCode.Forbidden, opResponse.StatusCode);

        SetAuthToken("Viewer", branchId: 1);
        var viewerResponse = await _client.GetAsync("/api/print/grn-rows");
        Assert.Equal(HttpStatusCode.Forbidden, viewerResponse.StatusCode);
    }

    [Fact]
    public async Task PolicyMatrix_AdminAndStoreKeeper_Get200OnPrintEndpoints()
    {
        SetAuthToken("StoreKeeper", branchId: 1);
        var skResponse = await _client.GetAsync("/api/print/grn-rows");
        Assert.Equal(HttpStatusCode.OK, skResponse.StatusCode);

        SetAuthToken("Admin", branchId: 1);
        var adminResponse = await _client.GetAsync("/api/print/grn-rows");
        Assert.Equal(HttpStatusCode.OK, adminResponse.StatusCode);
    }

    [Fact]
    public async Task CompletePrintFlow_Prepare_Print_Reprint_AndAuditLog()
    {
        SetAuthToken("StoreKeeper", branchId: 1);

        // 1. Seed a GRN row directly
        int grnRowId;
        var fixedDate = new DateTime(2026, 10, 15, 11, 20, 30);
        using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            var row = new GrnApiResponse
            {
                BranchId = 1,
                GrnNo = "GRN-INTEG-001",
                GrnDate = fixedDate,
                Qty = 100m,
                Plant = "1000",
                Status = GrnResponseStatus.New
            };
            db.GrnApiResponses.Add(row);
            await db.SaveChangesAsync();
            grnRowId = row.Id;
        }

        // Verify GET /api/print/grn-rows returns GrnDate formatted as yyyy-MM-ddTHH:mm:ss without Z
        var listResponse = await _client.GetAsync("/api/print/grn-rows?grnNo=GRN-INTEG-001");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var pagedRows = await listResponse.Content.ReadFromJsonAsync<PagedResult<GrnPrintRowDto>>();
        Assert.NotNull(pagedRows);
        Assert.Single(pagedRows.Items);
        Assert.Equal("2026-10-15T11:20:30", pagedRows.Items[0].GrnDate);
        Assert.False(pagedRows.Items[0].GrnDate!.EndsWith("Z", StringComparison.OrdinalIgnoreCase));

        // 2. Prepare tag (POST /api/print/grn-rows/{id}/tags)
        var prepareResponse = await _client.PostAsync($"/api/print/grn-rows/{grnRowId}/tags", null);
        Assert.Equal(HttpStatusCode.OK, prepareResponse.StatusCode);
        var tagDto = await prepareResponse.Content.ReadFromJsonAsync<TagDto>();
        Assert.NotNull(tagDto);
        Assert.Equal("MAIN-26-000001", tagDto.TagNo);
        Assert.Equal("2026-10-15T11:20:30", tagDto.GrnDate);
        Assert.False(tagDto.GrnDate!.EndsWith("Z", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(100, tagDto.Qty);
        Assert.Equal("Generated", tagDto.Status);
        Assert.Equal(0, tagDto.PrintCount);

        // 3. Idempotent check: calling prepare again returns the same tag
        var idempotentResponse = await _client.PostAsync($"/api/print/grn-rows/{grnRowId}/tags", null);
        Assert.Equal(HttpStatusCode.OK, idempotentResponse.StatusCode);
        var sameTagDto = await idempotentResponse.Content.ReadFromJsonAsync<TagDto>();
        Assert.Equal(tagDto.TagId, sameTagDto!.TagId);

        // 4. Mark printed with empty body (POST /api/print/tags/{id}/printed)
        var printResponse = await _client.PostAsync($"/api/print/tags/{tagDto.TagId}/printed", new StringContent(string.Empty));
        Assert.Equal(HttpStatusCode.OK, printResponse.StatusCode);
        var printedTag = await printResponse.Content.ReadFromJsonAsync<TagDto>();
        Assert.NotNull(printedTag);
        Assert.Equal("Printed", printedTag.Status);
        Assert.Equal(1, printedTag.PrintCount);

        // 5. Reprint without reason fails (POST /api/print/tags/{id}/reprint)
        var emptyReasonResponse = await _client.PostAsJsonAsync($"/api/print/tags/{tagDto.TagId}/reprint", new ReprintRequest(""));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, emptyReasonResponse.StatusCode);

        // 6. Reprint with reason succeeds
        var reprintResponse = await _client.PostAsJsonAsync($"/api/print/tags/{tagDto.TagId}/reprint", new ReprintRequest("Sticker peeled off"));
        Assert.Equal(HttpStatusCode.OK, reprintResponse.StatusCode);
        var reprintedTag = await reprintResponse.Content.ReadFromJsonAsync<TagDto>();
        Assert.Equal(2, reprintedTag!.PrintCount);

        // 7. Get print history logs (GET /api/print/tags/{id}/log)
        var logResponse = await _client.GetAsync($"/api/print/tags/{tagDto.TagId}/log");
        Assert.Equal(HttpStatusCode.OK, logResponse.StatusCode);
        var logs = await logResponse.Content.ReadFromJsonAsync<List<TagPrintLogDto>>();
        Assert.NotNull(logs);
        Assert.Equal(2, logs.Count);

        // Verify kinds and reasons
        Assert.Equal("Reprint", logs[0].Kind);
        Assert.Equal("Sticker peeled off", logs[0].Reason);
        Assert.Equal("Print", logs[1].Kind);
        Assert.Null(logs[1].Reason);
    }
}
