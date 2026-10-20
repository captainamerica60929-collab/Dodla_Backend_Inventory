using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ClosedXML.Excel;
using Inventory.Application.Common.Models;
using Inventory.Application.Locations.DTOs;
using Inventory.Application.Materials.DTOs;
using Inventory.Domain.Entities;
using Inventory.Infrastructure.Persistence;
using Inventory.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Inventory.Application.Tests;

public class ExcelImportTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ExcelImportTests(TestWebApplicationFactory factory)
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

    private static byte[] CreateMaterialsXlsx(
        IEnumerable<(string Code, string Name, string Uom, string? Zone)> rows,
        Action<IXLWorksheet>? customSetup = null)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Materials");
        ws.Cell(1, 1).Value = "Code";
        ws.Cell(1, 2).Value = "Name";
        ws.Cell(1, 3).Value = "UoM";
        ws.Cell(1, 4).Value = "Zone";

        var r = 2;
        foreach (var item in rows)
        {
            ws.Cell(r, 1).Value = item.Code;
            ws.Cell(r, 2).Value = item.Name;
            ws.Cell(r, 3).Value = item.Uom;
            ws.Cell(r, 4).Value = item.Zone ?? string.Empty;
            r++;
        }

        customSetup?.Invoke(ws);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static byte[] CreateMaterialsXlsx(
        params (string Code, string Name, string Uom, string? Zone)[] rows)
        => CreateMaterialsXlsx((IEnumerable<(string Code, string Name, string Uom, string? Zone)>)rows, null);

    private static byte[] CreateLocationsXlsx(
        IEnumerable<(string Code, string Zone, object Capacity)> rows,
        Action<IXLWorksheet>? customSetup = null)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Locations");
        ws.Cell(1, 1).Value = "Code";
        ws.Cell(1, 2).Value = "Zone";
        ws.Cell(1, 3).Value = "Capacity";

        var r = 2;
        foreach (var item in rows)
        {
            ws.Cell(r, 1).Value = item.Code;
            ws.Cell(r, 2).Value = item.Zone;
            if (item.Capacity is int intVal)
                ws.Cell(r, 3).Value = intVal;
            else if (item.Capacity is double dblVal)
                ws.Cell(r, 3).Value = dblVal;
            else
                ws.Cell(r, 3).Value = item.Capacity?.ToString() ?? string.Empty;
            r++;
        }

        customSetup?.Invoke(ws);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static byte[] CreateLocationsXlsx(
        params (string Code, string Zone, object Capacity)[] rows)
        => CreateLocationsXlsx((IEnumerable<(string Code, string Zone, object Capacity)>)rows, null);

    private static MultipartFormDataContent CreateMultipartContent(
        byte[] fileBytes,
        string fileName,
        ImportMode mode = ImportMode.CreateOnly,
        bool dryRun = false,
        bool skipInvalidRows = false)
    {
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(fileContent, "file", fileName);
        content.Add(new StringContent(mode.ToString()), "mode");
        content.Add(new StringContent(dryRun.ToString()), "dryRun");
        content.Add(new StringContent(skipInvalidRows.ToString()), "skipInvalidRows");
        return content;
    }

    [Fact]
    public async Task GetImportTemplate_Materials_ReturnsValidTwoSheetXlsx()
    {
        SetAuthToken("Admin");

        var response = await _client.GetAsync("/api/materials/import-template");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", response.Content.Headers.ContentType?.MediaType);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        using var ms = new MemoryStream(bytes);
        using var wb = new XLWorkbook(ms);

        Assert.Equal(2, wb.Worksheets.Count);
        Assert.Equal("Materials", wb.Worksheet(1).Name);
        Assert.Equal("Instructions", wb.Worksheet(2).Name);

        var wsData = wb.Worksheet("Materials");
        Assert.Equal("Code", wsData.Cell(1, 1).GetString());
        Assert.Equal("Name", wsData.Cell(1, 2).GetString());
        Assert.Equal("UoM", wsData.Cell(1, 3).GetString());
        Assert.Equal("@", wsData.Column(1).Style.NumberFormat.Format);
    }

    [Fact]
    public async Task GetImportTemplate_Locations_ReturnsValidTwoSheetXlsx()
    {
        SetAuthToken("Admin");

        var response = await _client.GetAsync("/api/locations/import-template");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        using var ms = new MemoryStream(bytes);
        using var wb = new XLWorkbook(ms);

        Assert.Equal(2, wb.Worksheets.Count);
        Assert.Equal("Locations", wb.Worksheet(1).Name);
        Assert.Equal("Instructions", wb.Worksheet(2).Name);

        var wsData = wb.Worksheet("Locations");
        Assert.Equal("Code", wsData.Cell(1, 1).GetString());
        Assert.Equal("Zone", wsData.Cell(1, 2).GetString());
        Assert.Equal("Capacity", wsData.Cell(1, 3).GetString());
        Assert.Equal("@", wsData.Column(1).Style.NumberFormat.Format);
    }

    [Fact]
    public async Task ValidFile_Materials_ImportsSuccessfully()
    {
        SetAuthToken("Admin");

        var code1 = "IMP-MAT-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var code2 = "IMP-MAT-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

        var xlsx = CreateMaterialsXlsx(new (string, string, string, string?)[]
        {
            (code1, "Test Material 1", "PCS", "ZONE-A"),
            (code2, "Test Material 2", "KG", "ZONE-B")
        });

        var content = CreateMultipartContent(xlsx, "materials.xlsx");
        var response = await _client.PostAsync("/api/materials/import", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<ImportResultDto>();
        Assert.NotNull(result);
        Assert.Equal(2, result.Total);
        Assert.Equal(2, result.NewCount);
        Assert.Equal(2, result.Imported);
        Assert.Equal(0, result.ErrorCount);

        // Verify in database
        using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        Assert.NotNull(await db.Materials.FirstOrDefaultAsync(m => m.Code == code1));
        Assert.NotNull(await db.Materials.FirstOrDefaultAsync(m => m.Code == code2));
    }

    [Fact]
    public async Task DryRun_Materials_WritesNothingToDatabase()
    {
        SetAuthToken("Admin");

        var code = "DRY-MAT-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var xlsx = CreateMaterialsXlsx((code, "Dry Run Mat", "PCS", null));

        var content = CreateMultipartContent(xlsx, "materials.xlsx", dryRun: true);
        var response = await _client.PostAsync("/api/materials/import", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<ImportResultDto>();
        Assert.NotNull(result);
        Assert.Equal(1, result.Total);
        Assert.Equal(1, result.NewCount);
        Assert.Equal(0, result.Imported); // 0 written

        using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        Assert.Null(await db.Materials.FirstOrDefaultAsync(m => m.Code == code));
    }

    [Fact]
    public async Task WrongOrMissingHeaders_Returns422()
    {
        SetAuthToken("Admin");

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Sheet1");
        ws.Cell(1, 1).Value = "WrongCodeHeader";
        ws.Cell(1, 2).Value = "Name";
        ws.Cell(1, 3).Value = "UoM";
        ws.Cell(2, 1).Value = "MAT-01";
        ws.Cell(2, 2).Value = "Item";
        ws.Cell(2, 3).Value = "PCS";

        using var ms = new MemoryStream();
        wb.SaveAs(ms);

        var content = CreateMultipartContent(ms.ToArray(), "wrong_header.xlsx");
        var response = await _client.PostAsync("/api/materials/import", content);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Contains("Code", problem!.Detail);
    }

    [Fact]
    public async Task BlankRows_AreSkipped()
    {
        SetAuthToken("Admin");

        var code1 = "BLK-MAT-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var code2 = "BLK-MAT-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

        var xlsx = CreateMaterialsXlsx(
            (code1, "First Item", "PCS", null),
            ("", "", "", null),
            ("   ", "   ", "   ", null),
            (code2, "Second Item", "BOX", null));

        var content = CreateMultipartContent(xlsx, "blank_rows.xlsx");
        var response = await _client.PostAsync("/api/materials/import", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<ImportResultDto>();
        Assert.Equal(2, result!.Total); // Only 2 data rows counted
        Assert.Equal(2, result.Imported);
    }

    [Fact]
    public async Task DuplicateInFile_FlagsLaterRowsAsError()
    {
        SetAuthToken("Admin");

        var code = "DUP-FILE-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

        var xlsx = CreateMaterialsXlsx(
            (code, "First Occurrence", "PCS", null),
            (code, "Second Occurrence (Duplicate)", "PCS", null));

        var content = CreateMultipartContent(xlsx, "dup.xlsx", skipInvalidRows: true);
        var response = await _client.PostAsync("/api/materials/import", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<ImportResultDto>();
        Assert.Equal(2, result!.Total);
        Assert.Equal(1, result.Imported);
        Assert.Equal(1, result.ErrorCount);

        var errRow = result.Rows.First(r => r.Status == ImportRowStatus.Error);
        Assert.Equal(3, errRow.RowNumber);
        Assert.Contains("Duplicate code", errRow.Message);
    }

    [Fact]
    public async Task ExistingCodes_CreateOnlySkips_UpsertUpdates()
    {
        SetAuthToken("Admin");

        var code = "EXT-MAT-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

        // 1. First import creates it
        var xlsx1 = CreateMaterialsXlsx((code, "Initial Name", "PCS", "ZONE-1"));
        var res1 = await _client.PostAsync("/api/materials/import", CreateMultipartContent(xlsx1, "m1.xlsx", mode: ImportMode.CreateOnly));
        Assert.Equal(HttpStatusCode.OK, res1.StatusCode);

        // 2. Second import in CreateOnly mode skips it
        var xlsx2 = CreateMaterialsXlsx((code, "Changed Name", "KG", "ZONE-2"));
        var res2 = await _client.PostAsync("/api/materials/import", CreateMultipartContent(xlsx2, "m2.xlsx", mode: ImportMode.CreateOnly));
        Assert.Equal(HttpStatusCode.OK, res2.StatusCode);
        var result2 = await res2.Content.ReadFromJsonAsync<ImportResultDto>();
        Assert.Equal(1, result2!.Skipped);
        Assert.Equal(0, result2.Updated);

        // 3. Third import in Upsert mode updates it
        var res3 = await _client.PostAsync("/api/materials/import", CreateMultipartContent(xlsx2, "m3.xlsx", mode: ImportMode.Upsert));
        Assert.Equal(HttpStatusCode.OK, res3.StatusCode);
        var result3 = await res3.Content.ReadFromJsonAsync<ImportResultDto>();
        Assert.Equal(1, result3!.Updated);
        Assert.Equal(0, result3.Skipped);

        // Verify updated in database
        using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var mat = await db.Materials.FirstAsync(m => m.Code == code);
        Assert.Equal("Changed Name", mat.Name);
        Assert.Equal("KG", mat.Uom);
    }

    [Fact]
    public async Task Locations_CapacityParsing_NumericTextAndDecimal()
    {
        SetAuthToken("Admin");

        var codeNum = "CAP-NUM-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var codeTxt = "CAP-TXT-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var codeDec = "CAP-DEC-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var codeLet = "CAP-LET-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

        var xlsx = CreateLocationsXlsx(new (string, string, object)[]
        {
            (codeNum, "ZONE-A", 500),      // numeric 500 -> Valid
            (codeTxt, "ZONE-A", "750"),    // text "750"  -> Valid
            (codeDec, "ZONE-A", 2.5),      // decimal 2.5 -> Invalid
            (codeLet, "ZONE-A", "500pcs")  // letters     -> Invalid
        });

        var content = CreateMultipartContent(xlsx, "locations.xlsx", skipInvalidRows: true);
        var response = await _client.PostAsync("/api/locations/import", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<ImportResultDto>();
        Assert.Equal(4, result!.Total);
        Assert.Equal(2, result.Imported);
        Assert.Equal(2, result.ErrorCount);

        var decError = result.Rows.First(r => r.Code == codeDec);
        Assert.Equal(ImportRowStatus.Error, decError.Status);
        Assert.Contains("positive whole number", decError.Message);
    }

    [Fact]
    public async Task Over5000Rows_Returns422()
    {
        SetAuthToken("Admin");

        var rows = new List<(string, string, string, string?)>();
        for (var i = 1; i <= 5001; i++)
        {
            rows.Add(($"M{i:D5}", "Name", "PCS", null));
        }

        var xlsx = CreateMaterialsXlsx(rows);
        var content = CreateMultipartContent(xlsx, "large.xlsx");
        var response = await _client.PostAsync("/api/materials/import", content);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Contains("5000", problem!.Detail);
    }

    [Fact]
    public async Task Over2MB_ReturnsError()
    {
        SetAuthToken("Admin");

        // Create a 2.5 MB dummy byte array with zip header
        var largeBytes = new byte[2500000];
        largeBytes[0] = 0x50;
        largeBytes[1] = 0x4B;
        largeBytes[2] = 0x03;
        largeBytes[3] = 0x04;

        var content = CreateMultipartContent(largeBytes, "large_file.xlsx");
        var response = await _client.PostAsync("/api/materials/import", content);

        Assert.True(response.StatusCode == HttpStatusCode.UnprocessableEntity ||
                    response.StatusCode == HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task RenamedTxtFile_Returns422()
    {
        SetAuthToken("Admin");

        var txtBytes = System.Text.Encoding.UTF8.GetBytes("This is plain text pretending to be xlsx");
        var content = CreateMultipartContent(txtBytes, "fake.xlsx");
        var response = await _client.PostAsync("/api/materials/import", content);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Contains("not a valid Excel", problem!.Detail);
    }

    [Fact]
    public async Task CorruptZip_Returns422Not500()
    {
        SetAuthToken("Admin");

        // Has zip magic bytes PK\x03\x04 followed by corrupted/invalid stream
        var corruptBytes = new byte[] { 0x50, 0x4B, 0x03, 0x04, 0xFF, 0xFE, 0xDD, 0xCC, 0x00, 0x00, 0x00 };
        var content = CreateMultipartContent(corruptBytes, "corrupt.xlsx");
        var response = await _client.PostAsync("/api/materials/import", content);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Contains("corrupt or unreadable", problem!.Detail);
    }

    [Fact]
    public async Task FormulaCells_ReadAsTextWithoutEvaluating()
    {
        SetAuthToken("Admin");

        var code = "FORMULA-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var xlsx = CreateMaterialsXlsx(new (string, string, string, string?)[] { (code, "Base Name", "PCS", "A") }, ws =>
        {
            // Set a formula on the Name column of row 2
            ws.Cell(2, 2).FormulaA1 = "=\"Calculated Name\"";
        });

        var content = CreateMultipartContent(xlsx, "formula.xlsx");
        var response = await _client.PostAsync("/api/materials/import", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<ImportResultDto>();
        Assert.Equal(1, result!.Imported);
    }

    [Fact]
    public async Task BranchScoping_LocationsImportIntoTokenBranch_SameCodeInOtherBranchAllowed()
    {
        var code = "LOC-SHARED-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

        // Ensure Branch 2 exists
        using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            if (!db.Branches.Any(b => b.Id == 2))
            {
                db.Branches.Add(new Branch { Id = 2, Code = "BR2", Name = "Branch 2", IsActive = true });
                db.SaveChanges();
            }
        }

        // 1. Admin logs into Branch 1 and imports location
        SetAuthToken("Admin", branchId: 1);
        var xlsx1 = CreateLocationsXlsx(new (string, string, object)[] { (code, "ZONE-1", 100) });
        var res1 = await _client.PostAsync("/api/locations/import", CreateMultipartContent(xlsx1, "loc1.xlsx"));
        Assert.Equal(HttpStatusCode.OK, res1.StatusCode);

        // 2. Admin logs into Branch 2 and imports SAME code
        SetAuthToken("Admin", branchId: 2);
        var xlsx2 = CreateLocationsXlsx(new (string, string, object)[] { (code, "ZONE-2", 200) });
        var res2 = await _client.PostAsync("/api/locations/import", CreateMultipartContent(xlsx2, "loc2.xlsx"));
        Assert.Equal(HttpStatusCode.OK, res2.StatusCode);

        // Verify both exist with distinct BranchIds in the database
        using (var scope = _factory.Services.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            var locations = await db.Locations.Where(l => l.Code == code).ToListAsync();
            Assert.Equal(2, locations.Count);
            Assert.Contains(locations, l => l.BranchId == 1 && l.Capacity == 100);
            Assert.Contains(locations, l => l.BranchId == 2 && l.Capacity == 200);
        }
    }

    [Fact]
    public async Task PolicyMatrix_NonAdminRoles_Get403OnImport()
    {
        var xlsx = CreateMaterialsXlsx(("POL-MAT", "Test", "PCS", null));

        foreach (var role in new[] { "StoreKeeper", "Viewer", "Operator" })
        {
            SetAuthToken(role);
            var contentMat = CreateMultipartContent(xlsx, "mat.xlsx");
            var resMat = await _client.PostAsync("/api/materials/import", contentMat);
            Assert.Equal(HttpStatusCode.Forbidden, resMat.StatusCode);

            var contentLoc = CreateMultipartContent(xlsx, "loc.xlsx");
            var resLoc = await _client.PostAsync("/api/locations/import", contentLoc);
            Assert.Equal(HttpStatusCode.Forbidden, resLoc.StatusCode);

            var resTplMat = await _client.GetAsync("/api/materials/import-template");
            Assert.Equal(HttpStatusCode.Forbidden, resTplMat.StatusCode);

            var resTplLoc = await _client.GetAsync("/api/locations/import-template");
            Assert.Equal(HttpStatusCode.Forbidden, resTplLoc.StatusCode);
        }
    }

    [Fact]
    public async Task Rollback_WhenCommitFailsAndSkipInvalidRowsFalse_WritesNothing()
    {
        SetAuthToken("Admin");

        var validCode = "ROLL-VALID-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var invalidCode = new string('X', 35); // Invalid: > 30 characters

        var xlsx = CreateMaterialsXlsx(
            (validCode, "Valid Material", "PCS", null),
            (invalidCode, "Invalid Material", "PCS", null));

        var content = CreateMultipartContent(xlsx, "rollback.xlsx", skipInvalidRows: false);
        var response = await _client.PostAsync("/api/materials/import", content);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        // Verify validCode was NOT inserted
        using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        Assert.Null(await db.Materials.FirstOrDefaultAsync(m => m.Code == validCode));
    }

    [Fact]
    public async Task Idempotency_SecondRunInCreateOnlyMode_DoesNotCreateDuplicates()
    {
        SetAuthToken("Admin");

        var code = "IDEM-MAT-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var xlsx = CreateMaterialsXlsx((code, "Idempotent Item", "PCS", null));

        // Run 1
        var res1 = await _client.PostAsync("/api/materials/import", CreateMultipartContent(xlsx, "run1.xlsx", mode: ImportMode.CreateOnly));
        Assert.Equal(HttpStatusCode.OK, res1.StatusCode);
        var ret1 = await res1.Content.ReadFromJsonAsync<ImportResultDto>();
        Assert.Equal(1, ret1!.Imported);

        // Run 2
        var res2 = await _client.PostAsync("/api/materials/import", CreateMultipartContent(xlsx, "run2.xlsx", mode: ImportMode.CreateOnly));
        Assert.Equal(HttpStatusCode.OK, res2.StatusCode);
        var ret2 = await res2.Content.ReadFromJsonAsync<ImportResultDto>();
        Assert.Equal(0, ret2!.Imported);
        Assert.Equal(1, ret2.Skipped);

        // Verify only 1 row in database
        using var scope = _factory.Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        Assert.Equal(1, await db.Materials.CountAsync(m => m.Code == code));
    }
}
