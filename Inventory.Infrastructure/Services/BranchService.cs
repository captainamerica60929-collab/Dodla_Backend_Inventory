using Inventory.Application.Branches.DTOs;
using Inventory.Application.Branches.Services;
using Inventory.Domain.Entities;
using Inventory.Domain.Exceptions;
using Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Infrastructure.Services;

public class BranchService : IBranchService
{
    private readonly InventoryDbContext _context;

    public BranchService(InventoryDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<BranchDto>> GetAllAsync(bool? activeOnly, CancellationToken ct = default)
    {
        var query = _context.Branches.AsNoTracking();

        if (activeOnly == true)
        {
            query = query.Where(b => b.IsActive);
        }

        return await query
            .OrderBy(b => b.Code)
            .Select(b => new BranchDto(
                b.Id,
                b.Code,
                b.Name,
                b.PlantCode,
                b.IsActive,
                b.CreatedAt,
                b.CreatedBy,
                b.ModifiedAt,
                b.ModifiedBy))
            .ToListAsync(ct);
    }

    public async Task<BranchDto> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var branch = await _context.Branches
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == id, ct);

        if (branch == null)
            throw new NotFoundException(nameof(Branch), id);

        return new BranchDto(
            branch.Id,
            branch.Code,
            branch.Name,
            branch.PlantCode,
            branch.IsActive,
            branch.CreatedAt,
            branch.CreatedBy,
            branch.ModifiedAt,
            branch.ModifiedBy);
    }

    public async Task<BranchDto> CreateAsync(CreateBranchDto dto, CancellationToken ct = default)
    {
        var normalizedCode = dto.Code.Trim().ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(normalizedCode))
            throw new DomainException("Branch code is required.");

        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new DomainException("Branch name is required.");

        var exists = await _context.Branches
            .AnyAsync(b => b.Code == normalizedCode, ct);

        if (exists)
            throw new DomainException($"Branch with code '{normalizedCode}' already exists.");

        var branch = new Branch
        {
            Code = normalizedCode,
            Name = dto.Name.Trim(),
            PlantCode = string.IsNullOrWhiteSpace(dto.PlantCode) ? null : dto.PlantCode.Trim(),
            IsActive = true
        };

        _context.Branches.Add(branch);
        await _context.SaveChangesAsync(ct);

        return new BranchDto(
            branch.Id,
            branch.Code,
            branch.Name,
            branch.PlantCode,
            branch.IsActive,
            branch.CreatedAt,
            branch.CreatedBy,
            branch.ModifiedAt,
            branch.ModifiedBy);
    }

    public async Task<BranchDto> UpdateAsync(int id, UpdateBranchDto dto, CancellationToken ct = default)
    {
        var branch = await _context.Branches.FindAsync([id], ct);

        if (branch == null)
            throw new NotFoundException(nameof(Branch), id);

        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new DomainException("Branch name is required.");

        branch.Name = dto.Name.Trim();
        branch.PlantCode = string.IsNullOrWhiteSpace(dto.PlantCode) ? null : dto.PlantCode.Trim();
        branch.IsActive = dto.IsActive;

        await _context.SaveChangesAsync(ct);

        return new BranchDto(
            branch.Id,
            branch.Code,
            branch.Name,
            branch.PlantCode,
            branch.IsActive,
            branch.CreatedAt,
            branch.CreatedBy,
            branch.ModifiedAt,
            branch.ModifiedBy);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var branch = await _context.Branches.FindAsync([id], ct);

        if (branch == null)
            throw new NotFoundException(nameof(Branch), id);

        // Soft-delete = deactivate
        branch.Deactivate();
        await _context.SaveChangesAsync(ct);
    }
}
