using Inventory.Application.Common.Models;
using Inventory.Application.Tags.DTOs;
using Inventory.Application.Tags.Services;
using Inventory.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Controllers;

[ApiController]
[Route("api/print")]
[Authorize(Policy = "GrnManage")]
public class PrintController : ControllerBase
{
    private readonly ITagService _tagService;
    private readonly IConfiguration _configuration;

    public PrintController(ITagService tagService, IConfiguration configuration)
    {
        _tagService = tagService;
        _configuration = configuration;
    }

    /// <summary>
    /// Gets rows from GrnApiResponses with status New or Processed, with tag information.
    /// Excludes RawJson. Ordered by GrnDate descending then Id descending.
    /// </summary>
    [HttpGet("grn-rows")]
    public async Task<ActionResult<PagedResult<GrnPrintRowDto>>> GetGrnRows(
        [FromQuery] PrintStateFilter printState = PrintStateFilter.All,
        [FromQuery] string? grnNo = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var result = await _tagService.GetGrnPrintRowsAsync(
            printState,
            grnNo,
            fromDate,
            toDate,
            page,
            pageSize,
            ct);

        return Ok(result);
    }

    /// <summary>
    /// Idempotently prepares a tag for a GRN row in ONE database transaction.
    /// Returns the existing tag if already prepared.
    /// </summary>
    [HttpPost("grn-rows/{id:int}/tags")]
    public async Task<ActionResult<TagDto>> PrepareTag([FromRoute] int id, CancellationToken ct)
    {
        var tag = await _tagService.PrepareTagAsync(id, ct);
        return Ok(tag);
    }

    /// <summary>
    /// Confirms that a tag was printed for the first time (or reprinted).
    /// Takes an empty request body. Logs audit tuple (UserId, UserNameSnapshot, ClientIp).
    /// </summary>
    [HttpPost("tags/{id:int}/printed")]
    public async Task<ActionResult<TagDto>> MarkPrinted([FromRoute] int id, CancellationToken ct)
    {
        var clientIp = GetClientIp();
        var tag = await _tagService.MarkPrintedAsync(id, clientIp, ct);
        return Ok(tag);
    }

    /// <summary>
    /// Records a tag reprint event with an audited mandatory reason.
    /// </summary>
    [HttpPost("tags/{id:int}/reprint")]
    public async Task<ActionResult<TagDto>> Reprint(
        [FromRoute] int id,
        [FromBody] ReprintRequest request,
        CancellationToken ct)
    {
        var clientIp = GetClientIp();
        var tag = await _tagService.ReprintAsync(id, request.Reason, clientIp, ct);
        return Ok(tag);
    }

    /// <summary>
    /// Gets the audit log history of all print and reprint events for a tag.
    /// </summary>
    [HttpGet("tags/{id:int}/log")]
    public async Task<ActionResult<IReadOnlyList<TagPrintLogDto>>> GetLogs([FromRoute] int id, CancellationToken ct)
    {
        var logs = await _tagService.GetPrintLogsAsync(id, ct);
        return Ok(logs);
    }

    private string? GetClientIp()
    {
        var trustProxy = _configuration.GetValue<bool>("ReverseProxy:TrustForwardedHeaders");
        if (trustProxy)
        {
            var forwardedFor = Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(forwardedFor))
            {
                var ips = forwardedFor.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (ips.Length > 0)
                    return ips[0];
            }
        }

        return HttpContext.Connection.RemoteIpAddress?.ToString();
    }
}
