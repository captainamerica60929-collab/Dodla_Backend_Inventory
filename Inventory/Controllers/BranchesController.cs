using Inventory.Application.Branches.DTOs;
using Inventory.Application.Branches.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BranchesController : ControllerBase
{
    private readonly IBranchService _branchService;

    public BranchesController(IBranchService branchService)
    {
        _branchService = branchService;
    }

    [HttpGet]
    [Authorize(Policy = "MastersView")]
    public async Task<ActionResult<IReadOnlyList<BranchDto>>> GetAll([FromQuery] bool? activeOnly, CancellationToken ct)
    {
        var branches = await _branchService.GetAllAsync(activeOnly, ct);
        return Ok(branches);
    }

    [HttpGet("{id:int}")]
    [Authorize(Policy = "MastersView")]
    public async Task<ActionResult<BranchDto>> GetById(int id, CancellationToken ct)
    {
        var branch = await _branchService.GetByIdAsync(id, ct);
        return Ok(branch);
    }

    [HttpPost]
    [Authorize(Policy = "MastersEdit")]
    public async Task<ActionResult<BranchDto>> Create([FromBody] CreateBranchDto dto, CancellationToken ct)
    {
        var branch = await _branchService.CreateAsync(dto, ct);
        return CreatedAtAction(nameof(GetById), new { id = branch.Id }, branch);
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = "MastersEdit")]
    public async Task<ActionResult<BranchDto>> Update(int id, [FromBody] UpdateBranchDto dto, CancellationToken ct)
    {
        var branch = await _branchService.UpdateAsync(id, dto, ct);
        return Ok(branch);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = "MastersEdit")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _branchService.DeleteAsync(id, ct);
        return NoContent();
    }
}
