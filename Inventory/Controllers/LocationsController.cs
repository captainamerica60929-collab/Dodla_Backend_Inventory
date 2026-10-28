using Inventory.Application.Common.Models;
using Inventory.Application.Locations.DTOs;
using Inventory.Application.Locations.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LocationsController : ControllerBase
{
    private readonly ILocationService _locationService;

    public LocationsController(ILocationService locationService)
    {
        _locationService = locationService;
    }

    [HttpGet]
    [Authorize(Policy = "MastersView")]
    public async Task<ActionResult<PagedResult<LocationDto>>> GetPaged(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] bool? activeOnly = null,
        [FromQuery] string? zone = null,
        CancellationToken ct = default)
    {
        var filter = new LocationFilterDto(page, pageSize, search, activeOnly, zone);
        var result = await _locationService.GetPagedAsync(filter, ct);
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    [Authorize(Policy = "MastersView")]
    public async Task<ActionResult<LocationDto>> GetById(int id, CancellationToken ct)
    {
        var location = await _locationService.GetByIdAsync(id, ct);
        return Ok(location);
    }

    [HttpPost]
    [Authorize(Policy = "MastersEdit")]
    public async Task<ActionResult<LocationDto>> Create([FromBody] CreateLocationDto dto, CancellationToken ct)
    {
        var location = await _locationService.CreateAsync(dto, ct);
        return CreatedAtAction(nameof(GetById), new { id = location.Id }, location);
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = "MastersEdit")]
    public async Task<ActionResult<LocationDto>> Update(int id, [FromBody] UpdateLocationDto dto, CancellationToken ct)
    {
        var location = await _locationService.UpdateAsync(id, dto, ct);
        return Ok(location);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = "MastersEdit")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _locationService.DeleteAsync(id, ct);
        return NoContent();
    }

    [HttpGet("import-template")]
    [Authorize(Policy = "MastersEdit")]
    public async Task<IActionResult> GetImportTemplate(CancellationToken ct)
    {
        var bytes = await _locationService.GenerateTemplateAsync(ct);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "locations-template.xlsx");
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
        var result = await _locationService.ImportAsync(stream, file.FileName, mode, dryRun, skipInvalidRows, ct);

        if (!dryRun && result.ErrorCount > 0 && !skipInvalidRows)
        {
            return UnprocessableEntity(result);
        }

        return Ok(result);
    }
}
