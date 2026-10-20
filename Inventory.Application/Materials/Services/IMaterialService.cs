using Inventory.Application.Common.Models;
using Inventory.Application.Materials.DTOs;

namespace Inventory.Application.Materials.Services;

public interface IMaterialService
{
    Task<PagedResult<MaterialDto>> GetPagedAsync(MaterialFilterDto filter, CancellationToken ct = default);
    Task<MaterialDto> GetByIdAsync(int id, CancellationToken ct = default);
    Task<MaterialDto> CreateAsync(CreateMaterialDto dto, CancellationToken ct = default);
    Task<MaterialDto> UpdateAsync(int id, UpdateMaterialDto dto, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
    Task<byte[]> GenerateTemplateAsync(CancellationToken ct = default);
    Task<ImportResultDto> ImportAsync(Stream fileStream, string fileName, ImportMode mode, bool dryRun, bool skipInvalidRows, CancellationToken ct = default);
}
