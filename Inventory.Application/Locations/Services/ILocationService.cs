using Inventory.Application.Common.Models;
using Inventory.Application.Locations.DTOs;

namespace Inventory.Application.Locations.Services;

public interface ILocationService
{
    Task<PagedResult<LocationDto>> GetPagedAsync(LocationFilterDto filter, CancellationToken ct = default);
    Task<LocationDto> GetByIdAsync(int id, CancellationToken ct = default);
    Task<LocationDto> CreateAsync(CreateLocationDto dto, CancellationToken ct = default);
    Task<LocationDto> UpdateAsync(int id, UpdateLocationDto dto, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
    Task<byte[]> GenerateTemplateAsync(CancellationToken ct = default);
    Task<ImportResultDto> ImportAsync(Stream fileStream, string fileName, ImportMode mode, bool dryRun, bool skipInvalidRows, CancellationToken ct = default);
}
