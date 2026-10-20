using Inventory.Application.Auth.DTOs;

namespace Inventory.Application.Auth.Services;

public interface IAuthService
{
    Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<BranchLookupDto>> GetActiveBranchesAsync(CancellationToken ct = default);
    Task<MeDto> GetMeAsync(CancellationToken ct = default);
    Task ChangePasswordAsync(ChangePasswordRequest request, CancellationToken ct = default);
}
