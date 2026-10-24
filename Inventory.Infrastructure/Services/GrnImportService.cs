using System.Globalization;
using System.Text.Json;
using ClosedXML.Excel;
using Inventory.Application.Common.Interfaces;
using Inventory.Application.Grn.DTOs;
using Inventory.Application.Grn.Services;
using Inventory.Domain.Entities;
using Inventory.Domain.Enums;
using Inventory.Domain.Exceptions;
using Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Inventory.Infrastructure.Services;

public class GrnImportService : IGrnImportService
{
    private readonly InventoryDbContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;

    private static readonly byte[] ZipSignature = [0x50, 0x4B, 0x03, 0x04];

    public GrnImportService(
        InventoryDbContext context,
        ICurrentUser currentUser,
        IClock clock)
    {
        _context = context;
        _currentUser = currentUser;
        _clock = clock;
    }

    public Task<byte[]> GenerateTemplateAsync(CancellationToken ct = default)
    {
        using var workbook = new XLWorkbook();

        // Sheet 1: GRN
        var wsData = workbook.Worksheets.Add("GRN");
        wsData.Cell(1, 1).Value = "GrnNo";
        wsData.Cell(1, 2).Value = "PartCode";
        wsData.Cell(1, 3).Value = "Qty";
        wsData.Cell(1, 4).Value = "Uom";
        wsData.Cell(1, 5).Value = "PartName";
        wsData.Cell(1, 6).Value = "Description";
        wsData.Cell(1, 7).Value = "GrnDate";
        wsData.Cell(1, 8).Value = "InvoiceNo";
        wsData.Cell(1, 9).Value = "InvoiceDate";
        wsData.Cell(1, 10).Value = "Plant";
        wsData.Cell(1, 11).Value = "StorageLocation";
        wsData.Cell(1, 12).Value = "BatchNo";
        wsData.Cell(1, 13).Value = "PoNo";
        wsData.Cell(1, 14).Value = "PoItem";
        wsData.Cell(1, 15).Value = "VendorCode";
        wsData.Cell(1, 16).Value = "VendorName";

        var headerRow = wsData.Row(1);
        headerRow.Style.Font.Bold = true;
        headerRow.Style.Fill.BackgroundColor = XLColor.LightGray;

        // Example row
        wsData.Cell(2, 1).Value = "GRN-50001001";
        wsData.Cell(2, 2).Value = "MAT-001";
        wsData.Cell(2, 3).Value = 100;
        wsData.Cell(2, 4).Value = "PCS";
        wsData.Cell(2, 5).Value = "Motor Rotor";
        wsData.Cell(2, 6).Value = "50HP Motor Rotor";
        wsData.Cell(2, 7).Value = "2026-10-21";
        wsData.Cell(2, 8).Value = "INV-1001";
        wsData.Cell(2, 9).Value = "2026-10-20";
        wsData.Cell(2, 10).Value = "1000";
        wsData.Cell(2, 11).Value = "SL01";
        wsData.Cell(2, 12).Value = "BATCH-A";
        wsData.Cell(2, 13).Value = "PO-9001";
        wsData.Cell(2, 14).Value = "10";
        wsData.Cell(2, 15).Value = "V100";
        wsData.Cell(2, 16).Value = "Apex Industries";

        // Text format for GrnNo and PartCode
        wsData.Column(1).Style.NumberFormat.Format = "@";
        wsData.Column(2).Style.NumberFormat.Format = "@";
        wsData.Columns().AdjustToContents();

        // Sheet 2: Instructions
        var wsInst = workbook.Worksheets.Add("Instructions");
        wsInst.Cell(1, 1).Value = "Column";
        wsInst.Cell(1, 2).Value = "Required";
        wsInst.Cell(1, 3).Value = "Description & Validation Rules";
        wsInst.Row(1).Style.Font.Bold = true;
        wsInst.Row(1).Style.Fill.BackgroundColor = XLColor.LightGray;

        wsInst.Cell(2, 1).Value = "GrnNo";
        wsInst.Cell(2, 2).Value = "Yes";
        wsInst.Cell(2, 3).Value = "Goods Receipt Note identifier (max 30 chars).";

        wsInst.Cell(3, 1).Value = "PartCode";
        wsInst.Cell(3, 2).Value = "Yes";
        wsInst.Cell(3, 3).Value = "Material code. Trimmed and uppercased. Must exist in Material Master.";

        wsInst.Cell(4, 1).Value = "Qty";
        wsInst.Cell(4, 2).Value = "No";
        wsInst.Cell(4, 3).Value = "Received quantity (positive numeric). Whole numbers required for tag generation.";

        wsInst.Cell(5, 1).Value = "Uom";
        wsInst.Cell(5, 2).Value = "No";
        wsInst.Cell(5, 3).Value = "Unit of Measure (e.g. PCS, KG). Defaults to Material Master UOM if empty.";

        wsInst.Cell(6, 1).Value = "PartName";
        wsInst.Cell(6, 2).Value = "No";
        wsInst.Cell(6, 3).Value = "Item name. Defaults to Material Master name if empty.";

        wsInst.Cell(7, 1).Value = "General";
        wsInst.Cell(7, 2).Value = "-";
        wsInst.Cell(7, 3).Value = "Maximum 5000 data rows. Blank rows are skipped. Records are landed in GrnApiResponses scoped to the current user's branch.";

        wsInst.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return Task.FromResult(ms.ToArray());
    }

    public async Task<GrnImportResultDto> ImportAsync(
        Stream stream,
        string fileName,
        string? contentType,
        bool dryRun,
        bool skipInvalidRows,
        CancellationToken ct = default)
    {
        if (stream == null || stream.Length == 0)
            throw new DomainException("Please select an Excel file to upload.");

        var ext = Path.GetExtension(fileName);
        if (!string.Equals(ext, ".xlsx", StringComparison.OrdinalIgnoreCase))
            throw new DomainException("The uploaded file must have a .xlsx extension.");

        if (!string.IsNullOrWhiteSpace(contentType))
        {
            var isExcelContentType =
                contentType.Equals("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", StringComparison.OrdinalIgnoreCase) ||
                contentType.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase) ||
                contentType.Equals("application/x-zip-compressed", StringComparison.OrdinalIgnoreCase) ||
                contentType.Equals("application/zip", StringComparison.OrdinalIgnoreCase);

            if (!isExcelContentType)
                throw new DomainException($"Invalid content type '{contentType}'. Expected an Excel (.xlsx) file.");
        }

        if (stream.Length > 2 * 1024 * 1024)
            throw new DomainException("The uploaded file exceeds the 2 MB size limit.");

        // Validate zip signature (magic bytes: PK\x03\x04)
        var buffer = new byte[4];
        var originalPos = stream.CanSeek ? stream.Position : 0;
        if (stream.CanSeek)
            stream.Position = 0;

        var bytesRead = stream.Read(buffer, 0, 4);
        if (stream.CanSeek)
            stream.Position = originalPos;

        if (bytesRead < 4 || !buffer.SequenceEqual(ZipSignature))
            throw new DomainException("The uploaded file is not a valid Excel (.xlsx) file.");

        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(stream);
        }
        catch (Exception ex) when (ex is not DomainException)
        {
            throw new DomainException("The Excel file is corrupt or unreadable.");
        }

        var rawRows = new List<ParsedGrnRow>();

        using (workbook)
        {
            if (workbook.Worksheets.Count == 0)
            throw new DomainException("The Excel file contains no worksheets.");

        var ws = workbook.Worksheet(1);
        var firstRow = ws.FirstRowUsed();
        if (firstRow == null)
            throw new DomainException("The Excel file is empty.");

        // Read and map headers
        var headerMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var cell in firstRow.CellsUsed())
        {
            var headerName = ExcelImportHelper.GetCellText(cell);
            if (!string.IsNullOrWhiteSpace(headerName))
            {
                headerMap[headerName] = cell.Address.ColumnNumber;
            }
        }

        // Required headers
        if (!TryFindColumn(headerMap, ["GrnNo", "GRN No", "GRNNo", "GRN_NO", "Grn Number", "GRN"], out var grnNoCol))
            throw new DomainException("Required column header 'GrnNo' is missing from the Excel file.");

        if (!TryFindColumn(headerMap, ["PartCode", "Part Code", "MaterialCode", "Material Code", "Item Code", "Part No"], out var partCodeCol))
            throw new DomainException("Required column header 'PartCode' is missing from the Excel file.");

        // Optional headers
        TryFindColumn(headerMap, ["Qty", "Quantity"], out var qtyCol);
        TryFindColumn(headerMap, ["Uom", "UoM", "Unit"], out var uomCol);
        TryFindColumn(headerMap, ["PartName", "Part Name", "Material Name"], out var partNameCol);
        TryFindColumn(headerMap, ["Description", "Desc"], out var descCol);
        TryFindColumn(headerMap, ["GrnDate", "GRN Date", "Date"], out var grnDateCol);
        TryFindColumn(headerMap, ["InvoiceNo", "Invoice No", "Invoice"], out var invoiceNoCol);
        TryFindColumn(headerMap, ["InvoiceDate", "Invoice Date"], out var invoiceDateCol);
        TryFindColumn(headerMap, ["Plant"], out var plantCol);
        TryFindColumn(headerMap, ["StorageLocation", "Storage Location", "SLoc"], out var storageLocationCol);
        TryFindColumn(headerMap, ["BatchNo", "Batch No", "Batch"], out var batchNoCol);
        TryFindColumn(headerMap, ["PoNo", "PO No", "Po Number", "PO"], out var poNoCol);
        TryFindColumn(headerMap, ["PoItem", "PO Item"], out var poItemCol);
        TryFindColumn(headerMap, ["VendorCode", "Vendor Code"], out var vendorCodeCol);
        TryFindColumn(headerMap, ["VendorName", "Vendor Name"], out var vendorNameCol);

        var lastRowNumber = ws.LastRowUsed()?.RowNumber() ?? firstRow.RowNumber();
        var startRow = firstRow.RowNumber() + 1;

        var totalDataRows = 0;

        for (var rowNum = startRow; rowNum <= lastRowNumber; rowNum++)
        {
            var row = ws.Row(rowNum);

            var rawGrnNo = ExcelImportHelper.GetCellText(row.Cell(grnNoCol));
            var rawPartCode = ExcelImportHelper.GetCellText(row.Cell(partCodeCol));
            var rawQty = qtyCol > 0 ? ExcelImportHelper.GetCellText(row.Cell(qtyCol)) : string.Empty;
            var rawUom = uomCol > 0 ? ExcelImportHelper.GetCellText(row.Cell(uomCol)) : string.Empty;
            var rawPartName = partNameCol > 0 ? ExcelImportHelper.GetCellText(row.Cell(partNameCol)) : string.Empty;
            var rawDesc = descCol > 0 ? ExcelImportHelper.GetCellText(row.Cell(descCol)) : string.Empty;
            var rawPlant = plantCol > 0 ? ExcelImportHelper.GetCellText(row.Cell(plantCol)) : string.Empty;
            var rawStorageLoc = storageLocationCol > 0 ? ExcelImportHelper.GetCellText(row.Cell(storageLocationCol)) : string.Empty;
            var rawBatchNo = batchNoCol > 0 ? ExcelImportHelper.GetCellText(row.Cell(batchNoCol)) : string.Empty;
            var rawPoNo = poNoCol > 0 ? ExcelImportHelper.GetCellText(row.Cell(poNoCol)) : string.Empty;
            var rawPoItem = poItemCol > 0 ? ExcelImportHelper.GetCellText(row.Cell(poItemCol)) : string.Empty;
            var rawVendorCode = vendorCodeCol > 0 ? ExcelImportHelper.GetCellText(row.Cell(vendorCodeCol)) : string.Empty;
            var rawVendorName = vendorNameCol > 0 ? ExcelImportHelper.GetCellText(row.Cell(vendorNameCol)) : string.Empty;

            // Skip entirely blank rows
            if (string.IsNullOrWhiteSpace(rawGrnNo) &&
                string.IsNullOrWhiteSpace(rawPartCode) &&
                string.IsNullOrWhiteSpace(rawQty) &&
                string.IsNullOrWhiteSpace(rawUom) &&
                string.IsNullOrWhiteSpace(rawPartName) &&
                string.IsNullOrWhiteSpace(rawDesc) &&
                string.IsNullOrWhiteSpace(rawPlant) &&
                string.IsNullOrWhiteSpace(rawStorageLoc) &&
                string.IsNullOrWhiteSpace(rawBatchNo) &&
                string.IsNullOrWhiteSpace(rawPoNo) &&
                string.IsNullOrWhiteSpace(rawPoItem) &&
                string.IsNullOrWhiteSpace(rawVendorCode) &&
                string.IsNullOrWhiteSpace(rawVendorName))
            {
                continue;
            }

            totalDataRows++;
            if (totalDataRows > 5000)
                throw new DomainException("Excel file exceeds the maximum limit of 5000 data rows.");

            var grnNo = rawGrnNo.Trim();
            var partCode = rawPartCode.Trim().ToUpperInvariant();
            var qty = qtyCol > 0 ? TryParseDecimal(row.Cell(qtyCol)) : null;
            var grnDate = grnDateCol > 0 ? TryParseDateTime(row.Cell(grnDateCol)) : null;
            var invoiceNo = invoiceNoCol > 0 ? ExcelImportHelper.GetCellText(row.Cell(invoiceNoCol)).Trim() : null;
            var invoiceDate = invoiceDateCol > 0 ? TryParseDateOnly(row.Cell(invoiceDateCol)) : null;

            string? initialError = null;
            if (string.IsNullOrWhiteSpace(grnNo))
            {
                initialError = "GRN number is required.";
            }
            else if (string.IsNullOrWhiteSpace(partCode))
            {
                initialError = "Part code is required.";
            }

            var rowDict = new Dictionary<string, object?>
            {
                ["GrnNo"] = grnNo,
                ["PartCode"] = partCode,
                ["PartName"] = string.IsNullOrWhiteSpace(rawPartName) ? null : rawPartName.Trim(),
                ["Qty"] = qty,
                ["Uom"] = string.IsNullOrWhiteSpace(rawUom) ? null : rawUom.Trim(),
                ["Description"] = string.IsNullOrWhiteSpace(rawDesc) ? null : rawDesc.Trim(),
                ["GrnDate"] = grnDate,
                ["InvoiceNo"] = string.IsNullOrWhiteSpace(invoiceNo) ? null : invoiceNo,
                ["InvoiceDate"] = invoiceDate,
                ["Plant"] = string.IsNullOrWhiteSpace(rawPlant) ? null : rawPlant.Trim(),
                ["StorageLocation"] = string.IsNullOrWhiteSpace(rawStorageLoc) ? null : rawStorageLoc.Trim(),
                ["BatchNo"] = string.IsNullOrWhiteSpace(rawBatchNo) ? null : rawBatchNo.Trim(),
                ["PoNo"] = string.IsNullOrWhiteSpace(rawPoNo) ? null : rawPoNo.Trim(),
                ["PoItem"] = string.IsNullOrWhiteSpace(rawPoItem) ? null : rawPoItem.Trim(),
                ["VendorCode"] = string.IsNullOrWhiteSpace(rawVendorCode) ? null : rawVendorCode.Trim(),
                ["VendorName"] = string.IsNullOrWhiteSpace(rawVendorName) ? null : rawVendorName.Trim()
            };

            var rawJson = JsonSerializer.Serialize(rowDict);

            rawRows.Add(new ParsedGrnRow(
                rowNum,
                grnNo,
                partCode,
                string.IsNullOrWhiteSpace(rawPartName) ? null : rawPartName.Trim(),
                string.IsNullOrWhiteSpace(rawDesc) ? null : rawDesc.Trim(),
                qty,
                string.IsNullOrWhiteSpace(rawUom) ? null : rawUom.Trim(),
                grnDate,
                string.IsNullOrWhiteSpace(invoiceNo) ? null : invoiceNo,
                invoiceDate,
                string.IsNullOrWhiteSpace(rawPlant) ? null : rawPlant.Trim(),
                string.IsNullOrWhiteSpace(rawStorageLoc) ? null : rawStorageLoc.Trim(),
                string.IsNullOrWhiteSpace(rawBatchNo) ? null : rawBatchNo.Trim(),
                string.IsNullOrWhiteSpace(rawPoNo) ? null : rawPoNo.Trim(),
                string.IsNullOrWhiteSpace(rawPoItem) ? null : rawPoItem.Trim(),
                string.IsNullOrWhiteSpace(rawVendorCode) ? null : rawVendorCode.Trim(),
                string.IsNullOrWhiteSpace(rawVendorName) ? null : rawVendorName.Trim(),
                rawJson,
                initialError));
        }
        } // end of using (workbook)

        // Master Cross-Validation with Materials table (Materials are global master data)
        var candidateCodes = rawRows
            .Where(r => r.InitialError == null && !string.IsNullOrEmpty(r.PartCode))
            .Select(r => r.PartCode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var existingMaterials = await _context.Materials
            .AsNoTracking()
            .Where(m => candidateCodes.Contains(m.Code))
            .ToDictionaryAsync(m => m.Code, StringComparer.OrdinalIgnoreCase, ct);

        var evaluatedRows = new List<EvaluatedGrnRow>();
        foreach (var r in rawRows)
        {
            if (r.InitialError != null)
            {
                evaluatedRows.Add(new EvaluatedGrnRow(r, GrnImportRowStatus.Error, r.InitialError, null));
                continue;
            }

            if (!existingMaterials.TryGetValue(r.PartCode, out var matchedMaterial))
            {
                evaluatedRows.Add(new EvaluatedGrnRow(r, GrnImportRowStatus.Error, "Part code not found in Material Master", null));
            }
            else
            {
                evaluatedRows.Add(new EvaluatedGrnRow(r, GrnImportRowStatus.New, null, matchedMaterial));
            }
        }

        var total = evaluatedRows.Count;
        var errorCount = evaluatedRows.Count(r => r.Status == GrnImportRowStatus.Error);
        var newCount = evaluatedRows.Count(r => r.Status == GrnImportRowStatus.New);

        var summaryRows = evaluatedRows
            .Select(r => new GrnImportRowResult(r.Row.RowNumber, r.Row.GrnNo, r.Row.PartCode, r.Status, r.Message))
            .ToList();

        var result = new GrnImportResultDto(total, newCount, errorCount, summaryRows);

        // Dry Run returns preview without saving
        if (dryRun)
        {
            return result;
        }

        // If there are errors and skipInvalidRows is false, block import without writing to DB
        if (errorCount > 0 && !skipInvalidRows)
        {
            return result;
        }

        // Persist valid rows inside one transaction
        var validToPersist = evaluatedRows
            .Where(r => r.Status == GrnImportRowStatus.New)
            .ToList();

        if (validToPersist.Count > 0)
        {
            IDbContextTransaction? tx = null;
            if (_context.Database.IsRelational())
            {
                tx = await _context.Database.BeginTransactionAsync(ct);
            }

            try
            {
                var fetchBatchId = Guid.NewGuid();
                var fetchedAt = _clock.UtcNow;
                var fetchedBy = _currentUser.UserId;
                var branchId = _currentUser.BranchId;

                var entities = validToPersist.Select(r => new GrnApiResponse
                {
                    BranchId = branchId,
                    ApiCredentialId = null,
                    FetchBatchId = fetchBatchId,
                    FetchedAt = fetchedAt,
                    FetchedBy = fetchedBy,
                    GrnNo = r.Row.GrnNo,
                    MaterialCode = r.Row.PartCode,
                    PartName = !string.IsNullOrWhiteSpace(r.Row.PartName) ? r.Row.PartName : r.MatchedMaterial?.Name,
                    Description = !string.IsNullOrWhiteSpace(r.Row.Description) ? r.Row.Description : null,
                    Qty = r.Row.Qty,
                    Uom = !string.IsNullOrWhiteSpace(r.Row.Uom) ? r.Row.Uom : r.MatchedMaterial?.Uom,
                    GrnDate = r.Row.GrnDate,
                    InvoiceNo = r.Row.InvoiceNo,
                    InvoiceDate = r.Row.InvoiceDate,
                    Plant = r.Row.Plant,
                    StorageLocation = r.Row.StorageLocation,
                    BatchNo = r.Row.BatchNo,
                    PoNo = r.Row.PoNo,
                    PoItem = r.Row.PoItem,
                    VendorCode = r.Row.VendorCode,
                    VendorName = r.Row.VendorName,
                    SourceKey = $"{r.Row.GrnNo}-{r.Row.PartCode}",
                    RawJson = r.Row.RawJson,
                    Status = GrnResponseStatus.New
                }).ToList();

                _context.GrnApiResponses.AddRange(entities);
                await _context.SaveChangesAsync(ct);

                if (tx != null)
                {
                    await tx.CommitAsync(ct);
                }
            }
            catch
            {
                if (tx != null)
                {
                    await tx.RollbackAsync(ct);
                }
                throw;
            }
            finally
            {
                if (tx != null)
                {
                    await tx.DisposeAsync();
                }
            }
        }

        return result;
    }

    private static bool TryFindColumn(Dictionary<string, int> headerMap, string[] candidateNames, out int columnNumber)
    {
        foreach (var name in candidateNames)
        {
            if (headerMap.TryGetValue(name, out columnNumber))
            {
                return true;
            }
        }

        columnNumber = 0;
        return false;
    }

    private static DateTime? TryParseDateTime(IXLCell cell)
    {
        if (cell == null || cell.IsEmpty()) return null;

        if (cell.DataType == XLDataType.DateTime)
        {
            try
            {
                return cell.GetDateTime();
            }
            catch
            {
            }
        }

        var text = ExcelImportHelper.GetCellText(cell);
        if (string.IsNullOrWhiteSpace(text)) return null;

        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt) ||
            DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out dt))
        {
            return dt;
        }

        return null;
    }

    private static DateOnly? TryParseDateOnly(IXLCell cell)
    {
        var dt = TryParseDateTime(cell);
        return dt.HasValue ? DateOnly.FromDateTime(dt.Value) : null;
    }

    private static decimal? TryParseDecimal(IXLCell cell)
    {
        if (cell == null || cell.IsEmpty()) return null;

        if (cell.DataType == XLDataType.Number)
        {
            try
            {
                return (decimal)cell.GetDouble();
            }
            catch
            {
            }
        }

        var text = ExcelImportHelper.GetCellText(cell);
        if (string.IsNullOrWhiteSpace(text)) return null;

        if (decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ||
            decimal.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out d))
        {
            return d;
        }

        return null;
    }

    private sealed record ParsedGrnRow(
        int RowNumber,
        string GrnNo,
        string PartCode,
        string? PartName,
        string? Description,
        decimal? Qty,
        string? Uom,
        DateTime? GrnDate,
        string? InvoiceNo,
        DateOnly? InvoiceDate,
        string? Plant,
        string? StorageLocation,
        string? BatchNo,
        string? PoNo,
        string? PoItem,
        string? VendorCode,
        string? VendorName,
        string RawJson,
        string? InitialError);

    private sealed record EvaluatedGrnRow(
        ParsedGrnRow Row,
        GrnImportRowStatus Status,
        string? Message,
        Material? MatchedMaterial);
}
