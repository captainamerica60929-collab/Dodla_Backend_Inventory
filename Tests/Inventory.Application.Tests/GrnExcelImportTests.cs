using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using Inventory.Application.Grn.DTOs;
using Inventory.Domain.Entities;
using Inventory.Domain.Enums;
using Inventory.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Inventory.Application.Tests;

public class GrnExcelImportTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public GrnExcelImportTests(TestWebApplicationFactory factory)
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

    private static byte[] CreateGrnXlsx(
        IEnumerable<(string GrnNo, string PartCode, decimal? Qty, string? Uom, string? PartName, string? Plant)> rows,
        Action<IXLWorksheet>? customSetup = null)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("GRN");
        ws.Cell(1, 1).Value = "GrnNo";
        ws.Cell(1, 2).Value = "PartCode";
        ws.Cell(1, 3).Value = "Qty";
        ws.Cell(1, 4).Value = "Uom";
        ws.Cell(1, 5).Value = "PartName";
        ws.Cell(1, 6).Value = "Plant";

        var r = 2;
        foreach (var item in rows)
        {
            ws.Cell(r, 1).Value = item.GrnNo;
            ws.Cell(r, 2).Value = item.PartCode;
            if (item.Qty.HasValue)
                ws.Cell(r, 3).Value = (double)item.Qty.Value;
            ws.Cell(r, 4).Value = item.Uom ?? string.Empty;
            ws.Cell(r, 5).Value = item.PartName ?? string.Empty;
            ws.Cell(r, 6).Value = item.Plant ?? string.Empty;
            r++;
        }

        customSetup?.Invoke(ws);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static MultipartFormDataContent CreateMultipartContent(
        byte[] fileBytes,
        string fileName,
        string contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        bool dryRun = false,
        bool skipInvalidRows = false)
    {
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(fileContent, "file", fileName);
        content.Add(new StringContent(dryRun.ToString()), "dryRun");
        content.Add(new StringContent(skipInvalidRows.ToString()), "skipInvalidRows");
        return content;
    }

    private async Task EnsureMaterialExistsAsync(string code, string name = "Test Mat", string uom = "PCS")
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var existing = await context.Materials.IgnoreQueryFilters().FirstOrDefaultAsync(m => m.Code == code);
        if (existing == null)
        {
            context.Materials.Add(new Material
            {
                Code = code,
                Name = name,
                Uom = uom,
                IsActive = true
            });
            await context.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task ValidUpload_PersistsRowsWithBatchIdStatusNewRawJson()
    {
        SetAuthToken("StoreKeeper", branchId: 1);
        await EnsureMaterialExistsAsync("MAT-VALID-01", "Motor Rotor", "PCS");
        await EnsureMaterialExistsAsync("MAT-VALID-02", "Stator Frame", "PCS");

        var fileBytes = CreateGrnXlsx([
            ("GRN-101", "mat-valid-01", 50m, "PCS", "Motor Rotor", "1000"),
            ("GRN-102", "MAT-VALID-02", 75m, "PCS", "Stator Frame", "1000")
        ]);

        var content = CreateMultipartContent(fileBytes, "grn-valid.xlsx");
        var response = await _client.PostAsync("/api/grn/import", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<GrnImportResultDto>();

        Assert.NotNull(result);
        Assert.Equal(2, result.Total);
        Assert.Equal(2, result.NewCount);
        Assert.Equal(0, result.ErrorCount);
        Assert.Equal(2, result.Rows.Count);
        Assert.All(result.Rows, r => Assert.Equal(GrnImportRowStatus.New, r.Status));

        // Check persistence in database
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var rows = await context.GrnApiResponses
            .IgnoreQueryFilters()
            .Where(r => r.GrnNo == "GRN-101" || r.GrnNo == "GRN-102")
            .ToListAsync();

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r =>
        {
            Assert.Equal(1, r.BranchId);
            Assert.Null(r.ApiCredentialId);
            Assert.NotEqual(Guid.Empty, r.FetchBatchId);
            Assert.Equal(GrnResponseStatus.New, r.Status);
            Assert.False(string.IsNullOrWhiteSpace(r.RawJson));
        });

        var r1 = rows.First(r => r.GrnNo == "GRN-101");
        Assert.Equal("MAT-VALID-01", r1.MaterialCode);
        Assert.Equal(50m, r1.Qty);
    }

    [Fact]
    public async Task MissingMaterialCodes_MarkedAsError_BlocksImportWhenSkipInvalidFalse()
    {
        SetAuthToken("Admin", branchId: 1);
        await EnsureMaterialExistsAsync("MAT-EXIST-01", "Existing Material", "PCS");

        var uniqueMissingCode = "MAT-NONEXIST-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var fileBytes = CreateGrnXlsx([
            ("GRN-201", "MAT-EXIST-01", 10m, "PCS", "Existing", "1000"),
            ("GRN-202", uniqueMissingCode, 20m, "PCS", "Missing", "1000")
        ]);

        var content = CreateMultipartContent(fileBytes, "grn-missing.xlsx", skipInvalidRows: false);
        var response = await _client.PostAsync("/api/grn/import", content);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<GrnImportResultDto>();

        Assert.NotNull(result);
        Assert.Equal(2, result.Total);
        Assert.Equal(1, result.NewCount);
        Assert.Equal(1, result.ErrorCount);

        var errRow = result.Rows.First(r => r.GrnNo == "GRN-202");
        Assert.Equal(GrnImportRowStatus.Error, errRow.Status);
        Assert.Equal("Part code not found in Material Master", errRow.Message);

        // Verify that NO rows were saved to the database
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var dbRows = await context.GrnApiResponses
            .IgnoreQueryFilters()
            .Where(r => r.GrnNo == "GRN-201" || r.GrnNo == "GRN-202")
            .ToListAsync();

        Assert.Empty(dbRows);
    }

    [Fact]
    public async Task MissingMaterialCodes_SkipsInvalidRowsWhenSkipInvalidTrue()
    {
        SetAuthToken("Admin", branchId: 1);
        await EnsureMaterialExistsAsync("MAT-EXIST-02", "Existing Material 2", "PCS");

        var uniqueMissingCode = "MAT-NONEXIST-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var fileBytes = CreateGrnXlsx([
            ("GRN-301", "MAT-EXIST-02", 15m, "PCS", "Existing 2", "1000"),
            ("GRN-302", uniqueMissingCode, 25m, "PCS", "Missing 2", "1000")
        ]);

        var content = CreateMultipartContent(fileBytes, "grn-skip.xlsx", skipInvalidRows: true);
        var response = await _client.PostAsync("/api/grn/import", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<GrnImportResultDto>();

        Assert.NotNull(result);
        Assert.Equal(2, result.Total);
        Assert.Equal(1, result.NewCount);
        Assert.Equal(1, result.ErrorCount);

        // Verify valid row is persisted, invalid row is NOT persisted
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var dbRows = await context.GrnApiResponses
            .IgnoreQueryFilters()
            .Where(r => r.GrnNo == "GRN-301" || r.GrnNo == "GRN-302")
            .ToListAsync();

        Assert.Single(dbRows);
        Assert.Equal("GRN-301", dbRows[0].GrnNo);
        Assert.Equal("MAT-EXIST-02", dbRows[0].MaterialCode);
    }

    [Fact]
    public async Task InvalidExcelHeaders_MissingRequiredHeader_Returns422ProblemDetails()
    {
        SetAuthToken("Admin");

        // Create workbook with missing PartCode header
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("GRN");
        ws.Cell(1, 1).Value = "GrnNo";
        ws.Cell(1, 2).Value = "WrongHeader";
        ws.Cell(2, 1).Value = "GRN-401";
        ws.Cell(2, 2).Value = "MAT-01";

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        var fileBytes = ms.ToArray();

        var content = CreateMultipartContent(fileBytes, "bad-headers.xlsx");
        var response = await _client.PostAsync("/api/grn/import", content);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.NotNull(problem);
        Assert.Contains("PartCode", problem.Detail);
    }

    [Fact]
    public async Task BranchScoping_EnforcedOnImportedRecords()
    {
        // Upload as Branch 1
        SetAuthToken("StoreKeeper", branchId: 1);
        await EnsureMaterialExistsAsync("MAT-BRANCH-01", "Branch Mat", "PCS");

        var fileBytes = CreateGrnXlsx([
            ("GRN-BR-1", "MAT-BRANCH-01", 10m, "PCS", "Branch Mat", "1000")
        ]);

        var content = CreateMultipartContent(fileBytes, "branch-scope.xlsx");
        var response = await _client.PostAsync("/api/grn/import", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Verify in DB with branch scoping:
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var dbRow = await context.GrnApiResponses.IgnoreQueryFilters().FirstOrDefaultAsync(r => r.GrnNo == "GRN-BR-1");
        Assert.NotNull(dbRow);
        Assert.Equal(1, dbRow.BranchId);

        // Query through Branch 2 via GrnResponsesController
        SetAuthToken("StoreKeeper", branchId: 2);
        var queryResp = await _client.GetAsync("/api/grn-responses?grnNo=GRN-BR-1");
        Assert.Equal(HttpStatusCode.OK, queryResp.StatusCode);
        var paged = await queryResp.Content.ReadFromJsonAsync<JsonDocument>();
        var total = paged!.RootElement.GetProperty("total").GetInt32();
        Assert.Equal(0, total); // Branch 2 user cannot see Branch 1's records!
    }

    [Fact]
    public async Task DryRunMode_ReturnsPreviewWithoutSaving()
    {
        SetAuthToken("Admin", branchId: 1);
        await EnsureMaterialExistsAsync("MAT-DRY-01", "Dry Run Material", "PCS");

        var fileBytes = CreateGrnXlsx([
            ("GRN-DRY-1", "MAT-DRY-01", 100m, "PCS", "Dry Run", "1000")
        ]);

        var content = CreateMultipartContent(fileBytes, "dry-run.xlsx", dryRun: true);
        var response = await _client.PostAsync("/api/grn/import", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<GrnImportResultDto>();

        Assert.NotNull(result);
        Assert.Equal(1, result.Total);
        Assert.Equal(1, result.NewCount);
        Assert.Equal(0, result.ErrorCount);

        // Verify zero records in DB
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var exists = await context.GrnApiResponses.IgnoreQueryFilters().AnyAsync(r => r.GrnNo == "GRN-DRY-1");
        Assert.False(exists);
    }

    [Fact]
    public async Task CorruptedOrInvalidFile_Returns422ProblemDetails()
    {
        SetAuthToken("Admin");

        // 1. Wrong extension
        var contentWrongExt = CreateMultipartContent(new byte[] { 1, 2, 3 }, "data.csv");
        var resp1 = await _client.PostAsync("/api/grn/import", contentWrongExt);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp1.StatusCode);

        // 2. Corrupt bytes (not zip signature)
        var contentCorrupt = CreateMultipartContent(new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04 }, "corrupt.xlsx");
        var resp2 = await _client.PostAsync("/api/grn/import", contentCorrupt);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp2.StatusCode);

        // 3. Empty file
        var emptyContent = new MultipartFormDataContent();
        var emptyFile = new ByteArrayContent(Array.Empty<byte>());
        emptyFile.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        emptyContent.Add(emptyFile, "file", "empty.xlsx");
        var resp3 = await _client.PostAsync("/api/grn/import", emptyContent);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp3.StatusCode);
    }

    [Fact]
    public async Task Exceeding5000Rows_Returns422ProblemDetails()
    {
        SetAuthToken("Admin");

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("GRN");
        ws.Cell(1, 1).Value = "GrnNo";
        ws.Cell(1, 2).Value = "PartCode";

        for (var i = 2; i <= 5003; i++)
        {
            ws.Cell(i, 1).Value = "G" + i;
            ws.Cell(i, 2).Value = "P" + i;
        }

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        var fileBytes = ms.ToArray();

        var content = CreateMultipartContent(fileBytes, "large.xlsx");
        var response = await _client.PostAsync("/api/grn/import", content);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Contains("5000", problem.Detail);
    }

    [Fact]
    public async Task Authorization_GrnManagePolicy_Enforced()
    {
        var fileBytes = CreateGrnXlsx([("G-AUTH", "MAT-01", 10m, "PCS", "Mat", "1000")]);

        // Unauthenticated -> 401
        _client.DefaultRequestHeaders.Authorization = null;
        var unauthContent = CreateMultipartContent(fileBytes, "auth.xlsx");
        var unauthResp = await _client.PostAsync("/api/grn/import", unauthContent);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthResp.StatusCode);

        // Operator role -> 403 Forbidden
        SetAuthToken("Operator");
        var opContent = CreateMultipartContent(fileBytes, "auth.xlsx");
        var opResp = await _client.PostAsync("/api/grn/import", opContent);
        Assert.Equal(HttpStatusCode.Forbidden, opResp.StatusCode);

        // Viewer role -> 403 Forbidden
        SetAuthToken("Viewer");
        var viewerContent = CreateMultipartContent(fileBytes, "auth.xlsx");
        var viewerResp = await _client.PostAsync("/api/grn/import", viewerContent);
        Assert.Equal(HttpStatusCode.Forbidden, viewerResp.StatusCode);

        // StoreKeeper role -> Not 401/403
        SetAuthToken("StoreKeeper");
        var skContent = CreateMultipartContent(fileBytes, "auth.xlsx", dryRun: true);
        var skResp = await _client.PostAsync("/api/grn/import", skContent);
        Assert.NotEqual(HttpStatusCode.Unauthorized, skResp.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, skResp.StatusCode);
    }

    [Fact]
    public async Task GetImportTemplate_Grn_ReturnsValidTwoSheetXlsx()
    {
        SetAuthToken("Admin");

        var response = await _client.GetAsync("/api/grn/import-template");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", response.Content.Headers.ContentType?.MediaType);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 100);

        var templateDir = @"D:\Projects\angular project\Inventory\templates";
        Directory.CreateDirectory(templateDir);
        await File.WriteAllBytesAsync(Path.Combine(templateDir, "grn-template.xlsx"), bytes);

        // Verify OpenXml ZIP magic bytes (PK\x03\x04)
        Assert.Equal(0x50, bytes[0]);
        Assert.Equal(0x4B, bytes[1]);
        Assert.Equal(0x03, bytes[2]);
        Assert.Equal(0x04, bytes[3]);

        using var ms = new MemoryStream(bytes);
        using var wb = new XLWorkbook(ms);

        Assert.Equal(2, wb.Worksheets.Count);
        Assert.Equal("GRN", wb.Worksheet(1).Name);
        Assert.Equal("Instructions", wb.Worksheet(2).Name);

        var wsData = wb.Worksheet("GRN");
        Assert.Equal("GrnNo", wsData.Cell(1, 1).GetString());
        Assert.Equal("PartCode", wsData.Cell(1, 2).GetString());
        Assert.Equal("Qty", wsData.Cell(1, 3).GetString());
        Assert.Equal("@", wsData.Column(1).Style.NumberFormat.Format);

        // Verify StoreKeeper can also download the template
        SetAuthToken("StoreKeeper");
        var skResponse = await _client.GetAsync("/api/grn/import-template");
        Assert.Equal(HttpStatusCode.OK, skResponse.StatusCode);

        // Verify unauthorized & non-GrnManage roles are rejected
        _client.DefaultRequestHeaders.Authorization = null;
        var unauthResponse = await _client.GetAsync("/api/grn/import-template");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthResponse.StatusCode);

        SetAuthToken("Operator");
        var opResponse = await _client.GetAsync("/api/grn/import-template");
        Assert.Equal(HttpStatusCode.Forbidden, opResponse.StatusCode);
    }
}

