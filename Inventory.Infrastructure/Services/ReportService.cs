using System.Data;
using Dapper;
using Inventory.Application.Common.Interfaces;
using Inventory.Application.Common.Models;
using Inventory.Application.Reports.DTOs;
using Inventory.Application.Reports.Services;
using Inventory.Domain.Enums;
using Inventory.Domain.Exceptions;
using Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Infrastructure.Services;

public class ReportService : IReportService
{
    private readonly InventoryDbContext _context;
    private readonly ICurrentUser _currentUser;

    public ReportService(InventoryDbContext context, ICurrentUser currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    private int? ResolveEffectiveBranchId(int? requestedBranchId, bool crossBranch)
    {
        bool isAdmin = string.Equals(_currentUser.Role, "Admin", StringComparison.OrdinalIgnoreCase);

        if (isAdmin)
        {
            return crossBranch ? null : (requestedBranchId ?? _currentUser.BranchId);
        }

        if (crossBranch || (requestedBranchId.HasValue && requestedBranchId.Value != _currentUser.BranchId))
        {
            throw new DomainException("Cross-branch reporting is restricted to Administrators.");
        }

        return _currentUser.BranchId;
    }

    public async Task<PagedResult<InwardReportRowDto>> GetInwardReportAsync(InwardReportFilter filter, CancellationToken ct = default)
    {
        filter ??= new InwardReportFilter();
        if (filter.FromDate.HasValue && filter.ToDate.HasValue && filter.FromDate.Value > filter.ToDate.Value)
        {
            throw new DomainException("From Date cannot be later than To Date.");
        }

        var branchId = ResolveEffectiveBranchId(filter.BranchId, filter.CrossBranch);
        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);
        var offset = (page - 1) * pageSize;

        if (_context.Database.IsRelational())
        {
            var conn = _context.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

            var sqlWhere = @"WHERE sm.MovementType = 'In'
                AND (@BranchId IS NULL OR sm.BranchId = @BranchId)
                AND (@FromDate IS NULL OR sm.CreatedAt >= @FromDate)
                AND (@ToDate IS NULL OR sm.CreatedAt <= @ToDate)
                AND (@MaterialCode IS NULL OR sm.MaterialCode = @MaterialCode)
                AND (@LocationCode IS NULL OR l.Code = @LocationCode)";

            var countSql = $@"SELECT COUNT(1)
                FROM StockMovements sm
                LEFT JOIN Locations l ON sm.LocationId = l.Id
                {sqlWhere}";

            var dataSql = $@"SELECT sm.Id, sm.BranchId, b.Code AS BranchCode, t.TagNo, sm.MaterialCode,
                       m.Name AS PartName, grn.BatchNo, l.Code AS LocationCode, sm.Qty, sm.CreatedAt AS InwardAt,
                       sm.UserNameSnapshot AS OperatorName, sm.ClientIp
                FROM StockMovements sm
                INNER JOIN Tags t ON sm.TagId = t.Id
                INNER JOIN Branches b ON sm.BranchId = b.Id
                LEFT JOIN Locations l ON sm.LocationId = l.Id
                LEFT JOIN Materials m ON sm.MaterialCode = m.Code
                LEFT JOIN GrnApiResponses grn ON t.GrnApiResponseId = grn.Id
                {sqlWhere}
                ORDER BY sm.CreatedAt DESC, sm.Id DESC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

            var p = new
            {
                BranchId = branchId,
                FromDate = filter.FromDate,
                ToDate = filter.ToDate,
                MaterialCode = string.IsNullOrWhiteSpace(filter.MaterialCode) ? null : filter.MaterialCode.Trim(),
                LocationCode = string.IsNullOrWhiteSpace(filter.LocationCode) ? null : filter.LocationCode.Trim(),
                Offset = offset,
                PageSize = pageSize
            };

            var totalCount = await conn.ExecuteScalarAsync<int>(countSql, p);
            var items = (await conn.QueryAsync<InwardReportRowDto>(dataSql, p)).ToList();
            var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

            return new PagedResult<InwardReportRowDto>(items, totalCount, page, pageSize, totalPages);
        }

        // EF InMemory fallback
        var query = _context.StockMovements
            .IgnoreQueryFilters()
            .Where(sm => sm.MovementType == StockMovementType.In);

        if (branchId.HasValue) query = query.Where(sm => sm.BranchId == branchId.Value);
        if (filter.FromDate.HasValue) query = query.Where(sm => sm.CreatedAt >= filter.FromDate.Value);
        if (filter.ToDate.HasValue) query = query.Where(sm => sm.CreatedAt <= filter.ToDate.Value);
        if (!string.IsNullOrWhiteSpace(filter.MaterialCode))
        {
            var mat = filter.MaterialCode.Trim();
            query = query.Where(sm => sm.MaterialCode == mat);
        }
        if (!string.IsNullOrWhiteSpace(filter.LocationCode))
        {
            var loc = filter.LocationCode.Trim();
            query = query.Where(sm => sm.Location != null && sm.Location.Code == loc);
        }

        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(sm => sm.CreatedAt)
            .ThenByDescending(sm => sm.Id)
            .Skip(offset)
            .Take(pageSize)
            .Select(sm => new InwardReportRowDto(
                sm.Id,
                sm.BranchId,
                sm.BranchId.ToString(),
                sm.Tag != null ? sm.Tag.TagNo : string.Empty,
                sm.MaterialCode,
                sm.Tag != null && sm.Tag.GrnApiResponse != null ? sm.Tag.GrnApiResponse.PartName : null,
                sm.Tag != null && sm.Tag.GrnApiResponse != null ? sm.Tag.GrnApiResponse.BatchNo : null,
                sm.Location != null ? sm.Location.Code : null,
                sm.Qty,
                sm.CreatedAt,
                sm.UserNameSnapshot,
                sm.ClientIp))
            .ToListAsync(ct);

        var pages = (int)Math.Ceiling((double)total / pageSize);
        return new PagedResult<InwardReportRowDto>(rows, total, page, pageSize, pages);
    }

    public async Task<PagedResult<OutwardReportRowDto>> GetOutwardReportAsync(OutwardReportFilter filter, CancellationToken ct = default)
    {
        filter ??= new OutwardReportFilter();
        if (filter.FromDate.HasValue && filter.ToDate.HasValue && filter.FromDate.Value > filter.ToDate.Value)
        {
            throw new DomainException("From Date cannot be later than To Date.");
        }

        var branchId = ResolveEffectiveBranchId(filter.BranchId, filter.CrossBranch);
        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);
        var offset = (page - 1) * pageSize;

        if (_context.Database.IsRelational())
        {
            var conn = _context.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

            var sqlWhere = @"WHERE sm.MovementType = 'Out'
                AND (@BranchId IS NULL OR sm.BranchId = @BranchId)
                AND (@FromDate IS NULL OR sm.CreatedAt >= @FromDate)
                AND (@ToDate IS NULL OR sm.CreatedAt <= @ToDate)
                AND (@MaterialCode IS NULL OR sm.MaterialCode = @MaterialCode)
                AND (@TagNo IS NULL OR t.TagNo = @TagNo)";

            var countSql = $@"SELECT COUNT(1)
                FROM StockMovements sm
                INNER JOIN Tags t ON sm.TagId = t.Id
                {sqlWhere}";

            var dataSql = $@"SELECT sm.Id, sm.BranchId, b.Code AS BranchCode, t.TagNo, sm.MaterialCode,
                       m.Name AS PartName, l.Code AS LocationCode, sm.Qty, sm.CreatedAt AS IssuedAt,
                       sm.InwardAt AS OriginalInwardAt, sm.UserNameSnapshot AS OperatorName, sm.ClientIp
                FROM StockMovements sm
                INNER JOIN Tags t ON sm.TagId = t.Id
                INNER JOIN Branches b ON sm.BranchId = b.Id
                LEFT JOIN Locations l ON sm.LocationId = l.Id
                LEFT JOIN Materials m ON sm.MaterialCode = m.Code
                {sqlWhere}
                ORDER BY sm.CreatedAt DESC, sm.Id DESC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

            var p = new
            {
                BranchId = branchId,
                FromDate = filter.FromDate,
                ToDate = filter.ToDate,
                MaterialCode = string.IsNullOrWhiteSpace(filter.MaterialCode) ? null : filter.MaterialCode.Trim(),
                TagNo = string.IsNullOrWhiteSpace(filter.TagNo) ? null : filter.TagNo.Trim(),
                Offset = offset,
                PageSize = pageSize
            };

            var totalCount = await conn.ExecuteScalarAsync<int>(countSql, p);
            var items = (await conn.QueryAsync<OutwardReportRowDto>(dataSql, p)).ToList();
            var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

            return new PagedResult<OutwardReportRowDto>(items, totalCount, page, pageSize, totalPages);
        }

        // EF InMemory fallback
        var query = _context.StockMovements
            .IgnoreQueryFilters()
            .Where(sm => sm.MovementType == StockMovementType.Out);

        if (branchId.HasValue) query = query.Where(sm => sm.BranchId == branchId.Value);
        if (filter.FromDate.HasValue) query = query.Where(sm => sm.CreatedAt >= filter.FromDate.Value);
        if (filter.ToDate.HasValue) query = query.Where(sm => sm.CreatedAt <= filter.ToDate.Value);
        if (!string.IsNullOrWhiteSpace(filter.MaterialCode))
        {
            var mat = filter.MaterialCode.Trim();
            query = query.Where(sm => sm.MaterialCode == mat);
        }
        if (!string.IsNullOrWhiteSpace(filter.TagNo))
        {
            var tagNo = filter.TagNo.Trim();
            query = query.Where(sm => sm.Tag != null && sm.Tag.TagNo == tagNo);
        }

        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(sm => sm.CreatedAt)
            .ThenByDescending(sm => sm.Id)
            .Skip(offset)
            .Take(pageSize)
            .Select(sm => new OutwardReportRowDto(
                sm.Id,
                sm.BranchId,
                sm.BranchId.ToString(),
                sm.Tag != null ? sm.Tag.TagNo : string.Empty,
                sm.MaterialCode,
                sm.Tag != null && sm.Tag.GrnApiResponse != null ? sm.Tag.GrnApiResponse.PartName : null,
                sm.Location != null ? sm.Location.Code : null,
                sm.Qty,
                sm.CreatedAt,
                sm.InwardAt,
                sm.UserNameSnapshot,
                sm.ClientIp))
            .ToListAsync(ct);

        var pages = (int)Math.Ceiling((double)total / pageSize);
        return new PagedResult<OutwardReportRowDto>(rows, total, page, pageSize, pages);
    }

    public async Task<PagedResult<InStockReportRowDto>> GetInStockReportAsync(InStockReportFilter filter, CancellationToken ct = default)
    {
        filter ??= new InStockReportFilter();
        var branchId = ResolveEffectiveBranchId(filter.BranchId, filter.CrossBranch);
        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);
        var offset = (page - 1) * pageSize;

        if (_context.Database.IsRelational())
        {
            var conn = _context.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

            var sqlWhere = @"WHERE sr.Qty > 0 AND t.Status = 'InStock'
                AND (@BranchId IS NULL OR sr.BranchId = @BranchId)
                AND (@MaterialCode IS NULL OR sr.MaterialCode = @MaterialCode)
                AND (@LocationCode IS NULL OR l.Code = @LocationCode)";

            var countSql = $@"SELECT COUNT(1) FROM (
                SELECT sr.BranchId, sr.MaterialCode, sr.LocationId
                FROM StockRecords sr
                INNER JOIN Tags t ON sr.TagId = t.Id
                INNER JOIN Locations l ON sr.LocationId = l.Id
                {sqlWhere}
                GROUP BY sr.BranchId, sr.MaterialCode, sr.LocationId
            ) AS grouped";

            var dataSql = $@"SELECT sr.BranchId, b.Code AS BranchCode, sr.MaterialCode,
                       m.Name AS PartName, sr.LocationId, l.Code AS LocationCode, l.Zone AS LocationZone,
                       COUNT(DISTINCT sr.TagId) AS TotalTags, SUM(sr.Qty) AS TotalInStockQty,
                       MIN(sr.InwardAt) AS OldestInwardAt, MAX(sr.InwardAt) AS NewestInwardAt
                FROM StockRecords sr
                INNER JOIN Tags t ON sr.TagId = t.Id
                INNER JOIN Branches b ON sr.BranchId = b.Id
                INNER JOIN Locations l ON sr.LocationId = l.Id
                LEFT JOIN Materials m ON sr.MaterialCode = m.Code
                {sqlWhere}
                GROUP BY sr.BranchId, b.Code, sr.MaterialCode, m.Name, sr.LocationId, l.Code, l.Zone
                ORDER BY sr.MaterialCode, l.Code
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

            var p = new
            {
                BranchId = branchId,
                MaterialCode = string.IsNullOrWhiteSpace(filter.MaterialCode) ? null : filter.MaterialCode.Trim(),
                LocationCode = string.IsNullOrWhiteSpace(filter.LocationCode) ? null : filter.LocationCode.Trim(),
                Offset = offset,
                PageSize = pageSize
            };

            var totalCount = await conn.ExecuteScalarAsync<int>(countSql, p);
            var items = (await conn.QueryAsync<InStockReportRowDto>(dataSql, p)).ToList();
            var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

            return new PagedResult<InStockReportRowDto>(items, totalCount, page, pageSize, totalPages);
        }

        // EF InMemory fallback
        var query = _context.StockRecords
            .IgnoreQueryFilters()
            .Include(sr => sr.Location)
            .Include(sr => sr.Tag)
            .Where(sr => sr.Qty > 0 && sr.Tag.Status == TagStatus.InStock);

        if (branchId.HasValue) query = query.Where(sr => sr.BranchId == branchId.Value);
        if (!string.IsNullOrWhiteSpace(filter.MaterialCode))
        {
            var mat = filter.MaterialCode.Trim();
            query = query.Where(sr => sr.MaterialCode == mat);
        }
        if (!string.IsNullOrWhiteSpace(filter.LocationCode))
        {
            var loc = filter.LocationCode.Trim();
            query = query.Where(sr => sr.Location != null && sr.Location.Code == loc);
        }

        var grouped = await query
            .GroupBy(sr => new { sr.BranchId, sr.MaterialCode, sr.LocationId, LocCode = sr.Location!.Code, LocZone = sr.Location.Zone })
            .Select(g => new InStockReportRowDto(
                g.Key.BranchId,
                g.Key.BranchId.ToString(),
                g.Key.MaterialCode,
                null,
                g.Key.LocationId,
                g.Key.LocCode,
                g.Key.LocZone,
                g.Select(x => x.TagId).Distinct().Count(),
                g.Sum(x => x.Qty),
                g.Min(x => x.InwardAt),
                g.Max(x => x.InwardAt)))
            .ToListAsync(ct);

        var total = grouped.Count;
        var rows = grouped.Skip(offset).Take(pageSize).ToList();
        var pages = (int)Math.Ceiling((double)total / pageSize);

        return new PagedResult<InStockReportRowDto>(rows, total, page, pageSize, pages);
    }

    public async Task<PagedResult<LabelScanReportRowDto>> GetLabelScanReportAsync(LabelScanReportFilter filter, CancellationToken ct = default)
    {
        filter ??= new LabelScanReportFilter();
        var branchId = ResolveEffectiveBranchId(filter.BranchId, filter.CrossBranch);
        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);
        var offset = (page - 1) * pageSize;

        // Combines TagPrintLogs and scan movements
        var printQuery = _context.TagPrintLogs
            .IgnoreQueryFilters()
            .Include(pl => pl.Tag)
            .Where(pl => pl.Tag != null)
            .AsQueryable();

        var moveQuery = _context.StockMovements
            .IgnoreQueryFilters()
            .Include(sm => sm.Tag)
            .Where(sm => sm.Tag != null)
            .AsQueryable();

        if (branchId.HasValue)
        {
            printQuery = printQuery.Where(pl => pl.Tag!.BranchId == branchId.Value);
            moveQuery = moveQuery.Where(sm => sm.BranchId == branchId.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.TagNo))
        {
            var tagNo = filter.TagNo.Trim();
            printQuery = printQuery.Where(pl => pl.Tag!.TagNo == tagNo);
            moveQuery = moveQuery.Where(sm => sm.Tag.TagNo == tagNo);
        }

        var printRows = await printQuery
            .OrderByDescending(pl => pl.PrintedAt)
            .Take(200)
            .Select(pl => new LabelScanReportRowDto(
                pl.Id,
                pl.Tag != null ? pl.Tag.BranchId : 0,
                pl.Tag != null ? pl.Tag.BranchId.ToString() : "0",
                pl.Tag != null ? pl.Tag.TagNo : string.Empty,
                pl.Kind.ToString(),
                null,
                pl.Reason,
                pl.UserNameSnapshot,
                pl.ClientIp,
                pl.PrintedAt))
            .ToListAsync(ct);

        var moveRows = await moveQuery
            .OrderByDescending(sm => sm.CreatedAt)
            .Take(200)
            .Select(sm => new LabelScanReportRowDto(
                sm.Id,
                sm.BranchId,
                sm.BranchId.ToString(),
                sm.Tag != null ? sm.Tag.TagNo : string.Empty,
                sm.MovementType.ToString(),
                sm.Reason,
                sm.Reason,
                sm.UserNameSnapshot,
                sm.ClientIp,
                sm.CreatedAt))
            .ToListAsync(ct);

        var combined = printRows.Concat(moveRows).OrderByDescending(r => r.Timestamp).ToList();

        if (!string.IsNullOrWhiteSpace(filter.EventType))
        {
            var evt = filter.EventType.Trim();
            combined = combined.Where(r => r.EventType.Equals(evt, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        var total = combined.Count;
        var paged = combined.Skip(offset).Take(pageSize).ToList();
        var pages = (int)Math.Ceiling((double)total / pageSize);

        return new PagedResult<LabelScanReportRowDto>(paged, total, page, pageSize, pages);
    }

    public async Task<PagedResult<OverallReportRowDto>> GetOverallReportAsync(OverallReportFilter filter, CancellationToken ct = default)
    {
        filter ??= new OverallReportFilter();
        var branchId = ResolveEffectiveBranchId(filter.BranchId, filter.CrossBranch);
        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);
        var offset = (page - 1) * pageSize;

        var query = _context.StockMovements
            .IgnoreQueryFilters()
            .AsQueryable();

        if (branchId.HasValue)
        {
            query = query.Where(sm => sm.BranchId == branchId.Value);
        }

        var rawMovements = await query
            .Select(sm => new { sm.CreatedAt.Date, sm.MaterialCode, sm.MovementType, sm.Qty })
            .ToListAsync(ct);

        List<OverallReportRowDto> summary;

        var groupBy = string.IsNullOrWhiteSpace(filter.GroupBy) ? "date" : filter.GroupBy.Trim();
        if (string.Equals(groupBy, "material", StringComparison.OrdinalIgnoreCase))
        {
            summary = rawMovements
                .GroupBy(sm => sm.MaterialCode)
                .Select(g => new OverallReportRowDto(
                    g.Key,
                    g.Where(x => x.MovementType == StockMovementType.In).Sum(x => x.Qty),
                    g.Where(x => x.MovementType == StockMovementType.Out).Sum(x => x.Qty),
                    0,
                    g.Count()))
                .OrderBy(s => s.GroupKey)
                .ToList();
        }
        else
        {
            // Default: Group by Date
            summary = rawMovements
                .GroupBy(x => x.Date.ToString("yyyy-MM-dd"))
                .Select(g => new OverallReportRowDto(
                    g.Key,
                    g.Where(x => x.MovementType == StockMovementType.In).Sum(x => x.Qty),
                    g.Where(x => x.MovementType == StockMovementType.Out).Sum(x => x.Qty),
                    0,
                    g.Count()))
                .OrderByDescending(s => s.GroupKey)
                .ToList();
        }

        var total = summary.Count;
        var paged = summary.Skip(offset).Take(pageSize).ToList();
        var pages = (int)Math.Ceiling((double)total / pageSize);

        return new PagedResult<OverallReportRowDto>(paged, total, page, pageSize, pages);
    }

    public async Task<PagedResult<AcceptRejectReportRowDto>> GetAcceptRejectReportAsync(AcceptRejectReportFilter filter, CancellationToken ct = default)
    {
        filter ??= new AcceptRejectReportFilter();
        var branchId = ResolveEffectiveBranchId(filter.BranchId, filter.CrossBranch);
        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);
        var offset = (page - 1) * pageSize;

        var query = _context.StockMovements
            .IgnoreQueryFilters()
            .Include(sm => sm.Tag)
            .Where(sm => sm.MovementType == StockMovementType.Accept || sm.MovementType == StockMovementType.Reject);

        if (branchId.HasValue) query = query.Where(sm => sm.BranchId == branchId.Value);

        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            var st = filter.Status.Trim();
            if (st.Equals("Accept", StringComparison.OrdinalIgnoreCase))
                query = query.Where(sm => sm.MovementType == StockMovementType.Accept);
            else if (st.Equals("Reject", StringComparison.OrdinalIgnoreCase))
                query = query.Where(sm => sm.MovementType == StockMovementType.Reject);
        }

        if (!string.IsNullOrWhiteSpace(filter.MaterialCode))
        {
            var mat = filter.MaterialCode.Trim();
            query = query.Where(sm => sm.MaterialCode == mat);
        }

        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(sm => sm.CreatedAt)
            .Skip(offset)
            .Take(pageSize)
            .Select(sm => new AcceptRejectReportRowDto(
                sm.Id,
                sm.BranchId,
                sm.BranchId.ToString(),
                sm.Tag != null ? sm.Tag.TagNo : string.Empty,
                sm.MaterialCode,
                null,
                sm.MovementType.ToString(),
                sm.Qty,
                sm.Reason,
                sm.UserNameSnapshot,
                sm.ClientIp,
                sm.CreatedAt))
            .ToListAsync(ct);

        var pages = (int)Math.Ceiling((double)total / pageSize);
        return new PagedResult<AcceptRejectReportRowDto>(rows, total, page, pageSize, pages);
    }

    public async Task<PagedResult<LocationTransferReportRowDto>> GetLocationTransferReportAsync(LocationTransferReportFilter filter, CancellationToken ct = default)
    {
        filter ??= new LocationTransferReportFilter();
        var branchId = ResolveEffectiveBranchId(filter.BranchId, filter.CrossBranch);
        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);
        var offset = (page - 1) * pageSize;

        var query = _context.StockMovements
            .IgnoreQueryFilters()
            .Include(sm => sm.Tag)
            .Include(sm => sm.Location)
            .Where(sm => sm.MovementType == StockMovementType.Transfer);

        if (branchId.HasValue) query = query.Where(sm => sm.BranchId == branchId.Value);

        if (!string.IsNullOrWhiteSpace(filter.TagNo))
        {
            var tagNo = filter.TagNo.Trim();
            query = query.Where(sm => sm.Tag != null && sm.Tag.TagNo == tagNo);
        }

        if (!string.IsNullOrWhiteSpace(filter.MaterialCode))
        {
            var mat = filter.MaterialCode.Trim();
            query = query.Where(sm => sm.MaterialCode == mat);
        }

        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(sm => sm.CreatedAt)
            .Skip(offset)
            .Take(pageSize)
            .Select(sm => new LocationTransferReportRowDto(
                sm.Id,
                sm.BranchId,
                sm.BranchId.ToString(),
                sm.Tag != null ? sm.Tag.TagNo : string.Empty,
                sm.MaterialCode,
                null,
                sm.Qty,
                null,
                sm.Location != null ? sm.Location.Code : "Unknown",
                sm.Reason,
                sm.UserNameSnapshot,
                sm.ClientIp,
                sm.CreatedAt))
            .ToListAsync(ct);

        var pages = (int)Math.Ceiling((double)total / pageSize);
        return new PagedResult<LocationTransferReportRowDto>(rows, total, page, pageSize, pages);
    }

    public async Task<DashboardLiveMetricsDto> GetDashboardLiveMetricsAsync(int? branchId, bool crossBranch = false, CancellationToken ct = default)
    {
        var effectiveBranchId = ResolveEffectiveBranchId(branchId, crossBranch);

        var grnQuery = _context.GrnApiResponses.IgnoreQueryFilters().AsQueryable();
        var tagQuery = _context.Tags.IgnoreQueryFilters().AsQueryable();
        var stockRecordQuery = _context.StockRecords.IgnoreQueryFilters().AsQueryable();
        var movementQuery = _context.StockMovements.IgnoreQueryFilters().Include(sm => sm.Tag).Include(sm => sm.Location).AsQueryable();

        if (effectiveBranchId.HasValue)
        {
            grnQuery = grnQuery.Where(g => g.BranchId == effectiveBranchId.Value);
            tagQuery = tagQuery.Where(t => t.BranchId == effectiveBranchId.Value);
            stockRecordQuery = stockRecordQuery.Where(sr => sr.BranchId == effectiveBranchId.Value);
            movementQuery = movementQuery.Where(sm => sm.BranchId == effectiveBranchId.Value);
        }

        var totalRegistered = await grnQuery.CountAsync(ct);
        var pendingInward = await tagQuery.Where(t => t.Status == TagStatus.Printed).CountAsync(ct);
        var totalInStock = await tagQuery.Where(t => t.Status == TagStatus.InStock && t.RemainingQty > 0).CountAsync(ct);
        var totalInStockUnits = await stockRecordQuery.Where(sr => sr.Qty > 0).SumAsync(sr => (int?)sr.Qty, ct) ?? 0;
        var rejectedCount = await tagQuery.Where(t => t.Status == TagStatus.Rejected).CountAsync(ct);
        var activeAlerts = rejectedCount + pendingInward;

        var recentMovements = await movementQuery
            .OrderByDescending(sm => sm.CreatedAt)
            .Take(10)
            .Select(sm => new RecentTransactionDto(
                sm.Id,
                sm.BranchId,
                sm.BranchId.ToString(),
                sm.Tag != null ? sm.Tag.TagNo : string.Empty,
                sm.MovementType.ToString(),
                sm.Qty,
                sm.Location != null ? sm.Location.Code : null,
                sm.UserNameSnapshot,
                sm.CreatedAt))
            .ToListAsync(ct);

        return new DashboardLiveMetricsDto(
            totalRegistered,
            pendingInward,
            totalInStock,
            totalInStockUnits,
            rejectedCount,
            activeAlerts,
            recentMovements);
    }
}
