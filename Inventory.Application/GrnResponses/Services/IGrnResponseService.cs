using Inventory.Application.GrnResponses.DTOs;

namespace Inventory.Application.GrnResponses.Services;

public interface IGrnResponseService
{
    Task<PagedResultDto<GrnResponseListItemDto>> GetPagedAsync(GrnResponseFilterDto filter, CancellationToken ct = default);
    Task<GrnResponseDetailDto> GetByIdAsync(int id, CancellationToken ct = default);
    Task DiscardBatchAsync(Guid fetchBatchId, CancellationToken ct = default);
}
