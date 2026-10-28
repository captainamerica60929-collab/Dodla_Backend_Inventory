using ClosedXML.Excel;
using Inventory.Application.Common.Models;
using Inventory.Application.Materials.DTOs;
using Inventory.Application.Materials.Services;
using Inventory.Domain.Entities;
using Inventory.Domain.Exceptions;
using Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Inventory.Infrastructure.Services;

public class MaterialService : IMaterialService
{
    private readonly InventoryDbContext _context;
    private readonly ILogger<MaterialService>? _logger;

    public MaterialService(InventoryDbContext context, ILogger<MaterialService>? logger = null)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<PagedResult<MaterialDto>> GetPagedAsync(MaterialFilterDto filter, CancellationToken ct = default)
    {
        var page = filter.Page < 1 ? 1 : filter.Page;
        var pageSize = filter.PageSize < 1 ? 20 : (filter.PageSize > 200 ? 200 : filter.PageSize);

        var query = _context.Materials.AsNoTracking();

        if (filter.ActiveOnly == true)
        {
            query = query.Where(m => m.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(filter.Zone))
        {
            var zone = filter.Zone.Trim();
            query = query.Where(m => m.Zone == zone);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim();
            query = query.Where(m => m.Code.Contains(search) || m.Name.Contains(search));
        }

        var totalCount = await query.CountAsync(ct);
        var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

        var items = await query
            .OrderBy(m => m.Code)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new MaterialDto(
                m.Id,
                m.Code,
                m.Name,
                m.Uom,
                m.Zone,
                m.IsActive,
                m.CreatedAt,
                m.CreatedBy,
                m.ModifiedAt,
                m.ModifiedBy))
            .ToListAsync(ct);

        return new PagedResult<MaterialDto>(items, totalCount, page, pageSize, totalPages);
    }

    public async Task<MaterialDto> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var material = await _context.Materials
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == id, ct);

        if (material == null)
            throw new NotFoundException(nameof(Material), id);

        return MapToDto(material);
    }

    public async Task<MaterialDto> CreateAsync(CreateMaterialDto dto, CancellationToken ct = default)
    {
        if (dto == null)
            throw new DomainException("Material data is required.");

        var normalizedCode = dto.Code?.Trim().ToUpperInvariant() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(normalizedCode))
            throw new DomainException("Material code is required.");

        if (normalizedCode.Length > 30)
            throw new DomainException("Material code cannot exceed 30 characters.");

        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new DomainException("Material name is required.");

        if (dto.Name.Trim().Length > 150)
            throw new DomainException("Material name cannot exceed 150 characters.");

        if (string.IsNullOrWhiteSpace(dto.Uom))
            throw new DomainException("Material UOM is required.");

        if (dto.Uom.Trim().Length > 10)
            throw new DomainException("Material UOM cannot exceed 10 characters.");

        if (!string.IsNullOrWhiteSpace(dto.Zone) && dto.Zone.Trim().Length > 20)
            throw new DomainException("Material zone cannot exceed 20 characters.");

        var exists = await _context.Materials
            .AnyAsync(m => m.Code == normalizedCode, ct);

        if (exists)
            throw new DomainException($"Material with code '{normalizedCode}' already exists.");

        var material = new Material
        {
            Code = normalizedCode,
            Name = dto.Name.Trim(),
            Uom = dto.Uom.Trim(),
            Zone = string.IsNullOrWhiteSpace(dto.Zone) ? null : dto.Zone.Trim(),
            IsActive = true
        };

        _context.Materials.Add(material);
        await _context.SaveChangesAsync(ct);

        return MapToDto(material);
    }

    public async Task<MaterialDto> UpdateAsync(int id, UpdateMaterialDto dto, CancellationToken ct = default)
    {
        if (dto == null)
            throw new DomainException("Update data is required.");

        var material = await _context.Materials.FindAsync([id], ct);

        if (material == null)
            throw new NotFoundException(nameof(Material), id);

        // Invariant: Code cannot be changed after creation.
        material.Update(dto.Name, dto.Uom, dto.Zone, dto.IsActive);

        await _context.SaveChangesAsync(ct);

        return MapToDto(material);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var material = await _context.Materials.FindAsync([id], ct);

        if (material == null)
            throw new NotFoundException(nameof(Material), id);

        // Soft-delete: deactivation with domain method hook
        material.Deactivate(currentStock: 0);

        await _context.SaveChangesAsync(ct);
    }

    public Task<byte[]> GenerateTemplateAsync(CancellationToken ct = default)
    {
        using var workbook = new XLWorkbook();

        // Sheet 1: Materials
        var wsData = workbook.Worksheets.Add("Materials");
        wsData.Cell(1, 1).Value = "Code";
        wsData.Cell(1, 2).Value = "Name";
        wsData.Cell(1, 3).Value = "UoM";
        wsData.Cell(1, 4).Value = "Zone";

        var headerRow = wsData.Row(1);
        headerRow.Style.Font.Bold = true;
        headerRow.Style.Fill.BackgroundColor = XLColor.LightGray;

        // Example row
        wsData.Cell(2, 1).Value = "BOLT-001";
        wsData.Cell(2, 2).Value = "Hex Bolt M8x30";
        wsData.Cell(2, 3).Value = "PCS";
        wsData.Cell(2, 4).Value = "ZONE-A";

        // Code column formatted as Text
        wsData.Column(1).Style.NumberFormat.Format = "@";
        wsData.Columns().AdjustToContents();

        // Sheet 2: Instructions
        var wsInst = workbook.Worksheets.Add("Instructions");
        wsInst.Cell(1, 1).Value = "Column";
        wsInst.Cell(1, 2).Value = "Required";
        wsInst.Cell(1, 3).Value = "Description & Validation Rules";
        wsInst.Row(1).Style.Font.Bold = true;
        wsInst.Row(1).Style.Fill.BackgroundColor = XLColor.LightGray;

        wsInst.Cell(2, 1).Value = "Code";
        wsInst.Cell(2, 2).Value = "Yes";
        wsInst.Cell(2, 3).Value = "Max 30 characters. Uppercase, trimmed. Unique globally. Cannot be changed after creation.";

        wsInst.Cell(3, 1).Value = "Name";
        wsInst.Cell(3, 2).Value = "Yes";
        wsInst.Cell(3, 3).Value = "Max 150 characters. Full descriptive name of the material.";

        wsInst.Cell(4, 1).Value = "UoM";
        wsInst.Cell(4, 2).Value = "Yes";
        wsInst.Cell(4, 3).Value = "Max 10 characters (e.g. PCS, KG, MTR, BOX).";

        wsInst.Cell(5, 1).Value = "Zone";
        wsInst.Cell(5, 2).Value = "No";
        wsInst.Cell(5, 3).Value = "Optional storage zone (max 20 characters).";

        wsInst.Cell(6, 1).Value = "General";
        wsInst.Cell(6, 2).Value = "-";
        wsInst.Cell(6, 3).Value = "Maximum 5000 data rows. Blank rows are skipped. Formatted code column as text.";

        wsInst.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return Task.FromResult(ms.ToArray());
    }

    public async Task<ImportResultDto> ImportAsync(
        Stream fileStream,
        string fileName,
        ImportMode mode,
        bool dryRun,
        bool skipInvalidRows,
        CancellationToken ct = default)
    {
        using var workbook = ExcelImportHelper.LoadAndValidateWorkbook(fileStream, fileName);
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

        // Required headers: Code, Name, UoM (or Uom)
        if (!headerMap.ContainsKey("Code"))
            throw new DomainException("Required column header 'Code' is missing from the Excel file.");
        if (!headerMap.ContainsKey("Name"))
            throw new DomainException("Required column header 'Name' is missing from the Excel file.");

        int uomCol;
        if (headerMap.TryGetValue("UoM", out var col1))
            uomCol = col1;
        else if (headerMap.TryGetValue("Uom", out var col2))
            uomCol = col2;
        else
            throw new DomainException("Required column header 'UoM' is missing from the Excel file.");

        var codeCol = headerMap["Code"];
        var nameCol = headerMap["Name"];
        headerMap.TryGetValue("Zone", out var zoneCol);

        var lastRowNumber = ws.LastRowUsed()?.RowNumber() ?? firstRow.RowNumber();
        var startRow = firstRow.RowNumber() + 1;

        var rawRows = new List<(int RowNumber, string Code, string Name, string Uom, string? Zone, bool IsValid, string? ErrorMessage)>();
        var seenCodesInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var totalDataRows = 0;

        for (var rowNum = startRow; rowNum <= lastRowNumber; rowNum++)
        {
            var row = ws.Row(rowNum);
            var rawCode = ExcelImportHelper.GetCellText(row.Cell(codeCol));
            var rawName = ExcelImportHelper.GetCellText(row.Cell(nameCol));
            var rawUom = ExcelImportHelper.GetCellText(row.Cell(uomCol));
            var rawZone = zoneCol > 0 ? ExcelImportHelper.GetCellText(row.Cell(zoneCol)) : string.Empty;

            // Skip blank row
            if (string.IsNullOrWhiteSpace(rawCode) &&
                string.IsNullOrWhiteSpace(rawName) &&
                string.IsNullOrWhiteSpace(rawUom) &&
                string.IsNullOrWhiteSpace(rawZone))
            {
                continue;
            }

            totalDataRows++;
            if (totalDataRows > 5000)
                throw new DomainException("Excel file exceeds the maximum limit of 5000 data rows.");

            var normalizedCode = rawCode.Trim().ToUpperInvariant();
            string? errorMessage = null;

            if (string.IsNullOrWhiteSpace(normalizedCode))
                errorMessage = "Material code is required.";
            else if (normalizedCode.Length > 30)
                errorMessage = "Material code cannot exceed 30 characters.";
            else if (!seenCodesInFile.Add(normalizedCode))
                errorMessage = $"Duplicate code '{normalizedCode}' in import file at row {rowNum}.";
            else if (string.IsNullOrWhiteSpace(rawName))
                errorMessage = "Material name is required.";
            else if (rawName.Trim().Length > 150)
                errorMessage = "Material name cannot exceed 150 characters.";
            else if (string.IsNullOrWhiteSpace(rawUom))
                errorMessage = "Material UOM is required.";
            else if (rawUom.Trim().Length > 10)
                errorMessage = "Material UOM cannot exceed 10 characters.";
            else if (!string.IsNullOrWhiteSpace(rawZone) && rawZone.Trim().Length > 20)
                errorMessage = "Material zone cannot exceed 20 characters.";

            rawRows.Add((rowNum, normalizedCode, rawName.Trim(), rawUom.Trim(), string.IsNullOrWhiteSpace(rawZone) ? null : rawZone.Trim(), errorMessage == null, errorMessage));
        }

        // Single query for existing codes
        var validCodes = rawRows.Where(r => r.IsValid && !string.IsNullOrEmpty(r.Code)).Select(r => r.Code).Distinct().ToList();
        var existingMap = await _context.Materials
            .Where(m => validCodes.Contains(m.Code))
            .ToDictionaryAsync(m => m.Code, StringComparer.OrdinalIgnoreCase, ct);

        var allResults = new List<ImportRowResult>();
        var newEntities = new List<Material>();

        var newCount = 0;
        var existsCount = 0;
        var errorCount = 0;
        var importedCount = 0;
        var updatedCount = 0;
        var skippedCount = 0;

        foreach (var r in rawRows)
        {
            if (!r.IsValid)
            {
                errorCount++;
                allResults.Add(new ImportRowResult(r.RowNumber, r.Code, ImportRowStatus.Error, r.ErrorMessage));
                continue;
            }

            var exists = existingMap.TryGetValue(r.Code, out var existingMaterial);
            if (exists)
            {
                existsCount++;
                if (dryRun)
                {
                    allResults.Add(new ImportRowResult(r.RowNumber, r.Code, ImportRowStatus.Exists, "Code already exists."));
                }
                else if (mode == ImportMode.CreateOnly)
                {
                    skippedCount++;
                    allResults.Add(new ImportRowResult(r.RowNumber, r.Code, ImportRowStatus.Skipped, "Code already exists (skipped in CreateOnly mode)."));
                }
                else // Upsert
                {
                    try
                    {
                        existingMaterial!.Update(r.Name, r.Uom, r.Zone, true);
                        updatedCount++;
                        allResults.Add(new ImportRowResult(r.RowNumber, r.Code, ImportRowStatus.Updated, "Updated successfully."));
                    }
                    catch (DomainException ex)
                    {
                        errorCount++;
                        allResults.Add(new ImportRowResult(r.RowNumber, r.Code, ImportRowStatus.Error, ex.Message));
                    }
                }
            }
            else
            {
                newCount++;
                if (dryRun)
                {
                    allResults.Add(new ImportRowResult(r.RowNumber, r.Code, ImportRowStatus.New, "Ready to import."));
                }
                else
                {
                    importedCount++;
                    newEntities.Add(new Material
                    {
                        Code = r.Code,
                        Name = r.Name,
                        Uom = r.Uom,
                        Zone = r.Zone,
                        IsActive = true
                    });
                    allResults.Add(new ImportRowResult(r.RowNumber, r.Code, ImportRowStatus.Imported, "Imported successfully."));
                }
            }
        }

        // Commit rules
        if (!dryRun)
        {
            if (errorCount > 0 && !skipInvalidRows)
            {
                // Write nothing when there are errors and skipInvalidRows is false
                importedCount = 0;
                updatedCount = 0;
                skippedCount = 0;
            }
            else
            {
                if (newEntities.Count > 0)
                {
                    _context.Materials.AddRange(newEntities);
                }
                await _context.SaveChangesAsync(ct);
            }
        }

        _logger?.LogInformation(
            "Material import: Total={Total}, New={New}, Exists={Exists}, Errors={Errors}, Imported={Imported}, Updated={Updated}, Skipped={Skipped}, DryRun={DryRun}",
            totalDataRows, newCount, existsCount, errorCount, importedCount, updatedCount, skippedCount, dryRun);

        // Return all error rows and at most the first 500 other rows
        var nonErrorRows = allResults.Where(r => r.Status != ImportRowStatus.Error).Take(500).ToHashSet();
        var returnedRows = allResults
            .Where(r => r.Status == ImportRowStatus.Error || nonErrorRows.Contains(r))
            .OrderBy(r => r.RowNumber)
            .ToList();

        return new ImportResultDto(
            totalDataRows,
            newCount,
            existsCount,
            errorCount,
            importedCount,
            updatedCount,
            skippedCount,
            returnedRows);
    }

    private static MaterialDto MapToDto(Material m) =>
        new(
            m.Id,
            m.Code,
            m.Name,
            m.Uom,
            m.Zone,
            m.IsActive,
            m.CreatedAt,
            m.CreatedBy,
            m.ModifiedAt,
            m.ModifiedBy);
}
