using Inventory.Application.Grn.DTOs;

namespace Inventory.Application.Grn.Services;

public interface IGrnImportService
{
    Task<GrnImportResultDto> ImportAsync(
        Stream stream,
        string fileName,
        string? contentType,
        bool dryRun,
        bool skipInvalidRows,
        CancellationToken ct = default);

    Task<byte[]> GenerateTemplateAsync(CancellationToken ct = default);
}
