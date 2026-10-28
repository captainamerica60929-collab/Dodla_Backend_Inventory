using System.Globalization;
using System.Security.Claims;
using Inventory.Application.Common.Interfaces;
using Inventory.Application.Common.Models;
using Inventory.Application.Tags.DTOs;
using Inventory.Application.Tags.Services;
using Inventory.Domain.Common;
using Inventory.Domain.Entities;
using Inventory.Domain.Enums;
using Inventory.Domain.Exceptions;
using Inventory.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Infrastructure.Services;

public class TagService : ITagService
{
    private readonly InventoryDbContext _context;
    private readonly ITagNumberAllocator _allocator;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public TagService(
        InventoryDbContext context,
        ITagNumberAllocator allocator,
        IClock clock,
        ICurrentUser currentUser,
        IHttpContextAccessor httpContextAccessor)
    {
        _context = context;
        _allocator = allocator;
        _clock = clock;
        _currentUser = currentUser;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<PagedResult<GrnPrintRowDto>> GetGrnPrintRowsAsync(
        PrintStateFilter printState,
        string? grnNo,
        DateTime? fromDate,
        DateTime? toDate,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 50;
        if (pageSize > 200) pageSize = 200;

        // Base query: New or Processed (never Error), branch scoped via global query filter
        var baseQuery = _context.GrnApiResponses
            .Where(r => r.Status == GrnResponseStatus.New || r.Status == GrnResponseStatus.Processed);

        if (!string.IsNullOrWhiteSpace(grnNo))
        {
            var prefix = grnNo.Trim();
            baseQuery = baseQuery.Where(r => r.GrnNo.StartsWith(prefix));
        }

        if (fromDate.HasValue)
        {
            baseQuery = baseQuery.Where(r => r.GrnDate >= fromDate.Value);
        }

        if (toDate.HasValue)
        {
            var nextDay = toDate.Value.Date.AddDays(1);
            baseQuery = baseQuery.Where(r => r.GrnDate < nextDay);
        }

        // Left join with Tags in a single query (no N+1), excluding RawJson
        var joinedQuery = from r in baseQuery
                          join t in _context.Tags on r.Id equals t.GrnApiResponseId into tagGroup
                          from t in tagGroup.DefaultIfEmpty()
                          select new { Row = r, Tag = t };

        // Filter by print state:
        // NotPrepared = no tag
        // Prepared = tag exists and PrintCount == 0
        // Printed = tag exists and PrintCount > 0
        if (printState == PrintStateFilter.NotPrepared)
        {
            joinedQuery = joinedQuery.Where(x => x.Tag == null);
        }
        else if (printState == PrintStateFilter.Prepared)
        {
            joinedQuery = joinedQuery.Where(x => x.Tag != null && x.Tag.PrintCount == 0);
        }
        else if (printState == PrintStateFilter.Printed)
        {
            joinedQuery = joinedQuery.Where(x => x.Tag != null && x.Tag.PrintCount > 0);
        }

        // Deterministic sort: GrnDate desc, Id desc
        joinedQuery = joinedQuery
            .OrderByDescending(x => x.Row.GrnDate)
            .ThenByDescending(x => x.Row.Id);

        var totalCount = await joinedQuery.CountAsync(ct);
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        var rawItems = await joinedQuery
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new
            {
                x.Row.Id,
                x.Row.GrnNo,
                x.Row.GrnDate,
                x.Row.InvoiceNo,
                x.Row.PartName,
                x.Row.Description,
                x.Row.Qty,
                x.Row.Uom,
                x.Row.Plant,
                RowStatus = x.Row.Status,
                Tag = x.Tag != null ? new TagSummaryDto(x.Tag.Id, x.Tag.TagNo, x.Tag.Status.ToString(), x.Tag.PrintCount) : null
            })
            .ToListAsync(ct);

        var items = rawItems
            .Select(x => new GrnPrintRowDto(
                x.Id,
                x.GrnNo,
                x.GrnDate.HasValue ? x.GrnDate.Value.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture) : null,
                x.InvoiceNo,
                x.PartName,
                x.Description,
                x.Qty,
                x.Uom,
                x.Plant,
                x.RowStatus.ToString(),
                x.Tag
            ))
            .ToList();

        return new PagedResult<GrnPrintRowDto>(items, totalCount, page, pageSize, totalPages);
    }

    public async Task<TagDto> PrepareTagAsync(int grnRowId, CancellationToken ct = default)
    {
        // 1. Initial check (fast path)
        var existingTag = await GetExistingTagDtoAsync(grnRowId, ct);
        if (existingTag != null)
            return existingTag;

        // 2. Open transaction for atomic allocation and creation if relational provider
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? tx = null;
        if (_context.Database.IsRelational())
        {
            tx = await _context.Database.BeginTransactionAsync(ct);
        }

        try
        {
            // Re-check inside transaction (concurrency race check)
            existingTag = await GetExistingTagDtoAsync(grnRowId, ct);
            if (existingTag != null)
            {
                if (tx != null) await tx.RollbackAsync(ct);
                return existingTag;
            }

            var row = await _context.GrnApiResponses
                .FirstOrDefaultAsync(r => r.Id == grnRowId, ct)
                ?? throw new NotFoundException("GrnApiResponse", grnRowId);

            if (row.Status == GrnResponseStatus.Error)
                throw new DomainException($"Cannot prepare tag for GRN row {grnRowId} with status Error.");

            // Validate Quantity: whole number, > 0, <= int.MaxValue, checked directly on decimal
            ValidateQuantity(row.Qty);
            var qtyInt = (int)row.Qty!.Value;

            // Branch code from branch
            var branch = await _context.Branches
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(b => b.Id == row.BranchId, ct)
                ?? throw new NotFoundException("Branch", row.BranchId);

            var year = _clock.UtcNow.Year;

            // Atomically allocate next sequence number
            var firstNo = await _allocator.AllocateAsync(row.BranchId, year, 1, ct);
            var tagNo = TagNumberFormatter.Format(branch.Code, year, firstNo);

            var tag = new Tag
            {
                BranchId = row.BranchId,
                GrnApiResponseId = row.Id,
                TagNo = tagNo,
                Qty = qtyInt,
                RemainingQty = qtyInt,
                Status = TagStatus.Generated,
                PrintCount = 0
            };

            _context.Tags.Add(tag);
            row.Status = GrnResponseStatus.Processed;
            row.ProcessedAt = _clock.UtcNow;

            await _context.SaveChangesAsync(ct);
            if (tx != null) await tx.CommitAsync(ct);

            tag.GrnApiResponse = row;
            return MapToDto(tag, branch.Code);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            if (tx != null) await tx.RollbackAsync(ct);

            var winnerTag = await GetExistingTagDtoAsync(grnRowId, ct);
            if (winnerTag != null)
                return winnerTag;

            throw;
        }
        catch
        {
            if (tx != null) await tx.RollbackAsync(ct);
            throw;
        }
        finally
        {
            if (tx != null) await tx.DisposeAsync();
        }
    }

    public async Task<TagDto> MarkPrintedAsync(int tagId, string? clientIp, CancellationToken ct = default)
    {
        var tag = await _context.Tags
            .Include(t => t.GrnApiResponse)
            .FirstOrDefaultAsync(t => t.Id == tagId, ct)
            ?? throw new NotFoundException("Tag", tagId);

        if (tag.Status == TagStatus.Void)
            throw new DomainException("Cannot print a void tag.");

        var branch = await _context.Branches
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(b => b.Id == tag.BranchId, ct)
            ?? throw new NotFoundException("Branch", tag.BranchId);

        var utcNow = _clock.UtcNow;
        var userSnapshot = await GetUserNameSnapshotAsync(ct);

        if (tag.PrintCount == 0)
        {
            tag.Status = TagStatus.Printed;
            tag.FirstPrintedAt = utcNow;
        }

        tag.PrintCount++;
        tag.LastPrintedAt = utcNow;

        var log = new TagPrintLog
        {
            TagId = tag.Id,
            Kind = TagPrintKind.Print,
            Reason = null,
            PrintedAt = utcNow,
            UserId = _currentUser.UserId,
            UserNameSnapshot = userSnapshot,
            ClientIp = clientIp
        };

        _context.TagPrintLogs.Add(log);
        await _context.SaveChangesAsync(ct);

        return MapToDto(tag, branch.Code);
    }

    public async Task<TagDto> ReprintAsync(int tagId, string reason, string? clientIp, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("Reprint reason is required.");

        var cleanReason = reason.Trim();
        if (cleanReason.Length > 200)
            throw new DomainException("Reprint reason cannot exceed 200 characters.");

        var tag = await _context.Tags
            .Include(t => t.GrnApiResponse)
            .FirstOrDefaultAsync(t => t.Id == tagId, ct)
            ?? throw new NotFoundException("Tag", tagId);

        if (tag.Status == TagStatus.Void)
            throw new DomainException("Cannot reprint a void tag.");

        if (tag.PrintCount == 0)
            throw new DomainException("This tag has not been printed yet. Use the printed call after printing.");

        var branch = await _context.Branches
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(b => b.Id == tag.BranchId, ct)
            ?? throw new NotFoundException("Branch", tag.BranchId);

        var utcNow = _clock.UtcNow;
        var userSnapshot = await GetUserNameSnapshotAsync(ct);

        tag.PrintCount++;
        tag.LastPrintedAt = utcNow;

        var log = new TagPrintLog
        {
            TagId = tag.Id,
            Kind = TagPrintKind.Reprint,
            Reason = cleanReason,
            PrintedAt = utcNow,
            UserId = _currentUser.UserId,
            UserNameSnapshot = userSnapshot,
            ClientIp = clientIp
        };

        _context.TagPrintLogs.Add(log);
        await _context.SaveChangesAsync(ct);

        return MapToDto(tag, branch.Code);
    }

    public async Task<IReadOnlyList<TagPrintLogDto>> GetPrintLogsAsync(int tagId, CancellationToken ct = default)
    {
        var tagExists = await _context.Tags.AnyAsync(t => t.Id == tagId, ct);
        if (!tagExists)
            throw new NotFoundException("Tag", tagId);

        return await _context.TagPrintLogs
            .Where(l => l.TagId == tagId)
            .OrderByDescending(l => l.PrintedAt)
            .ThenByDescending(l => l.Id)
            .Select(l => new TagPrintLogDto(
                l.Id,
                l.TagId,
                l.Kind.ToString(),
                l.Reason,
                l.PrintedAt.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                l.UserId,
                l.UserNameSnapshot,
                l.ClientIp
            ))
            .ToListAsync(ct);
    }

    private async Task<TagDto?> GetExistingTagDtoAsync(int grnRowId, CancellationToken ct)
    {
        var tag = await _context.Tags
            .Include(t => t.GrnApiResponse)
            .FirstOrDefaultAsync(t => t.GrnApiResponseId == grnRowId, ct);

        if (tag == null)
            return null;

        var branch = await _context.Branches
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(b => b.Id == tag.BranchId, ct);

        return MapToDto(tag, branch?.Code ?? "MAIN");
    }

    private static void ValidateQuantity(decimal? qty)
    {
        if (!qty.HasValue)
            throw new DomainException("Quantity is required.");

        var val = qty.Value;
        if (val <= 0m)
            throw new DomainException("Quantity must be greater than zero.");

        if (val > int.MaxValue)
            throw new DomainException($"Quantity cannot exceed {int.MaxValue}.");

        if (decimal.Floor(val) != val)
            throw new DomainException("Quantity must be a whole number.");
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        var msg = ex.InnerException?.Message ?? ex.Message;
        return msg.Contains("IX_Tags_GrnApiResponseId", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("duplicate key", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("2601")
            || msg.Contains("2627");
    }

    private async Task<string> GetUserNameSnapshotAsync(CancellationToken ct)
    {
        var claimName = _httpContextAccessor.HttpContext?.User?.FindFirstValue("name");
        if (!string.IsNullOrWhiteSpace(claimName))
            return claimName;

        var userId = _currentUser.UserId;
        if (userId > 0)
        {
            var user = await _context.AppUsers
                .IgnoreQueryFilters()
                .Include(u => u.Employee)
                .FirstOrDefaultAsync(u => u.Id == userId, ct);

            if (user?.Employee?.Name != null && !string.IsNullOrWhiteSpace(user.Employee.Name))
                return user.Employee.Name;

            if (!string.IsNullOrWhiteSpace(user?.Username))
                return user.Username;
        }

        return "Unknown";
    }

    private static TagDto MapToDto(Tag tag, string branchCode)
    {
        var row = tag.GrnApiResponse;
        return new TagDto(
            tag.Id,
            tag.TagNo,
            tag.Status.ToString(),
            tag.PrintCount,
            row?.GrnNo ?? string.Empty,
            row?.GrnDate.HasValue == true ? row.GrnDate.Value.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture) : null,
            row?.InvoiceNo,
            row?.PartName,
            row?.Description,
            tag.Qty,
            row?.Plant,
            branchCode
        );
    }
}
