using Inventory.Application.Common.Models;
using Inventory.Application.Materials.DTOs;
using Inventory.Application.Materials.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MaterialsController : ControllerBase
{
    private readonly IMaterialService _materialService;

    public MaterialsController(IMaterialService materialService)
    {
        _materialService = materialService;
    }

    [HttpGet]
    [Authorize(Policy = "MastersView")]
    public async Task<ActionResult<PagedResult<MaterialDto>>> GetPaged(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] bool? activeOnly = null,
        [FromQuery] string? zone = null,
        CancellationToken ct = default)
    {
        var filter = new MaterialFilterDto(page, pageSize, search, activeOnly, zone);
        var result = await _materialService.GetPagedAsync(filter, ct);
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    [Authorize(Policy = "MastersView")]
    public async Task<ActionResult<MaterialDto>> GetById(int id, CancellationToken ct)
    {
        var material = await _materialService.GetByIdAsync(id, ct);
        return Ok(material);
    }

    [HttpPost]
    [Authorize(Policy = "MastersEdit")]
    public async Task<ActionResult<MaterialDto>> Create([FromBody] CreateMaterialDto dto, CancellationToken ct)
    {
        var material = await _materialService.CreateAsync(dto, ct);
        return CreatedAtAction(nameof(GetById), new { id = material.Id }, material);
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = "MastersEdit")]
    public async Task<ActionResult<MaterialDto>> Update(int id, [FromBody] UpdateMaterialDto dto, CancellationToken ct)
    {
        var material = await _materialService.UpdateAsync(id, dto, ct);
        return Ok(material);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = "MastersEdit")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _materialService.DeleteAsync(id, ct);
        return NoContent();
    }

    [HttpGet("import-template")]
    [Authorize(Policy = "MastersEdit")]
    public async Task<IActionResult> GetImportTemplate(CancellationToken ct)
    {
        var bytes = await _materialService.GenerateTemplateAsync(ct);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "materials-template.xlsx");
    }

    [HttpPost("import")]
    [Authorize(Policy = "MastersEdit")]
    [RequestSizeLimit(2 * 1024 * 1024)]
    public async Task<ActionResult<ImportResultDto>> Import(
        IFormFile? file,
        [FromForm] ImportMode mode = ImportMode.CreateOnly,
        [FromForm] bool dryRun = false,
        [FromForm] bool skipInvalidRows = false,
        CancellationToken ct = default)
    {
        if (file == null || file.Length == 0)
            throw new Inventory.Domain.Exceptions.DomainException("Please select an Excel file to upload.");

        await using var stream = file.OpenReadStream();
        var result = await _materialService.ImportAsync(stream, file.FileName, mode, dryRun, skipInvalidRows, ct);

        if (!dryRun && result.ErrorCount > 0 && !skipInvalidRows)
        {
            return UnprocessableEntity(result);
        }

        return Ok(result);
    }
}
