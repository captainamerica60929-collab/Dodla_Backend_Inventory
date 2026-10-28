using Inventory.Application.Grn.DTOs;
using Inventory.Application.Grn.Services;
using Inventory.Domain.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Controllers;

[ApiController]
[Route("api/grn")]
[Authorize(Policy = "GrnManage")]
public class GrnController : ControllerBase
{
    private readonly IGrnImportService _grnImportService;

    public GrnController(IGrnImportService grnImportService)
    {
        _grnImportService = grnImportService;
    }

    [HttpGet("import-template")]
    public async Task<IActionResult> GetImportTemplate(CancellationToken ct)
    {
        var bytes = await _grnImportService.GenerateTemplateAsync(ct);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "grn-template.xlsx");
    }

    [HttpPost("import")]
    [RequestSizeLimit(2 * 1024 * 1024)]
    public async Task<ActionResult<GrnImportResultDto>> Import(
        IFormFile? file,
        [FromForm] bool dryRun = false,
        [FromForm] bool skipInvalidRows = false,
        CancellationToken ct = default)
    {
        if (file == null || file.Length == 0)
            throw new DomainException("Please select an Excel file to upload.");

        await using var stream = file.OpenReadStream();
        var result = await _grnImportService.ImportAsync(
            stream,
            file.FileName,
            file.ContentType,
            dryRun,
            skipInvalidRows,
            ct);

        if (!dryRun && result.ErrorCount > 0 && !skipInvalidRows)
        {
            return UnprocessableEntity(result);
        }

        return Ok(result);
    }
}
