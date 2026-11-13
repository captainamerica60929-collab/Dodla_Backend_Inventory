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

            var dataSql = $@"SELECT sm.Id, sm.BranchId, b.Code AS BranchCode, t.TagNo,
                       COALESCE(sm.MaterialCode, grn.MaterialCode, m.Code, '') AS MaterialCode,
                       COALESCE(m.Name, grn.PartName, '') AS PartName,
                       l.Code AS LocationCode, sm.Qty,
                       t.RemainingQty,
                       sm.CreatedAt AS IssuedAt,
                       sm.InwardAt AS OriginalInwardAt,
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
            .Include(sm => sm.Tag)
                .ThenInclude(t => t.GrnApiResponse)
            .Include(sm => sm.Location)
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
                !string.IsNullOrEmpty(sm.MaterialCode) ? sm.MaterialCode : (sm.Tag != null && sm.Tag.GrnApiResponse != null ? sm.Tag.GrnApiResponse.MaterialCode ?? "" : ""),
                sm.Tag != null && sm.Tag.GrnApiResponse != null ? sm.Tag.GrnApiResponse.PartName : null,
                sm.Location != null ? sm.Location.Code : null,
                sm.Qty,
                sm.Tag != null ? sm.Tag.RemainingQty : 0,
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

            var sqlWhere = @"WHERE sr.Qty > 0 AND (t.Status = 'InStock' OR t.Status = '2')
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
                       COALESCE(m.Name, grn.PartName, '') AS PartName, sr.LocationId, l.Code AS LocationCode, l.Zone AS LocationZone,
                       COUNT(DISTINCT sr.TagId) AS TotalTags,
                       COUNT(DISTINCT sr.TagId) AS ActiveTags,
                       SUM(sr.Qty) AS TotalInStockQty,
                       SUM(sr.Qty) AS TotalStockQty,
                       MIN(sr.InwardAt) AS OldestInwardAt, MAX(sr.InwardAt) AS NewestInwardAt
                FROM StockRecords sr
                INNER JOIN Tags t ON sr.TagId = t.Id
                INNER JOIN Branches b ON sr.BranchId = b.Id
                INNER JOIN Locations l ON sr.LocationId = l.Id
                LEFT JOIN Materials m ON sr.MaterialCode = m.Code
                LEFT JOIN GrnApiResponses grn ON t.GrnApiResponseId = grn.Id
                {sqlWhere}
                GROUP BY sr.BranchId, b.Code, sr.MaterialCode, COALESCE(m.Name, grn.PartName, ''), sr.LocationId, l.Code, l.Zone
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
                .ThenInclude(t => t.GrnApiResponse)
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
                g.Select(x => x.Tag != null && x.Tag.GrnApiResponse != null ? x.Tag.GrnApiResponse.PartName : null).FirstOrDefault(p => !string.IsNullOrEmpty(p)),
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

        var filterEventType = !string.IsNullOrWhiteSpace(filter.ActionType)
            ? filter.ActionType.Trim()
            : (!string.IsNullOrWhiteSpace(filter.EventType) ? filter.EventType.Trim() : null);

        var tagNo = string.IsNullOrWhiteSpace(filter.TagNo) ? null : filter.TagNo.Trim();
        var searchPattern = string.IsNullOrWhiteSpace(filter.Search) ? null : $"%{filter.Search.Trim()}%";
        var fromDate = filter.FromDate;
        var toDateExclusive = filter.ToDate.HasValue ? filter.ToDate.Value.Date.AddDays(1) : (DateTime?)null;

        if (_context.Database.IsRelational())
        {
            var conn = _context.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

            var sqlWherePrint = @"WHERE (@BranchId IS NULL OR t.BranchId = @BranchId)
                AND (@TagNo IS NULL OR t.TagNo = @TagNo)
                AND (@FromDate IS NULL OR pl.PrintedAt >= @FromDate)
                AND (@ToDateExclusive IS NULL OR pl.PrintedAt < @ToDateExclusive)";

            var sqlWhereMove = @"WHERE (@BranchId IS NULL OR sm.BranchId = @BranchId)
                AND (@TagNo IS NULL OR (t.TagNo IS NOT NULL AND t.TagNo = @TagNo))
                AND (@FromDate IS NULL OR sm.CreatedAt >= @FromDate)
                AND (@ToDateExclusive IS NULL OR sm.CreatedAt < @ToDateExclusive)";

            var unifiedCte = $@"
                WITH UnifiedAudit AS (
                    SELECT pl.Id, t.BranchId, b.Code AS BranchCode, t.TagNo,
                           COALESCE(
                               NULLIF(LTRIM(RTRIM(grn.MaterialCode)), ''),
                               NULLIF(LTRIM(RTRIM(sr_mat.MaterialCode)), ''),
                               NULLIF(LTRIM(RTRIM(sm_mat.MaterialCode)), ''),
                               NULLIF(LTRIM(RTRIM(m.Code)), ''),
                               '—'
                           ) AS MaterialCode,
                           t.Qty,
                           CASE WHEN pl.Kind = 'Print' OR pl.Kind = '0' THEN 'Printed' ELSE 'Reprinted' END AS EventType,
                           COALESCE(pl.ClientIp, '127.0.0.1') AS PrinterIp,
                           NULL AS Details,
                           pl.Reason,
                           pl.UserNameSnapshot AS OperatorName,
                           pl.ClientIp,
                           pl.PrintedAt AS Timestamp
                    FROM TagPrintLogs pl
                    INNER JOIN Tags t ON pl.TagId = t.Id
                    INNER JOIN Branches b ON t.BranchId = b.Id
                    LEFT JOIN GrnApiResponses grn ON t.GrnApiResponseId = grn.Id
                    LEFT JOIN Materials m ON grn.MaterialCode = m.Code
                    OUTER APPLY (
                        SELECT TOP 1 sr.MaterialCode
                        FROM StockRecords sr
                        WHERE sr.TagId = t.Id AND sr.MaterialCode IS NOT NULL AND sr.MaterialCode <> ''
                    ) sr_mat
                    OUTER APPLY (
                        SELECT TOP 1 sm.MaterialCode
                        FROM StockMovements sm
                        WHERE sm.TagId = t.Id AND sm.MaterialCode IS NOT NULL AND sm.MaterialCode <> ''
                    ) sm_mat
                    {sqlWherePrint}

                    UNION ALL

                    SELECT sm.Id, sm.BranchId, b.Code AS BranchCode, COALESCE(t.TagNo, '—') AS TagNo,
                           COALESCE(
                               NULLIF(LTRIM(RTRIM(sm.MaterialCode)), ''),
                               NULLIF(LTRIM(RTRIM(grn.MaterialCode)), ''),
                               NULLIF(LTRIM(RTRIM(sr_mat2.MaterialCode)), ''),
                               NULLIF(LTRIM(RTRIM(m.Code)), ''),
                               '—'
                           ) AS MaterialCode,
                           sm.Qty,
                           'Scanned' AS EventType,
                           COALESCE(sm.ClientIp, '127.0.0.1') AS PrinterIp,
                           sm.Reason AS Details,
                           sm.Reason,
                           sm.UserNameSnapshot AS OperatorName,
                           sm.ClientIp,
                           sm.CreatedAt AS Timestamp
                    FROM StockMovements sm
                    INNER JOIN Branches b ON sm.BranchId = b.Id
                    LEFT JOIN Tags t ON sm.TagId = t.Id
                    LEFT JOIN GrnApiResponses grn ON t.GrnApiResponseId = grn.Id
                    LEFT JOIN Materials m ON sm.MaterialCode = m.Code
                    OUTER APPLY (
                        SELECT TOP 1 sr.MaterialCode
                        FROM StockRecords sr
                        WHERE sr.TagId = sm.TagId AND sr.MaterialCode IS NOT NULL AND sr.MaterialCode <> ''
                    ) sr_mat2
                    {sqlWhereMove}
                )";

            var countSql = $@"{unifiedCte}
                SELECT COUNT(1) FROM UnifiedAudit
                WHERE (@EventType IS NULL OR EventType = @EventType)
                  AND (@SearchPattern IS NULL OR TagNo LIKE @SearchPattern OR MaterialCode LIKE @SearchPattern OR OperatorName LIKE @SearchPattern)";

            var dataSql = $@"{unifiedCte}
                SELECT * FROM UnifiedAudit
                WHERE (@EventType IS NULL OR EventType = @EventType)
                  AND (@SearchPattern IS NULL OR TagNo LIKE @SearchPattern OR MaterialCode LIKE @SearchPattern OR OperatorName LIKE @SearchPattern)
                ORDER BY Timestamp DESC, Id DESC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

            var p = new
            {
                BranchId = branchId,
                TagNo = tagNo,
                EventType = filterEventType,
                SearchPattern = searchPattern,
                FromDate = fromDate,
                ToDateExclusive = toDateExclusive,
                Offset = offset,
                PageSize = pageSize
            };

            var totalCount = await conn.ExecuteScalarAsync<int>(countSql, p);
            var items = (await conn.QueryAsync<LabelScanReportRowDto>(dataSql, p)).ToList();
            var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

            return new PagedResult<LabelScanReportRowDto>(items, totalCount, page, pageSize, totalPages);
        }

        // Combines TagPrintLogs and scan movements for In-Memory / non-relational fallback
        var printQuery = _context.TagPrintLogs
            .IgnoreQueryFilters()
            .Include(pl => pl.Tag)
                .ThenInclude(t => t!.GrnApiResponse)
            .Where(pl => pl.Tag != null)
            .AsQueryable();

        var moveQuery = _context.StockMovements
            .IgnoreQueryFilters()
            .Include(sm => sm.Tag)
                .ThenInclude(t => t.GrnApiResponse)
            .Where(sm => sm.Tag != null)
            .AsQueryable();

        if (branchId.HasValue)
        {
            printQuery = printQuery.Where(pl => pl.Tag!.BranchId == branchId.Value);
            moveQuery = moveQuery.Where(sm => sm.BranchId == branchId.Value);
        }

        if (!string.IsNullOrWhiteSpace(tagNo))
        {
            printQuery = printQuery.Where(pl => pl.Tag!.TagNo == tagNo);
            moveQuery = moveQuery.Where(sm => sm.Tag.TagNo == tagNo);
        }

        if (fromDate.HasValue)
        {
            printQuery = printQuery.Where(pl => pl.PrintedAt >= fromDate.Value);
            moveQuery = moveQuery.Where(sm => sm.CreatedAt >= fromDate.Value);
        }

        if (toDateExclusive.HasValue)
        {
            printQuery = printQuery.Where(pl => pl.PrintedAt < toDateExclusive.Value);
            moveQuery = moveQuery.Where(sm => sm.CreatedAt < toDateExclusive.Value);
        }

        var printLogsList = await printQuery
            .OrderByDescending(pl => pl.PrintedAt)
            .Take(200)
            .ToListAsync(ct);

        var moveList = await moveQuery
            .OrderByDescending(sm => sm.CreatedAt)
            .Take(200)
            .ToListAsync(ct);

        var tagIds = printLogsList.Select(pl => pl.TagId)
            .Concat(moveList.Select(m => m.TagId))
            .Distinct()
            .ToList();

        var srList = await _context.StockRecords.IgnoreQueryFilters()
            .Where(sr => tagIds.Contains(sr.TagId) && !string.IsNullOrWhiteSpace(sr.MaterialCode))
            .ToListAsync(ct);

        var stockRecordsMap = srList
            .GroupBy(sr => sr.TagId)
            .ToDictionary(g => g.Key, g => g.First().MaterialCode.Trim());

        var printRows = printLogsList
            .Select(pl =>
            {
                var matCode = !string.IsNullOrWhiteSpace(pl.Tag?.GrnApiResponse?.MaterialCode)
                    ? pl.Tag!.GrnApiResponse!.MaterialCode!.Trim()
                    : (stockRecordsMap.TryGetValue(pl.TagId, out var srMat) && !string.IsNullOrWhiteSpace(srMat)
                        ? srMat
                        : "—");

                return new LabelScanReportRowDto(
                    pl.Id,
                    pl.Tag != null ? pl.Tag.BranchId : 0,
                    pl.Tag != null ? pl.Tag.BranchId.ToString() : "0",
                    pl.Tag != null ? pl.Tag.TagNo : string.Empty,
                    matCode,
                    pl.Tag != null ? pl.Tag.Qty : 0,
                    pl.Kind == TagPrintKind.Print ? "Printed" : "Reprinted",
                    pl.ClientIp ?? "127.0.0.1",
                    null,
                    pl.Reason,
                    pl.UserNameSnapshot,
                    pl.ClientIp,
                    pl.PrintedAt);
            })
            .ToList();

        var moveRows = moveList
            .Select(sm =>
            {
                var matCode = !string.IsNullOrWhiteSpace(sm.MaterialCode)
                    ? sm.MaterialCode.Trim()
                    : (!string.IsNullOrWhiteSpace(sm.Tag?.GrnApiResponse?.MaterialCode)
                        ? sm.Tag!.GrnApiResponse!.MaterialCode!.Trim()
                        : (stockRecordsMap.TryGetValue(sm.TagId, out var srMat) && !string.IsNullOrWhiteSpace(srMat)
                            ? srMat
                            : "—"));

                return new LabelScanReportRowDto(
                    sm.Id,
                    sm.BranchId,
                    sm.BranchId.ToString(),
                    sm.Tag != null ? sm.Tag.TagNo : string.Empty,
                    matCode,
                    sm.Qty,
                    "Scanned",
                    sm.ClientIp ?? "127.0.0.1",
                    sm.Reason,
                    sm.Reason,
                    sm.UserNameSnapshot,
                    sm.ClientIp,
                    sm.CreatedAt);
            })
            .ToList();

        var combined = printRows.Concat(moveRows).OrderByDescending(r => r.Timestamp).ToList();

        if (!string.IsNullOrWhiteSpace(filterEventType))
        {
            combined = combined.Where(r => r.EventType.Equals(filterEventType, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            combined = combined.Where(r =>
                r.TagNo.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                r.MaterialCode.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                (r.OperatorName != null && r.OperatorName.Contains(term, StringComparison.OrdinalIgnoreCase))
            ).ToList();
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

        var searchPattern = string.IsNullOrWhiteSpace(filter.Search) ? null : $"%{filter.Search.Trim()}%";
        var materialFilter = string.IsNullOrWhiteSpace(filter.MaterialCode) ? null : filter.MaterialCode.Trim();
        var fromDate = filter.FromDate;
        var toDateExclusive = filter.ToDate.HasValue ? filter.ToDate.Value.Date.AddDays(1) : (DateTime?)null;

        if (_context.Database.IsRelational())
        {
            var conn = _context.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

            var overallCte = $@"
                WITH AllMaterials AS (
                    SELECT DISTINCT
                        COALESCE(
                            NULLIF(LTRIM(RTRIM(sm.MaterialCode)), ''),
                            NULLIF(LTRIM(RTRIM(grn.MaterialCode)), ''),
                            'UNKNOWN'
                        ) AS MatCode,
                        sm.BranchId
                    FROM StockMovements sm
                    LEFT JOIN Tags t ON sm.TagId = t.Id
                    LEFT JOIN GrnApiResponses grn ON t.GrnApiResponseId = grn.Id
                    WHERE (@BranchId IS NULL OR sm.BranchId = @BranchId)
                      AND (@FromDate IS NULL OR sm.CreatedAt >= @FromDate)
                      AND (@ToDateExclusive IS NULL OR sm.CreatedAt < @ToDateExclusive)

                    UNION

                    SELECT DISTINCT
                        NULLIF(LTRIM(RTRIM(sr.MaterialCode)), '') AS MatCode,
                        sr.BranchId
                    FROM StockRecords sr
                    WHERE (@BranchId IS NULL OR sr.BranchId = @BranchId)
                      AND sr.MaterialCode IS NOT NULL AND sr.MaterialCode <> ''
                ),
                DistinctMats AS (
                    SELECT DISTINCT MatCode
                    FROM AllMaterials
                    WHERE MatCode IS NOT NULL AND MatCode <> ''
                      AND (@MaterialFilter IS NULL OR MatCode = @MaterialFilter)
                ),
                Aggregated AS (
                    SELECT 
                        dm.MatCode AS GroupKey,
                        dm.MatCode AS MaterialCode,
                        COALESCE(
                            NULLIF(LTRIM(RTRIM(MAX(m.Name))), ''),
                            NULLIF(LTRIM(RTRIM(MAX(grn_direct.PartName))), ''),
                            NULLIF(LTRIM(RTRIM(MAX(grn_direct.Description))), ''),
                            NULLIF(LTRIM(RTRIM(MAX(grn_tag.PartName))), ''),
                            NULLIF(LTRIM(RTRIM(MAX(grn_tag.Description))), ''),
                            NULLIF(LTRIM(RTRIM(dm.MatCode)), ''),
                            'Standard Item'
                        ) AS PartName,
                        COALESCE(SUM(CASE WHEN sm.MovementType = 'In' OR sm.MovementType = '1' THEN sm.Qty ELSE 0 END), 0) AS TotalInwardQty,
                        COALESCE(SUM(CASE WHEN sm.MovementType = 'Out' OR sm.MovementType = '2' THEN sm.Qty ELSE 0 END), 0) AS TotalOutwardQty,
                        COALESCE((
                            SELECT SUM(sr.Qty) 
                            FROM StockRecords sr 
                            WHERE sr.MaterialCode = dm.MatCode 
                              AND (@BranchId IS NULL OR sr.BranchId = @BranchId)
                        ), 0) AS CurrentStockQty,
                        COUNT(sm.Id) AS TotalMovements,
                        MAX(sm.CreatedAt) AS LastMovementAt
                    FROM DistinctMats dm
                    LEFT JOIN StockMovements sm 
                        ON dm.MatCode = COALESCE(NULLIF(LTRIM(RTRIM(sm.MaterialCode)), ''), 'UNKNOWN')
                        AND (@BranchId IS NULL OR sm.BranchId = @BranchId)
                        AND (@FromDate IS NULL OR sm.CreatedAt >= @FromDate)
                        AND (@ToDateExclusive IS NULL OR sm.CreatedAt < @ToDateExclusive)
                    LEFT JOIN Tags t ON sm.TagId = t.Id
                    LEFT JOIN GrnApiResponses grn_tag ON t.GrnApiResponseId = grn_tag.Id
                    LEFT JOIN Materials m ON dm.MatCode = m.Code
                    OUTER APPLY (
                        SELECT TOP 1 g.PartName, g.Description
                        FROM GrnApiResponses g
                        WHERE g.MaterialCode = dm.MatCode AND (@BranchId IS NULL OR g.BranchId = @BranchId)
                        ORDER BY g.Id DESC
                    ) grn_direct
                    GROUP BY dm.MatCode
                )";

            var countSql = $@"{overallCte}
                SELECT COUNT(1) FROM Aggregated
                WHERE (@SearchPattern IS NULL OR MaterialCode LIKE @SearchPattern OR PartName LIKE @SearchPattern)";

            var dataSql = $@"{overallCte}
                SELECT * FROM Aggregated
                WHERE (@SearchPattern IS NULL OR MaterialCode LIKE @SearchPattern OR PartName LIKE @SearchPattern)
                ORDER BY MaterialCode
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

            var p = new
            {
                BranchId = branchId,
                MaterialFilter = materialFilter,
                SearchPattern = searchPattern,
                FromDate = fromDate,
                ToDateExclusive = toDateExclusive,
                Offset = offset,
                PageSize = pageSize
            };

            var totalCount = await conn.ExecuteScalarAsync<int>(countSql, p);
            var items = (await conn.QueryAsync<OverallReportRowDto>(dataSql, p)).ToList();
            var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

            return new PagedResult<OverallReportRowDto>(items, totalCount, page, pageSize, totalPages);
        }

        var query = _context.StockMovements
            .IgnoreQueryFilters()
            .Include(sm => sm.Tag)
                .ThenInclude(t => t.GrnApiResponse)
            .AsQueryable();

        if (branchId.HasValue)
        {
            query = query.Where(sm => sm.BranchId == branchId.Value);
        }

        if (fromDate.HasValue)
        {
            query = query.Where(sm => sm.CreatedAt >= fromDate.Value);
        }

        if (toDateExclusive.HasValue)
        {
            query = query.Where(sm => sm.CreatedAt < toDateExclusive.Value);
        }

        var movements = await query.ToListAsync(ct);
        var stockRecords = await _context.StockRecords.IgnoreQueryFilters()
            .Where(sr => !branchId.HasValue || sr.BranchId == branchId.Value)
            .ToListAsync(ct);

        var materials = await _context.Materials.ToListAsync(ct);
        var matMap = materials
            .Where(m => !string.IsNullOrWhiteSpace(m.Code))
            .ToDictionary(m => m.Code.Trim(), m => m.Name.Trim(), StringComparer.OrdinalIgnoreCase);

        var grnResponses = await _context.GrnApiResponses.ToListAsync(ct);
        var grnMap = grnResponses
            .Where(g => !string.IsNullOrWhiteSpace(g.MaterialCode) && (!string.IsNullOrWhiteSpace(g.PartName) || !string.IsNullOrWhiteSpace(g.Description)))
            .GroupBy(g => g.MaterialCode!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => !string.IsNullOrWhiteSpace(x.PartName) ? x.PartName.Trim() : x.Description?.Trim()).FirstOrDefault(p => !string.IsNullOrWhiteSpace(p)) ?? string.Empty,
                StringComparer.OrdinalIgnoreCase);

        var allMatCodes = movements
            .Select(sm => !string.IsNullOrWhiteSpace(sm.MaterialCode) ? sm.MaterialCode.Trim() : (sm.Tag?.GrnApiResponse?.MaterialCode?.Trim() ?? string.Empty))
            .Concat(stockRecords.Select(sr => sr.MaterialCode.Trim()))
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!string.IsNullOrWhiteSpace(materialFilter))
        {
            allMatCodes = allMatCodes.Where(c => c.Equals(materialFilter, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        var summary = allMatCodes
            .Select(matCode =>
            {
                var matMoves = movements.Where(m =>
                    string.Equals(matCode, !string.IsNullOrWhiteSpace(m.MaterialCode) ? m.MaterialCode.Trim() : (m.Tag?.GrnApiResponse?.MaterialCode?.Trim() ?? ""), StringComparison.OrdinalIgnoreCase)
                ).ToList();

                var partName = matMap.TryGetValue(matCode, out var mName) && !string.IsNullOrWhiteSpace(mName)
                    ? mName
                    : (grnMap.TryGetValue(matCode, out var gName) && !string.IsNullOrWhiteSpace(gName)
                        ? gName
                        : (matMoves.Select(x => x.Tag?.GrnApiResponse?.PartName?.Trim() ?? x.Tag?.GrnApiResponse?.Description?.Trim()).FirstOrDefault(p => !string.IsNullOrWhiteSpace(p)) ?? matCode));

                var inQty = matMoves.Where(x => x.MovementType == StockMovementType.In).Sum(x => x.Qty);
                var outQty = matMoves.Where(x => x.MovementType == StockMovementType.Out).Sum(x => x.Qty);
                var curStock = stockRecords.Where(sr => sr.MaterialCode.Trim().Equals(matCode, StringComparison.OrdinalIgnoreCase)).Sum(sr => sr.Qty);
                var lastMove = matMoves.Max(x => (DateTime?)x.CreatedAt);

                return new OverallReportRowDto(
                    matCode,
                    matCode,
                    partName,
                    inQty,
                    outQty,
                    curStock > 0 ? curStock : Math.Max(0, inQty - outQty),
                    matMoves.Count,
                    lastMove);
            })
            .OrderBy(s => s.MaterialCode)
            .ToList();

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            summary = summary.Where(s =>
                s.MaterialCode.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                (s.PartName != null && s.PartName.Contains(term, StringComparison.OrdinalIgnoreCase))
            ).ToList();
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

        if (_context.Database.IsRelational())
        {
            var conn = _context.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

            var sqlWhere = @"WHERE sm.MovementType IN ('Accept', 'Reject')
                AND (@BranchId IS NULL OR sm.BranchId = @BranchId)
                AND (@MaterialCode IS NULL OR sm.MaterialCode = @MaterialCode)";

            if (!string.IsNullOrWhiteSpace(filter.Status))
            {
                var st = filter.Status.Trim();
                if (st.StartsWith("Accept", StringComparison.OrdinalIgnoreCase))
                    sqlWhere += " AND sm.MovementType = 'Accept'";
                else if (st.StartsWith("Reject", StringComparison.OrdinalIgnoreCase))
                    sqlWhere += " AND sm.MovementType = 'Reject'";
            }

            var countSql = $@"SELECT COUNT(1)
                FROM StockMovements sm
                INNER JOIN Tags t ON sm.TagId = t.Id
                {sqlWhere}";

            var dataSql = $@"SELECT sm.Id, sm.BranchId, b.Code AS BranchCode, t.TagNo, sm.MaterialCode,
                       COALESCE(m.Name, grn.PartName, '') AS PartName,
                       CASE WHEN sm.MovementType = 'Accept' THEN 'Accepted' ELSE 'Rejected' END AS Status,
                       sm.Qty, sm.Reason,
                       sm.UserNameSnapshot AS InspectorName, sm.ClientIp,
                       sm.CreatedAt AS ProcessedAt
                FROM StockMovements sm
                INNER JOIN Tags t ON sm.TagId = t.Id
                INNER JOIN Branches b ON sm.BranchId = b.Id
                LEFT JOIN Materials m ON sm.MaterialCode = m.Code
                LEFT JOIN GrnApiResponses grn ON t.GrnApiResponseId = grn.Id
                {sqlWhere}
                ORDER BY sm.CreatedAt DESC, sm.Id DESC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

            var p = new
            {
                BranchId = branchId,
                MaterialCode = string.IsNullOrWhiteSpace(filter.MaterialCode) ? null : filter.MaterialCode.Trim(),
                Offset = offset,
                PageSize = pageSize
            };

            var totalCount = await conn.ExecuteScalarAsync<int>(countSql, p);
            var items = (await conn.QueryAsync<AcceptRejectReportRowDto>(dataSql, p)).ToList();
            var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

            return new PagedResult<AcceptRejectReportRowDto>(items, totalCount, page, pageSize, totalPages);
        }

        var query = _context.StockMovements
            .IgnoreQueryFilters()
            .Include(sm => sm.Tag)
                .ThenInclude(t => t.GrnApiResponse)
            .Where(sm => sm.MovementType == StockMovementType.Accept || sm.MovementType == StockMovementType.Reject);

        if (branchId.HasValue) query = query.Where(sm => sm.BranchId == branchId.Value);

        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            var st = filter.Status.Trim();
            if (st.StartsWith("Accept", StringComparison.OrdinalIgnoreCase))
                query = query.Where(sm => sm.MovementType == StockMovementType.Accept);
            else if (st.StartsWith("Reject", StringComparison.OrdinalIgnoreCase))
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
                sm.Tag != null && sm.Tag.GrnApiResponse != null ? sm.Tag.GrnApiResponse.PartName : null,
                sm.MovementType == StockMovementType.Accept ? "Accepted" : "Rejected",
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

        if (_context.Database.IsRelational())
        {
            var conn = _context.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

            var sqlWhere = @"WHERE sm.MovementType = 'Transfer'
                AND (@BranchId IS NULL OR sm.BranchId = @BranchId)
                AND (@TagNo IS NULL OR t.TagNo = @TagNo)
                AND (@MaterialCode IS NULL OR sm.MaterialCode = @MaterialCode)";

            var countSql = $@"SELECT COUNT(1)
                FROM StockMovements sm
                INNER JOIN Tags t ON sm.TagId = t.Id
                {sqlWhere}";

            var dataSql = $@"SELECT sm.Id, sm.BranchId, b.Code AS BranchCode, t.TagNo, sm.MaterialCode,
                       COALESCE(m.Name, grn.PartName, '') AS PartName,
                       sm.Qty,
                       COALESCE(
                           l_from.Code,
                           CASE WHEN CHARINDEX('Transferred from ', sm.Reason) > 0 AND CHARINDEX(' to ', sm.Reason) > CHARINDEX('Transferred from ', sm.Reason)
                                THEN SUBSTRING(sm.Reason, CHARINDEX('Transferred from ', sm.Reason) + 17, CHARINDEX(' to ', sm.Reason) - (CHARINDEX('Transferred from ', sm.Reason) + 17))
                                ELSE NULL END
                       ) AS FromLocationCode,
                       COALESCE(l_to.Code, 'Unknown') AS ToLocationCode,
                       sm.Reason,
                       sm.UserNameSnapshot AS OperatorName,
                       sm.ClientIp,
                       sm.CreatedAt AS TransferredAt
                FROM StockMovements sm
                INNER JOIN Tags t ON sm.TagId = t.Id
                INNER JOIN Branches b ON sm.BranchId = b.Id
                LEFT JOIN Locations l_to ON sm.LocationId = l_to.Id
                OUTER APPLY (
                    SELECT TOP 1 prevLoc.Code
                    FROM StockMovements prevSm
                    INNER JOIN Locations prevLoc ON prevSm.LocationId = prevLoc.Id
                    WHERE prevSm.TagId = sm.TagId AND prevSm.Id < sm.Id AND prevSm.LocationId IS NOT NULL
                    ORDER BY prevSm.Id DESC
                ) l_from
                LEFT JOIN Materials m ON sm.MaterialCode = m.Code
                LEFT JOIN GrnApiResponses grn ON t.GrnApiResponseId = grn.Id
                {sqlWhere}
                ORDER BY sm.CreatedAt DESC, sm.Id DESC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

            var p = new
            {
                BranchId = branchId,
                TagNo = string.IsNullOrWhiteSpace(filter.TagNo) ? null : filter.TagNo.Trim(),
                MaterialCode = string.IsNullOrWhiteSpace(filter.MaterialCode) ? null : filter.MaterialCode.Trim(),
                Offset = offset,
                PageSize = pageSize
            };

            var totalCount = await conn.ExecuteScalarAsync<int>(countSql, p);
            var items = (await conn.QueryAsync<LocationTransferReportRowDto>(dataSql, p)).ToList();
            var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

            return new PagedResult<LocationTransferReportRowDto>(items, totalCount, page, pageSize, totalPages);
        }

        var query = _context.StockMovements
            .IgnoreQueryFilters()
            .Include(sm => sm.Tag)
                .ThenInclude(t => t.GrnApiResponse)
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
        var movementList = await query
            .OrderByDescending(sm => sm.CreatedAt)
            .Skip(offset)
            .Take(pageSize)
            .ToListAsync(ct);

        var rows = movementList.Select(sm =>
        {
            string? fromLocationCode = null;
            if (!string.IsNullOrEmpty(sm.Reason) && sm.Reason.Contains("Transferred from ") && sm.Reason.Contains(" to "))
            {
                var start = sm.Reason.IndexOf("Transferred from ", StringComparison.Ordinal) + 17;
                var end = sm.Reason.IndexOf(" to ", start, StringComparison.Ordinal);
                if (end > start)
                {
                    fromLocationCode = sm.Reason.Substring(start, end - start).Trim();
                }
            }

            return new LocationTransferReportRowDto(
                sm.Id,
                sm.BranchId,
                sm.BranchId.ToString(),
                sm.Tag != null ? sm.Tag.TagNo : string.Empty,
                sm.MaterialCode,
                sm.Tag != null && sm.Tag.GrnApiResponse != null ? sm.Tag.GrnApiResponse.PartName : null,
                sm.Qty,
                fromLocationCode,
                sm.Location != null ? sm.Location.Code : "Unknown",
                sm.Reason,
                sm.UserNameSnapshot,
                sm.ClientIp,
                sm.CreatedAt);
        }).ToList();

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
