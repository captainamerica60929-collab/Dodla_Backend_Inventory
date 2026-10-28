using Inventory.Application.Employees.DTOs;

namespace Inventory.Application.Employees.Services;

public interface IEmployeeService
{
    Task<IReadOnlyList<EmployeeDto>> GetAllAsync(bool? activeOnly, CancellationToken ct = default);
    Task<EmployeeDto> GetByIdAsync(int id, CancellationToken ct = default);
    Task<EmployeeDto> CreateAsync(CreateEmployeeDto dto, CancellationToken ct = default);
    Task<EmployeeDto> UpdateAsync(int id, UpdateEmployeeDto dto, CancellationToken ct = default);
    Task DeactivateAsync(int id, CancellationToken ct = default);
}
