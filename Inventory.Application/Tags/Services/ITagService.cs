using Inventory.Application.Common.Models;
using Inventory.Application.Tags.DTOs;
using Inventory.Domain.Enums;

namespace Inventory.Application.Tags.Services;

public interface ITagService
{
    Task<PagedResult<GrnPrintRowDto>> GetGrnPrintRowsAsync(
        PrintStateFilter printState,
        string? grnNo,
        DateTime? fromDate,
        DateTime? toDate,
        int page,
        int pageSize,
        CancellationToken ct = default);

    Task<TagDto> PrepareTagAsync(int grnRowId, CancellationToken ct = default);

    Task<TagDto> MarkPrintedAsync(int tagId, string? clientIp, CancellationToken ct = default);

    Task<TagDto> ReprintAsync(int tagId, string reason, string? clientIp, CancellationToken ct = default);

    Task<IReadOnlyList<TagPrintLogDto>> GetPrintLogsAsync(int tagId, CancellationToken ct = default);
}
