using Inventory.Application.Branches.DTOs;

namespace Inventory.Application.Branches.Services;

public interface IBranchService
{
    Task<IReadOnlyList<BranchDto>> GetAllAsync(bool? activeOnly, CancellationToken ct = default);
    Task<BranchDto> GetByIdAsync(int id, CancellationToken ct = default);
    Task<BranchDto> CreateAsync(CreateBranchDto dto, CancellationToken ct = default);
    Task<BranchDto> UpdateAsync(int id, UpdateBranchDto dto, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
}
