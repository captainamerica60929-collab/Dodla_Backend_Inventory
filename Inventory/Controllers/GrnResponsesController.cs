using Inventory.Application.GrnResponses.DTOs;
using Inventory.Application.GrnResponses.Services;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Controllers;

[ApiController]
[Route("api/grn-responses")]
public class GrnResponsesController : ControllerBase
{
    private readonly IGrnResponseService _grnResponseService;

    public GrnResponsesController(IGrnResponseService grnResponseService)
    {
        _grnResponseService = grnResponseService;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResultDto<GrnResponseListItemDto>>> GetPaged(
        [FromQuery] GrnResponseFilterDto filter,
        CancellationToken ct)
    {
        var result = await _grnResponseService.GetPagedAsync(filter, ct);
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<GrnResponseDetailDto>> GetById(int id, CancellationToken ct)
    {
        var item = await _grnResponseService.GetByIdAsync(id, ct);
        return Ok(item);
    }

    [HttpDelete("batches/{fetchBatchId:guid}")]
    public async Task<IActionResult> DiscardBatch(Guid fetchBatchId, CancellationToken ct)
    {
        await _grnResponseService.DiscardBatchAsync(fetchBatchId, ct);
        return NoContent();
    }
}
