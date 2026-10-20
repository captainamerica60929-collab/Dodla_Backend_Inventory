using Inventory.Application.Employees.DTOs;
using Inventory.Application.Employees.Services;
using Inventory.Domain.Entities;
using Inventory.Domain.Enums;
using Inventory.Domain.Exceptions;
using Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Infrastructure.Services;

public class EmployeeService : IEmployeeService
{
    private readonly InventoryDbContext _context;

    public EmployeeService(InventoryDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<EmployeeDto>> GetAllAsync(bool? activeOnly, CancellationToken ct = default)
    {
        var query = _context.Employees.AsNoTracking();

        if (activeOnly == true)
            query = query.Where(e => e.IsActive);

        return await query
            .OrderBy(e => e.Code)
            .Select(e => ToDto(e))
            .ToListAsync(ct);
    }

    public async Task<EmployeeDto> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var employee = await _context.Employees
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id, ct)
            ?? throw new NotFoundException(nameof(Employee), id);

        return ToDto(employee);
    }

    public async Task<EmployeeDto> CreateAsync(CreateEmployeeDto dto, CancellationToken ct = default)
    {
        var normalizedCode = dto.Code.Trim().ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(normalizedCode))
            throw new DomainException("Employee code is required.");

        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new DomainException("Employee name is required.");

        var exists = await _context.Employees
            .AnyAsync(e => e.Code == normalizedCode, ct);
        if (exists)
            throw new DomainException($"Employee with code '{normalizedCode}' already exists.");

        var employee = new Employee
        {
            Code = normalizedCode,
            Name = dto.Name.Trim(),
            Department = NullIfBlank(dto.Department),
            Designation = NullIfBlank(dto.Designation),
            Phone = NullIfBlank(dto.Phone),
            IsActive = true
        };

        _context.Employees.Add(employee);
        await _context.SaveChangesAsync(ct);

        return ToDto(employee);
    }

    public async Task<EmployeeDto> UpdateAsync(int id, UpdateEmployeeDto dto, CancellationToken ct = default)
    {
        var employee = await _context.Employees.FindAsync([id], ct)
            ?? throw new NotFoundException(nameof(Employee), id);

        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new DomainException("Employee name is required.");

        employee.Name = dto.Name.Trim();
        employee.Department = NullIfBlank(dto.Department);
        employee.Designation = NullIfBlank(dto.Designation);
        employee.Phone = NullIfBlank(dto.Phone);

        if (employee.IsActive && !dto.IsActive)
            await DeactivateEmployeeAndUsersAsync(employee, ct);
        else if (!employee.IsActive && dto.IsActive)
            employee.Activate();

        await _context.SaveChangesAsync(ct);
        return ToDto(employee);
    }

    public async Task DeactivateAsync(int id, CancellationToken ct = default)
    {
        var employee = await _context.Employees
            .Include(e => e.AppUser)
            .FirstOrDefaultAsync(e => e.Id == id, ct)
            ?? throw new NotFoundException(nameof(Employee), id);

        await DeactivateEmployeeAndUsersAsync(employee, ct);
        await _context.SaveChangesAsync(ct);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task DeactivateEmployeeAndUsersAsync(Employee employee, CancellationToken ct)
    {
        var users = await _context.AppUsers
            .IgnoreQueryFilters()
            .Where(u => u.EmployeeId == employee.Id)
            .ToListAsync(ct);

        // Guard: cannot deactivate employee if linked to the last active Admin
        var activeAdminsLinked = users.Where(u => u.Role == UserRole.Admin && u.IsActive).ToList();
        if (activeAdminsLinked.Any())
        {
            var linkedAdminIds = activeAdminsLinked.Select(u => u.Id).ToList();
            var remainingActiveAdmins = await _context.AppUsers
                .IgnoreQueryFilters()
                .CountAsync(u => u.Role == UserRole.Admin && u.IsActive && !linkedAdminIds.Contains(u.Id), ct);

            if (remainingActiveAdmins == 0)
                throw new DomainException("Cannot deactivate an employee linked to the last active administrator.");
        }

        employee.Deactivate();

        // Cascade: deactivate all AppUsers linked to this employee
        foreach (var user in users)
            user.Deactivate();
    }

    private static EmployeeDto ToDto(Employee e) => new(
        e.Id, e.Code, e.Name, e.Department, e.Designation, e.Phone,
        e.IsActive, e.CreatedAt, e.CreatedBy, e.ModifiedAt, e.ModifiedBy);

    private static string? NullIfBlank(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
