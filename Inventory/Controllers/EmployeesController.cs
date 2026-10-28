using Inventory.Application.Employees.DTOs;
using Inventory.Application.Employees.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Controllers;

[ApiController]
[Route("api/employees")]
public class EmployeesController : ControllerBase
{
    private readonly IEmployeeService _employeeService;

    public EmployeesController(IEmployeeService employeeService)
    {
        _employeeService = employeeService;
    }

    [HttpGet]
    [Authorize(Policy = "MastersView")]
    public async Task<ActionResult<IReadOnlyList<EmployeeDto>>> GetAll(
        [FromQuery] bool? activeOnly, CancellationToken ct)
    {
        var employees = await _employeeService.GetAllAsync(activeOnly, ct);
        return Ok(employees);
    }

    [HttpGet("{id:int}")]
    [Authorize(Policy = "MastersView")]
    public async Task<ActionResult<EmployeeDto>> GetById(int id, CancellationToken ct)
    {
        var employee = await _employeeService.GetByIdAsync(id, ct);
        return Ok(employee);
    }

    [HttpPost]
    [Authorize(Policy = "MastersEdit")]
    public async Task<ActionResult<EmployeeDto>> Create([FromBody] CreateEmployeeDto dto, CancellationToken ct)
    {
        var employee = await _employeeService.CreateAsync(dto, ct);
        return CreatedAtAction(nameof(GetById), new { id = employee.Id }, employee);
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = "MastersEdit")]
    public async Task<ActionResult<EmployeeDto>> Update(
        int id, [FromBody] UpdateEmployeeDto dto, CancellationToken ct)
    {
        var employee = await _employeeService.UpdateAsync(id, dto, ct);
        return Ok(employee);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = "MastersEdit")]
    public async Task<IActionResult> Deactivate(int id, CancellationToken ct)
    {
        await _employeeService.DeactivateAsync(id, ct);
        return NoContent();
    }
}
