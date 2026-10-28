using Inventory.Application.Users.DTOs;

namespace Inventory.Application.Users.Services;

public interface IUserService
{
    Task<IReadOnlyList<UserDto>> GetAllAsync(CancellationToken ct = default);
    Task<UserDto> GetByIdAsync(int id, CancellationToken ct = default);
    Task<UserDto> CreateAsync(CreateUserDto dto, CancellationToken ct = default);
    Task<UserDto> UpdateAsync(int id, UpdateUserDto dto, CancellationToken ct = default);
    Task ResetPasswordAsync(int id, ResetPasswordDto dto, CancellationToken ct = default);
    Task UnlockAsync(int id, CancellationToken ct = default);
}
