using Inventory.Application.Stock.DTOs;
using Inventory.Application.Stock.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Controllers;

[ApiController]
[Route("api/stock")]
[Authorize(Policy = "StockOps")]
public class StockController : ControllerBase
{
    private readonly IStockService _stockService;

    public StockController(IStockService stockService)
    {
        _stockService = stockService;
    }

    /// <summary>
    /// Inward Putaway scan: maps a printed tag into a specified destination location/bin.
    /// Checks bin capacity, updates stock records with rowversion concurrency,
    /// sets tag status to InStock, and logs an IN movement.
    /// </summary>
    [HttpPost("inward")]
    [HttpPost("/api/inward")]
    public async Task<ActionResult<InwardResponseDto>> Inward(
        [FromBody] InwardScanRequestDto request,
        CancellationToken ct)
    {
        var result = await _stockService.InwardAsync(request, GetClientIp(), ct);
        return Ok(result);
    }

    /// <summary>
    /// Outward FIFO stock issuance: enforces strict FIFO consumption by material inward date.
    /// Supports partial issuance while preserving remaining balance on the same tag.
    /// Transitions tag status from InStock to Issued, and logs an OUT movement.
    /// </summary>
    [HttpPost("outward")]
    [HttpPost("/api/outward")]
    public async Task<ActionResult<OutwardResponseDto>> Outward(
        [FromBody] OutwardRequestDto request,
        CancellationToken ct)
    {
        var result = await _stockService.OutwardAsync(request, GetClientIp(), ct);
        return Ok(result);
    }

    /// <summary>
    /// Location transfer: moves an InStock tag from its current bin to a target active bin.
    /// Validates destination capacity, updates stock record location, and logs a Transfer movement.
    /// </summary>
    [HttpPost("transfer")]
    public async Task<ActionResult<TransferResponseDto>> Transfer(
        [FromBody] TransferRequestDto request,
        CancellationToken ct)
    {
        var result = await _stockService.TransferAsync(request, GetClientIp(), ct);
        return Ok(result);
    }

    /// <summary>
    /// User Accept: receiving operator confirms acceptance of an issued tag.
    /// Transitions tag status from Issued to Accepted (or back to InStock if partial balance remains).
    /// Logs an ACCEPT movement.
    /// </summary>
    [HttpPost("accept")]
    public async Task<ActionResult<AcceptRejectResponseDto>> Accept(
        [FromBody] AcceptRequestDto request,
        CancellationToken ct)
    {
        var result = await _stockService.AcceptAsync(request, GetClientIp(), ct);
        return Ok(result);
    }

    /// <summary>
    /// User Reject: receiving operator rejects an issued tag with a mandatory reason.
    /// Transitions tag status from Issued to Rejected.
    /// Logs a REJECT movement.
    /// </summary>
    [HttpPost("reject")]
    public async Task<ActionResult<AcceptRejectResponseDto>> Reject(
        [FromBody] RejectRequestDto request,
        CancellationToken ct)
    {
        var result = await _stockService.RejectAsync(request, GetClientIp(), ct);
        return Ok(result);
    }

    /// <summary>
    /// Tag lookup: returns live stock, status, location, and balance for scanning feedback.
    /// </summary>
    [HttpGet("scan/{tagNo}")]
    [HttpGet("tags/{tagNo}")]
    public async Task<ActionResult<TagStockInfoDto>> GetTagStockInfo(
        string tagNo,
        CancellationToken ct)
    {
        var result = await _stockService.GetTagStockInfoAsync(tagNo, ct);
        return Ok(result);
    }

    /// <summary>
    /// Movement audit trail: returns chronological list of stock movements.
    /// </summary>
    [HttpGet("movements")]
    public async Task<ActionResult<IReadOnlyList<StockMovementDto>>> GetMovements(
        [FromQuery] string? tagNo,
        [FromQuery] int? locationId,
        CancellationToken ct)
    {
        var result = await _stockService.GetMovementsAsync(tagNo, locationId, ct);
        return Ok(result);
    }

    private string? GetClientIp() =>
        HttpContext.Connection.RemoteIpAddress?.ToString();
}
