using ClosedXML.Excel;
using Inventory.Application.Common.Models;
using Inventory.Application.Locations.DTOs;
using Inventory.Application.Locations.Services;
using Inventory.Domain.Entities;
using Inventory.Domain.Exceptions;
using Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Inventory.Infrastructure.Services;

public class LocationService : ILocationService
{
    private readonly InventoryDbContext _context;
    private readonly ILogger<LocationService>? _logger;

    public LocationService(InventoryDbContext context, ILogger<LocationService>? logger = null)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<PagedResult<LocationDto>> GetPagedAsync(LocationFilterDto filter, CancellationToken ct = default)
    {
        var page = filter.Page < 1 ? 1 : filter.Page;
        var pageSize = filter.PageSize < 1 ? 20 : (filter.PageSize > 200 ? 200 : filter.PageSize);

        var query = _context.Locations.AsNoTracking();

        if (filter.ActiveOnly == true)
        {
            query = query.Where(l => l.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(filter.Zone))
        {
            var zone = filter.Zone.Trim();
            query = query.Where(l => l.Zone == zone);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim();
            query = query.Where(l => l.Code.Contains(search));
        }

        var totalCount = await query.CountAsync(ct);
        var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

        var locations = await query
            .OrderBy(l => l.Zone)
            .ThenBy(l => l.Code)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        // TODO: computed from stock later (UsedQty = 0, FreeQty = Capacity)
        var items = locations.Select(MapToDto).ToList();

        return new PagedResult<LocationDto>(items, totalCount, page, pageSize, totalPages);
    }

    public async Task<LocationDto> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var location = await _context.Locations
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == id, ct);

        if (location == null)
            throw new NotFoundException(nameof(Location), id);

        return MapToDto(location);
    }

    public async Task<LocationDto> CreateAsync(CreateLocationDto dto, CancellationToken ct = default)
    {
        if (dto == null)
            throw new DomainException("Location data is required.");

        var normalizedCode = dto.Code?.Trim().ToUpperInvariant() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(normalizedCode))
            throw new DomainException("Location code is required.");

        if (normalizedCode.Length > 30)
            throw new DomainException("Location code cannot exceed 30 characters.");

        if (string.IsNullOrWhiteSpace(dto.Zone))
            throw new DomainException("Location zone is required.");

        if (dto.Zone.Trim().Length > 20)
            throw new DomainException("Location zone cannot exceed 20 characters.");

        if (dto.Capacity <= 0)
            throw new DomainException("Location capacity must be greater than zero.");

        var exists = await _context.Locations
            .AnyAsync(l => l.Code == normalizedCode, ct);

        if (exists)
            throw new DomainException($"Location with code '{normalizedCode}' already exists in this branch.");

        var location = new Location
        {
            Code = normalizedCode,
            Zone = dto.Zone.Trim(),
            Capacity = dto.Capacity,
            IsActive = true
        };

        _context.Locations.Add(location);
        await _context.SaveChangesAsync(ct);

        return MapToDto(location);
    }

    public async Task<LocationDto> UpdateAsync(int id, UpdateLocationDto dto, CancellationToken ct = default)
    {
        if (dto == null)
            throw new DomainException("Update data is required.");

        var location = await _context.Locations.FindAsync([id], ct);

        if (location == null)
            throw new NotFoundException(nameof(Location), id);

        // Concurrency token validation
        if (dto.RowVersion == null || dto.RowVersion.Length == 0)
            throw new DomainException("RowVersion is required for location updates.");

        if (location.RowVersion != null && location.RowVersion.Length > 0 && !location.RowVersion.SequenceEqual(dto.RowVersion))
        {
            throw new ConcurrencyException("A concurrency conflict occurred. The location was modified by another operation.");
        }

        // Apply original rowversion for EF Core DB level check
        _context.Entry(location).Property(l => l.RowVersion).OriginalValue = dto.RowVersion;

        // Invariant: Capacity may not be reduced below used qty (usedQty = 0 for now)
        // TODO: computed from stock later
        const int usedQty = 0;
        location.Update(dto.Zone, dto.Capacity, dto.IsActive, usedQty);

        await _context.SaveChangesAsync(ct);

        return MapToDto(location);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var location = await _context.Locations.FindAsync([id], ct);

        if (location == null)
            throw new NotFoundException(nameof(Location), id);

        // Soft-delete: deactivation with domain method hook
        location.Deactivate(currentStock: 0);

        await _context.SaveChangesAsync(ct);
    }

    public Task<byte[]> GenerateTemplateAsync(CancellationToken ct = default)
    {
        using var workbook = new XLWorkbook();

        // Sheet 1: Locations
        var wsData = workbook.Worksheets.Add("Locations");
        wsData.Cell(1, 1).Value = "Code";
        wsData.Cell(1, 2).Value = "Zone";
        wsData.Cell(1, 3).Value = "Capacity";

        var headerRow = wsData.Row(1);
        headerRow.Style.Font.Bold = true;
        headerRow.Style.Fill.BackgroundColor = XLColor.LightGray;

        // Example row
        wsData.Cell(2, 1).Value = "LOC-A01";
        wsData.Cell(2, 2).Value = "ZONE-A";
        wsData.Cell(2, 3).Value = 500;

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
        wsInst.Cell(2, 3).Value = "Max 30 characters. Uppercase, trimmed. Unique per branch.";

        wsInst.Cell(3, 1).Value = "Zone";
        wsInst.Cell(3, 2).Value = "Yes";
        wsInst.Cell(3, 3).Value = "Max 20 characters. Warehouse zone / aisle identifier.";

        wsInst.Cell(4, 1).Value = "Capacity";
        wsInst.Cell(4, 2).Value = "Yes";
        wsInst.Cell(4, 3).Value = "Whole number between 1 and 2147483647. Cannot be reduced below used quantity on update.";

        wsInst.Cell(5, 1).Value = "General";
        wsInst.Cell(5, 2).Value = "-";
        wsInst.Cell(5, 3).Value = "Locations are scoped to the active branch of the user. Maximum 5000 data rows. Blank rows are skipped.";

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

        // Required headers: Code, Zone, Capacity
        if (!headerMap.ContainsKey("Code"))
            throw new DomainException("Required column header 'Code' is missing from the Excel file.");
        if (!headerMap.ContainsKey("Zone"))
            throw new DomainException("Required column header 'Zone' is missing from the Excel file.");
        if (!headerMap.ContainsKey("Capacity"))
            throw new DomainException("Required column header 'Capacity' is missing from the Excel file.");

        var codeCol = headerMap["Code"];
        var zoneCol = headerMap["Zone"];
        var capacityCol = headerMap["Capacity"];

        var lastRowNumber = ws.LastRowUsed()?.RowNumber() ?? firstRow.RowNumber();
        var startRow = firstRow.RowNumber() + 1;

        var rawRows = new List<(int RowNumber, string Code, string Zone, int Capacity, bool IsValid, string? ErrorMessage)>();
        var seenCodesInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var totalDataRows = 0;

        for (var rowNum = startRow; rowNum <= lastRowNumber; rowNum++)
        {
            var row = ws.Row(rowNum);
            var rawCode = ExcelImportHelper.GetCellText(row.Cell(codeCol));
            var rawZone = ExcelImportHelper.GetCellText(row.Cell(zoneCol));
            var rawCapacity = ExcelImportHelper.GetCellText(row.Cell(capacityCol));

            // Skip blank row
            if (string.IsNullOrWhiteSpace(rawCode) &&
                string.IsNullOrWhiteSpace(rawZone) &&
                string.IsNullOrWhiteSpace(rawCapacity))
            {
                continue;
            }

            totalDataRows++;
            if (totalDataRows > 5000)
                throw new DomainException("Excel file exceeds the maximum limit of 5000 data rows.");

            var normalizedCode = rawCode.Trim().ToUpperInvariant();
            string? errorMessage = null;
            int capacity = 0;

            if (string.IsNullOrWhiteSpace(normalizedCode))
                errorMessage = "Location code is required.";
            else if (normalizedCode.Length > 30)
                errorMessage = "Location code cannot exceed 30 characters.";
            else if (!seenCodesInFile.Add(normalizedCode))
                errorMessage = $"Duplicate code '{normalizedCode}' in import file at row {rowNum}.";
            else if (string.IsNullOrWhiteSpace(rawZone))
                errorMessage = "Location zone is required.";
            else if (rawZone.Trim().Length > 20)
                errorMessage = "Location zone cannot exceed 20 characters.";
            else if (string.IsNullOrWhiteSpace(rawCapacity) ||
                     !int.TryParse(rawCapacity.Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out capacity) ||
                     capacity <= 0)
            {
                errorMessage = "Location capacity must be a positive whole number between 1 and 2147483647.";
            }

            rawRows.Add((rowNum, normalizedCode, rawZone.Trim(), capacity, errorMessage == null, errorMessage));
        }

        // Single query for existing codes (scoped by BranchId via EF Core Global Query Filter)
        var validCodes = rawRows.Where(r => r.IsValid && !string.IsNullOrEmpty(r.Code)).Select(r => r.Code).Distinct().ToList();
        var existingMap = await _context.Locations
            .Where(l => validCodes.Contains(l.Code))
            .ToDictionaryAsync(l => l.Code, StringComparer.OrdinalIgnoreCase, ct);

        var allResults = new List<ImportRowResult>();
        var newEntities = new List<Location>();

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

            var exists = existingMap.TryGetValue(r.Code, out var existingLocation);
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
                        const int usedQty = 0; // TODO: computed from stock later
                        existingLocation!.Update(r.Zone, r.Capacity, true, usedQty);
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
                    newEntities.Add(new Location
                    {
                        Code = r.Code,
                        Zone = r.Zone,
                        Capacity = r.Capacity,
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
                    _context.Locations.AddRange(newEntities);
                }
                await _context.SaveChangesAsync(ct);
            }
        }

        _logger?.LogInformation(
            "Location import: Total={Total}, New={New}, Exists={Exists}, Errors={Errors}, Imported={Imported}, Updated={Updated}, Skipped={Skipped}, DryRun={DryRun}",
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

    private static LocationDto MapToDto(Location l)
    {
        // TODO: computed from stock later
        const int usedQty = 0;
        var freeQty = Math.Max(0, l.Capacity - usedQty);

        return new LocationDto(
            l.Id,
            l.BranchId,
            l.Code,
            l.Zone,
            l.Capacity,
            usedQty,
            freeQty,
            l.IsActive,
            l.RowVersion,
            l.CreatedAt,
            l.CreatedBy,
            l.ModifiedAt,
            l.ModifiedBy);
    }
}
