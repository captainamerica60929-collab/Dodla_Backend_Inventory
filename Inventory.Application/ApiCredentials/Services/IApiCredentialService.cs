using Inventory.Application.ApiCredentials.DTOs;

namespace Inventory.Application.ApiCredentials.Services;

public interface IApiCredentialService
{
    Task<IReadOnlyList<ApiCredentialDto>> GetAllAsync(CancellationToken ct = default);
    Task<ApiCredentialDto> GetByIdAsync(int id, CancellationToken ct = default);
    Task<ApiCredentialDto> CreateAsync(CreateApiCredentialDto dto, CancellationToken ct = default);
    Task<ApiCredentialDto> UpdateAsync(int id, UpdateApiCredentialDto dto, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);

    Task<IReadOnlyList<ApiFieldMappingDto>> GetMappingsAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<ApiFieldMappingDto>> ReplaceMappingsAsync(int id, IReadOnlyList<UpsertApiFieldMappingDto> mappings, CancellationToken ct = default);

    Task<ApiTestResultDto> TestConnectionAsync(int id, CancellationToken ct = default);
    Task<MappingPreviewResultDto> PreviewMappingAsync(int id, CancellationToken ct = default);
    Task<Inventory.Application.GrnResponses.DTOs.FetchResultDto> FetchAsync(int id, Inventory.Application.GrnResponses.DTOs.FetchGrnRequestDto request, CancellationToken ct = default);
}
