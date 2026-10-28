using Inventory.Application.Common.Interfaces;
using Inventory.Application.Stock.DTOs;
using Inventory.Application.Stock.Services;
using Inventory.Domain.Common;
using Inventory.Domain.Entities;
using Inventory.Domain.Enums;
using Inventory.Domain.Exceptions;
using Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Infrastructure.Services;

public class StockService : IStockService
{
    private readonly InventoryDbContext _context;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;

    public StockService(
        InventoryDbContext context,
        IClock clock,
        ICurrentUser currentUser)
    {
        _context = context;
        _clock = clock;
        _currentUser = currentUser;
    }

    public async Task<InwardResponseDto> InwardAsync(
        InwardScanRequestDto request,
        string? clientIp,
        CancellationToken ct = default)
    {
        if (request == null)
            throw new DomainException("Inward request payload is required.");

        var tagNo = request.TagNo?.Trim();
        var locationCode = request.LocationCode?.Trim();

        if (string.IsNullOrWhiteSpace(tagNo))
            throw new DomainException("Tag number is required.");

        if (string.IsNullOrWhiteSpace(locationCode))
            throw new DomainException("Location code is required.");

        var tag = await _context.Tags
            .Include(t => t.GrnApiResponse)
            .FirstOrDefaultAsync(t => t.TagNo == tagNo, ct)
            ?? throw new NotFoundException("Tag", tagNo);

        if (tag.Status == TagStatus.InStock)
            throw new DomainException($"Tag '{tagNo}' is already inwarded and in stock.");

        if (tag.Status == TagStatus.Issued)
            throw new DomainException($"Tag '{tagNo}' is currently in status 'Issued'.");

        if (tag.Status == TagStatus.Accepted || tag.Status == TagStatus.Rejected)
            throw new DomainException($"Tag '{tagNo}' is in status '{tag.Status}' and cannot be inwarded.");

        if (tag.Status == TagStatus.Void)
            throw new DomainException("Cannot inward a void tag.");

        if (tag.Status != TagStatus.Printed)
            throw new DomainException($"Tag '{tagNo}' is in status '{tag.Status}'. Only printed tags can be inwarded.");

        if (tag.RemainingQty <= 0)
            throw new DomainException($"Tag '{tagNo}' has no remaining quantity to inward.");

        var location = await _context.Locations
            .FirstOrDefaultAsync(l => l.Code == locationCode, ct)
            ?? throw new NotFoundException("Location", locationCode);

        if (!location.IsActive)
            throw new DomainException($"Location '{locationCode}' is inactive.");

        var currentUsed = await _context.StockRecords
            .Where(sr => sr.LocationId == location.Id)
            .SumAsync(sr => sr.Qty, ct);

        if (currentUsed + tag.RemainingQty > location.Capacity)
        {
            throw new DomainException(
                $"Location '{locationCode}' capacity ({location.Capacity}) exceeded. Currently used: {currentUsed}, attempting to inward: {tag.RemainingQty}.");
        }

        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? tx = null;
        if (_context.Database.IsRelational())
        {
            tx = await _context.Database.BeginTransactionAsync(ct);
        }

        try
        {
            var utcNow = _clock.UtcNow;
            var userSnapshot = await GetUserNameSnapshotAsync(ct);

            tag.Status = TagStatus.InStock;

            var stockRecord = new StockRecord
            {
                BranchId = tag.BranchId,
                TagId = tag.Id,
                LocationId = location.Id,
                MaterialCode = tag.GrnApiResponse?.MaterialCode ?? string.Empty,
                BatchNo = tag.GrnApiResponse?.BatchNo,
                Qty = tag.RemainingQty,
                InwardAt = utcNow
            };

            _context.StockRecords.Add(stockRecord);

            // Touch location timestamp to participate in optimistic concurrency check on bin capacity
            location.ModifiedAt = utcNow;

            var movement = new StockMovement
            {
                BranchId = tag.BranchId,
                TagId = tag.Id,
                LocationId = location.Id,
                MaterialCode = stockRecord.MaterialCode,
                MovementType = StockMovementType.In,
                Qty = tag.RemainingQty,
                InwardAt = utcNow,
                UserId = _currentUser.UserId,
                UserNameSnapshot = userSnapshot,
                ClientIp = clientIp
            };

            _context.StockMovements.Add(movement);

            await _context.SaveChangesAsync(ct);
            if (tx != null) await tx.CommitAsync(ct);

            return new InwardResponseDto(
                stockRecord.Id,
                tag.Id,
                tag.TagNo,
                location.Id,
                location.Code,
                stockRecord.MaterialCode,
                stockRecord.Qty,
                currentUsed + tag.RemainingQty,
                location.Capacity,
                stockRecord.InwardAt);
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

    public async Task<OutwardResponseDto> OutwardAsync(
        OutwardRequestDto request,
        string? clientIp,
        CancellationToken ct = default)
    {
        if (request == null)
            throw new DomainException("Outward issue request payload is required.");

        var tagNo = request.TagNo?.Trim();
        if (string.IsNullOrWhiteSpace(tagNo))
            throw new DomainException("Tag number is required.");

        if (request.Qty <= 0)
            throw new DomainException("Issue quantity must be greater than zero.");

        var tag = await _context.Tags
            .Include(t => t.GrnApiResponse)
            .FirstOrDefaultAsync(t => t.TagNo == tagNo, ct)
            ?? throw new NotFoundException("Tag", tagNo);

        if (tag.Status != TagStatus.InStock)
            throw new DomainException($"Tag '{tagNo}' is in status '{tag.Status}'. Only 'InStock' tags can be issued.");

        var stockRecord = await _context.StockRecords
            .Include(sr => sr.Location)
            .FirstOrDefaultAsync(sr => sr.TagId == tag.Id && sr.Qty > 0, ct)
            ?? throw new DomainException($"No active stock record found for tag '{tagNo}'.");

        if (request.Qty > stockRecord.Qty || request.Qty > tag.RemainingQty)
        {
            throw new DomainException(
                $"Requested quantity ({request.Qty}) exceeds available tag balance ({stockRecord.Qty}).");
        }

        // FIFO Check: ensure no strictly older stock record exists for this material with available stock (unless bypassed)
        if (!request.BypassFifo)
        {
            var olderStock = await _context.StockRecords
                .Include(sr => sr.Tag)
                .Where(sr => sr.MaterialCode == stockRecord.MaterialCode
                             && sr.Qty > 0
                             && sr.InwardAt < stockRecord.InwardAt
                             && sr.Id != stockRecord.Id)
                .OrderBy(sr => sr.InwardAt)
                .FirstOrDefaultAsync(ct);

            if (olderStock != null)
            {
                throw new DomainException(
                    $"FIFO violation: Older stock for material '{stockRecord.MaterialCode}' exists (Tag: {olderStock.Tag.TagNo}, InwardAt: {olderStock.InwardAt:yyyy-MM-dd HH:mm:ss} UTC). The oldest stock must be issued first.");
            }
        }

        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? tx = null;
        if (_context.Database.IsRelational())
        {
            tx = await _context.Database.BeginTransactionAsync(ct);
        }

        try
        {
            var utcNow = _clock.UtcNow;
            var userSnapshot = await GetUserNameSnapshotAsync(ct);

            // Deduct stock record and tag remaining balance
            stockRecord.Qty -= request.Qty;
            stockRecord.ModifiedAt = utcNow;

            tag.RemainingQty -= request.Qty;
            tag.Status = TagStatus.Issued; // Status transitions from InStock -> Issued

            var movement = new StockMovement
            {
                BranchId = tag.BranchId,
                TagId = tag.Id,
                LocationId = stockRecord.LocationId,
                MaterialCode = stockRecord.MaterialCode,
                MovementType = StockMovementType.Out,
                Qty = request.Qty,
                InwardAt = stockRecord.InwardAt,
                UserId = _currentUser.UserId,
                UserNameSnapshot = userSnapshot,
                ClientIp = clientIp
            };

            _context.StockMovements.Add(movement);

            await _context.SaveChangesAsync(ct);
            if (tx != null) await tx.CommitAsync(ct);

            return new OutwardResponseDto(
                tag.Id,
                tag.TagNo,
                stockRecord.MaterialCode,
                request.Qty,
                tag.RemainingQty,
                tag.Status.ToString(),
                stockRecord.LocationId,
                stockRecord.Location.Code,
                stockRecord.InwardAt);
        }
        catch (DbUpdateConcurrencyException)
        {
            if (tx != null) await tx.RollbackAsync(ct);
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

    public async Task<AcceptRejectResponseDto> AcceptAsync(
        AcceptRequestDto request,
        string? clientIp,
        CancellationToken ct = default)
    {
        if (request == null)
            throw new DomainException("Accept request payload is required.");

        var tagNo = request.TagNo?.Trim();
        if (string.IsNullOrWhiteSpace(tagNo))
            throw new DomainException("Tag number is required.");

        var tag = await _context.Tags
            .Include(t => t.GrnApiResponse)
            .FirstOrDefaultAsync(t => t.TagNo == tagNo, ct)
            ?? throw new NotFoundException("Tag", tagNo);

        if (tag.Status != TagStatus.Issued)
            throw new DomainException($"Tag '{tagNo}' is in status '{tag.Status}'. Only 'Issued' tags can be accepted.");

        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? tx = null;
        if (_context.Database.IsRelational())
        {
            tx = await _context.Database.BeginTransactionAsync(ct);
        }

        try
        {
            var utcNow = _clock.UtcNow;
            var userSnapshot = await GetUserNameSnapshotAsync(ct);

            var lastOutward = await _context.StockMovements
                .Where(sm => sm.TagId == tag.Id && sm.MovementType == StockMovementType.Out)
                .OrderByDescending(sm => sm.CreatedAt)
                .FirstOrDefaultAsync(ct);

            var acceptedQty = lastOutward?.Qty ?? tag.Qty;
            var prevStatus = tag.Status.ToString();

            // If tag has remaining balance from partial outward, it returns to InStock; otherwise Accepted
            tag.Status = tag.RemainingQty > 0 ? TagStatus.InStock : TagStatus.Accepted;

            var movement = new StockMovement
            {
                BranchId = tag.BranchId,
                TagId = tag.Id,
                LocationId = lastOutward?.LocationId,
                MaterialCode = tag.GrnApiResponse?.MaterialCode ?? lastOutward?.MaterialCode ?? string.Empty,
                MovementType = StockMovementType.Accept,
                Qty = acceptedQty,
                InwardAt = lastOutward?.InwardAt,
                UserId = _currentUser.UserId,
                UserNameSnapshot = userSnapshot,
                ClientIp = clientIp
            };

            _context.StockMovements.Add(movement);

            await _context.SaveChangesAsync(ct);
            if (tx != null) await tx.CommitAsync(ct);

            return new AcceptRejectResponseDto(
                tag.Id,
                tag.TagNo,
                movement.MaterialCode,
                prevStatus,
                tag.Status.ToString(),
                tag.RemainingQty,
                null,
                utcNow);
        }
        catch (DbUpdateConcurrencyException)
        {
            if (tx != null) await tx.RollbackAsync(ct);
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

    public async Task<AcceptRejectResponseDto> RejectAsync(
        RejectRequestDto request,
        string? clientIp,
        CancellationToken ct = default)
    {
        if (request == null)
            throw new DomainException("Reject request payload is required.");

        var tagNo = request.TagNo?.Trim();
        if (string.IsNullOrWhiteSpace(tagNo))
            throw new DomainException("Tag number is required.");

        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new DomainException("Rejection reason is required.");

        var cleanReason = request.Reason.Trim();
        if (cleanReason.Length > 250)
            throw new DomainException("Rejection reason cannot exceed 250 characters.");

        var tag = await _context.Tags
            .Include(t => t.GrnApiResponse)
            .FirstOrDefaultAsync(t => t.TagNo == tagNo, ct)
            ?? throw new NotFoundException("Tag", tagNo);

        if (tag.Status != TagStatus.Issued)
            throw new DomainException($"Tag '{tagNo}' is in status '{tag.Status}'. Only 'Issued' tags can be rejected.");

        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? tx = null;
        if (_context.Database.IsRelational())
        {
            tx = await _context.Database.BeginTransactionAsync(ct);
        }

        try
        {
            var utcNow = _clock.UtcNow;
            var userSnapshot = await GetUserNameSnapshotAsync(ct);

            var lastOutward = await _context.StockMovements
                .Where(sm => sm.TagId == tag.Id && sm.MovementType == StockMovementType.Out)
                .OrderByDescending(sm => sm.CreatedAt)
                .FirstOrDefaultAsync(ct);

            var rejectedQty = lastOutward?.Qty ?? tag.Qty;
            var prevStatus = tag.Status.ToString();
            tag.Status = TagStatus.Rejected;

            var movement = new StockMovement
            {
                BranchId = tag.BranchId,
                TagId = tag.Id,
                LocationId = lastOutward?.LocationId,
                MaterialCode = tag.GrnApiResponse?.MaterialCode ?? lastOutward?.MaterialCode ?? string.Empty,
                MovementType = StockMovementType.Reject,
                Qty = rejectedQty,
                InwardAt = lastOutward?.InwardAt,
                Reason = cleanReason,
                UserId = _currentUser.UserId,
                UserNameSnapshot = userSnapshot,
                ClientIp = clientIp
            };

            _context.StockMovements.Add(movement);

            await _context.SaveChangesAsync(ct);
            if (tx != null) await tx.CommitAsync(ct);

            return new AcceptRejectResponseDto(
                tag.Id,
                tag.TagNo,
                movement.MaterialCode,
                prevStatus,
                tag.Status.ToString(),
                tag.RemainingQty,
                cleanReason,
                utcNow);
        }
        catch (DbUpdateConcurrencyException)
        {
            if (tx != null) await tx.RollbackAsync(ct);
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

    public async Task<TransferResponseDto> TransferAsync(
        TransferRequestDto request,
        string? clientIp,
        CancellationToken ct = default)
    {
        if (request == null)
            throw new DomainException("Transfer request payload is required.");

        var tagNo = request.TagNo?.Trim();
        var targetLocationCode = request.TargetLocationCode?.Trim();

        if (string.IsNullOrWhiteSpace(tagNo))
            throw new DomainException("Tag number is required.");

        if (string.IsNullOrWhiteSpace(targetLocationCode))
            throw new DomainException("Target location code is required.");

        var tag = await _context.Tags
            .Include(t => t.GrnApiResponse)
            .FirstOrDefaultAsync(t => t.TagNo == tagNo, ct)
            ?? throw new NotFoundException("Tag", tagNo);

        if (tag.Status != TagStatus.InStock)
            throw new DomainException($"Tag '{tagNo}' is in status '{tag.Status}'. Only 'InStock' tags can be transferred.");

        var stockRecord = await _context.StockRecords
            .Include(sr => sr.Location)
            .FirstOrDefaultAsync(sr => sr.TagId == tag.Id && sr.Qty > 0, ct)
            ?? throw new DomainException($"No active stock record found for tag '{tagNo}'.");

        var targetLocation = await _context.Locations
            .FirstOrDefaultAsync(l => l.Code == targetLocationCode, ct)
            ?? throw new NotFoundException("Location", targetLocationCode);

        if (!targetLocation.IsActive)
            throw new DomainException($"Target location '{targetLocationCode}' is inactive.");

        if (targetLocation.Id == stockRecord.LocationId)
            throw new DomainException($"Tag '{tagNo}' is already located in '{targetLocationCode}'.");

        var targetUsedCapacity = await _context.StockRecords
            .Where(sr => sr.LocationId == targetLocation.Id)
            .SumAsync(sr => sr.Qty, ct);

        if (targetUsedCapacity + stockRecord.Qty > targetLocation.Capacity)
        {
            throw new DomainException(
                $"Target location '{targetLocationCode}' capacity ({targetLocation.Capacity}) exceeded. Currently used: {targetUsedCapacity}, attempting to transfer: {stockRecord.Qty}.");
        }

        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? tx = null;
        if (_context.Database.IsRelational())
        {
            tx = await _context.Database.BeginTransactionAsync(ct);
        }

        try
        {
            var utcNow = _clock.UtcNow;
            var userSnapshot = await GetUserNameSnapshotAsync(ct);

            var fromLocationId = stockRecord.LocationId;
            var fromLocationCode = stockRecord.Location.Code;

            // Update stock record location
            stockRecord.LocationId = targetLocation.Id;
            stockRecord.ModifiedAt = utcNow;

            // Touch both locations for optimistic concurrency
            stockRecord.Location.ModifiedAt = utcNow;
            targetLocation.ModifiedAt = utcNow;

            var movement = new StockMovement
            {
                BranchId = tag.BranchId,
                TagId = tag.Id,
                LocationId = targetLocation.Id,
                MaterialCode = stockRecord.MaterialCode,
                MovementType = StockMovementType.Transfer,
                Qty = stockRecord.Qty,
                InwardAt = stockRecord.InwardAt,
                Reason = $"Transferred from {fromLocationCode} to {targetLocation.Code}",
                UserId = _currentUser.UserId,
                UserNameSnapshot = userSnapshot,
                ClientIp = clientIp
            };

            _context.StockMovements.Add(movement);

            await _context.SaveChangesAsync(ct);
            if (tx != null) await tx.CommitAsync(ct);

            return new TransferResponseDto(
                stockRecord.Id,
                tag.Id,
                tag.TagNo,
                stockRecord.MaterialCode,
                stockRecord.Qty,
                fromLocationId,
                fromLocationCode,
                targetLocation.Id,
                targetLocation.Code,
                targetUsedCapacity + stockRecord.Qty,
                targetLocation.Capacity,
                utcNow);
        }
        catch (DbUpdateConcurrencyException)
        {
            if (tx != null) await tx.RollbackAsync(ct);
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

    public async Task<TagStockInfoDto> GetTagStockInfoAsync(string tagNo, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tagNo))
            throw new DomainException("Tag number is required.");

        var cleanTagNo = tagNo.Trim();
        var tag = await _context.Tags
            .Include(t => t.GrnApiResponse)
            .FirstOrDefaultAsync(t => t.TagNo == cleanTagNo, ct)
            ?? throw new NotFoundException("Tag", cleanTagNo);

        var stockRecord = await _context.StockRecords
            .Include(sr => sr.Location)
            .Where(sr => sr.TagId == tag.Id)
            .OrderByDescending(sr => sr.InwardAt)
            .FirstOrDefaultAsync(ct);

        return new TagStockInfoDto(
            tag.Id,
            tag.TagNo,
            tag.Status.ToString(),
            tag.RemainingQty,
            tag.RemainingQty,
            tag.GrnApiResponse?.MaterialCode ?? string.Empty,
            tag.GrnApiResponse?.PartName,
            tag.GrnApiResponse?.BatchNo,
            stockRecord?.LocationId,
            stockRecord?.Location.Code,
            stockRecord?.Location.Zone,
            stockRecord?.InwardAt);
    }

    public async Task<IReadOnlyList<StockMovementDto>> GetMovementsAsync(
        string? tagNo,
        int? locationId,
        CancellationToken ct = default)
    {
        var query = _context.StockMovements
            .Include(sm => sm.Tag)
            .Include(sm => sm.Location)
            .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(tagNo))
        {
            var cleanTag = tagNo.Trim();
            query = query.Where(sm => sm.Tag.TagNo == cleanTag);
        }

        if (locationId.HasValue)
        {
            query = query.Where(sm => sm.LocationId == locationId.Value);
        }

        return await query
            .OrderByDescending(sm => sm.CreatedAt)
            .ThenByDescending(sm => sm.Id)
            .Take(100)
            .Select(sm => new StockMovementDto(
                sm.Id,
                sm.TagId,
                sm.Tag.TagNo,
                sm.LocationId,
                sm.Location != null ? sm.Location.Code : null,
                sm.MaterialCode,
                sm.MovementType.ToString(),
                sm.Qty,
                sm.InwardAt,
                sm.Reason,
                sm.UserId,
                sm.UserNameSnapshot,
                sm.ClientIp,
                sm.CreatedAt))
            .ToListAsync(ct);
    }

    private async Task<string> GetUserNameSnapshotAsync(CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (userId == 0) return "System";

        var user = await _context.AppUsers
            .IgnoreQueryFilters()
            .Include(u => u.Employee)
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

        return user?.Employee?.Name ?? user?.Username ?? $"User {userId}";
    }
}
