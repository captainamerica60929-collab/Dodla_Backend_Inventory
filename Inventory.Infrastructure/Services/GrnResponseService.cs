using Inventory.Application.GrnResponses.DTOs;
using Inventory.Application.GrnResponses.Services;
using Inventory.Domain.Entities;
using Inventory.Domain.Enums;
using Inventory.Domain.Exceptions;
using Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Infrastructure.Services;

public class GrnResponseService : IGrnResponseService
{
    private readonly InventoryDbContext _context;

    public GrnResponseService(InventoryDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResultDto<GrnResponseListItemDto>> GetPagedAsync(GrnResponseFilterDto filter, CancellationToken ct = default)
    {
        var query = _context.GrnApiResponses.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            if (!Enum.TryParse<GrnResponseStatus>(filter.Status.Trim(), ignoreCase: true, out var parsedStatus))
            {
                throw new DomainException($"Unknown status '{filter.Status}'. Valid values are: {string.Join(", ", Enum.GetNames<GrnResponseStatus>())}.");
            }
            query = query.Where(r => r.Status == parsedStatus);
        }

        if (filter.FetchBatchId.HasValue)
        {
            query = query.Where(r => r.FetchBatchId == filter.FetchBatchId.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.GrnNo))
        {
            var prefix = filter.GrnNo.Trim();
            query = query.Where(r => r.GrnNo.StartsWith(prefix));
        }

        if (filter.FromDate.HasValue)
        {
            query = query.Where(r => r.FetchedAt >= filter.FromDate.Value);
        }

        if (filter.ToDate.HasValue)
        {
            // Inclusive of the whole day
            var exclusiveEnd = filter.ToDate.Value.Date.AddDays(1);
            query = query.Where(r => r.FetchedAt < exclusiveEnd);
        }

        var total = await query.CountAsync(ct);

        var pageSize = Math.Clamp(filter.PageSize, 1, 200);
        var page = Math.Max(1, filter.Page);

        // Default sort: FetchedAt desc, then Id desc for stable paging
        var items = await query
            .OrderByDescending(r => r.FetchedAt)
            .ThenByDescending(r => r.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new GrnResponseListItemDto(
                r.Id,
                r.BranchId,
                r.ApiCredentialId,
                r.FetchBatchId,
                r.FetchedAt,
                r.FetchedBy,
                r.GrnNo,
                r.GrnYear,
                r.GrnItemNo,
                r.GrnDate,
                r.InvoiceNo,
                r.InvoiceDate,
                r.Qty,
                r.Uom,
                r.MaterialCode,
                r.PartName,
                r.Description,
                r.Plant,
                r.StorageLocation,
                r.BatchNo,
                r.PoNo,
                r.PoItem,
                r.VendorCode,
                r.VendorName,
                r.SourceKey,
                r.Status,
                r.ErrorMessage,
                r.ProcessedAt))
            .ToListAsync(ct);

        return new PagedResultDto<GrnResponseListItemDto>(items, total);
    }

    public async Task<GrnResponseDetailDto> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var item = await _context.GrnApiResponses
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, ct);

        if (item == null)
            throw new NotFoundException(nameof(GrnApiResponse), id);

        return new GrnResponseDetailDto(
            item.Id,
            item.BranchId,
            item.ApiCredentialId,
            item.FetchBatchId,
            item.FetchedAt,
            item.FetchedBy,
            item.GrnNo,
            item.GrnYear,
            item.GrnItemNo,
            item.GrnDate,
            item.InvoiceNo,
            item.InvoiceDate,
            item.Qty,
            item.Uom,
            item.MaterialCode,
            item.PartName,
            item.Description,
            item.Plant,
            item.StorageLocation,
            item.BatchNo,
            item.PoNo,
            item.PoItem,
            item.VendorCode,
            item.VendorName,
            item.SourceKey,
            item.RawJson,
            item.Status,
            item.ErrorMessage,
            item.ProcessedAt);
    }

    public async Task DiscardBatchAsync(Guid fetchBatchId, CancellationToken ct = default)
    {
        var rows = await _context.GrnApiResponses
            .Where(r => r.FetchBatchId == fetchBatchId)
            .ToListAsync(ct);

        if (rows.Count == 0)
            throw new NotFoundException($"Batch '{fetchBatchId}' was not found.");

        if (rows.Any(r => r.Status == GrnResponseStatus.Processed))
        {
            throw new DomainException($"Cannot discard batch '{fetchBatchId}' because one or more rows have already been processed.");
        }

        var deletable = rows.Where(r => r.Status == GrnResponseStatus.New || r.Status == GrnResponseStatus.Error).ToList();
        _context.GrnApiResponses.RemoveRange(deletable);

        await _context.SaveChangesAsync(ct);
    }
}
