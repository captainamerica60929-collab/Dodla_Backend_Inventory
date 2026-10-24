using Inventory.Application.Common.Models;
using Inventory.Application.Reports.DTOs;
using Inventory.Application.Reports.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Controllers;

[ApiController]
[Route("api/reports")]
[Authorize(Policy = "ReportsView")]
public class ReportsController : ControllerBase
{
    private readonly IReportService _reportService;

    public ReportsController(IReportService reportService)
    {
        _reportService = reportService;
    }

    /// <summary>
    /// Inward report: live movement history of putaway operations filtered by date range, material, location, and branch.
    /// </summary>
    [HttpGet("inward")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PagedResult<InwardReportRowDto>>> GetInwardReport(
        [FromQuery] InwardReportFilter? filter,
        CancellationToken ct)
    {
        var result = await _reportService.GetInwardReportAsync(filter ?? new InwardReportFilter(), ct);
        return Ok(result);
    }

    /// <summary>
    /// Outward report: FIFO issuance log filtered by date range, material, tag number, and branch.
    /// </summary>
    [HttpGet("outward")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PagedResult<OutwardReportRowDto>>> GetOutwardReport(
        [FromQuery] OutwardReportFilter? filter,
        CancellationToken ct)
    {
        var result = await _reportService.GetOutwardReportAsync(filter ?? new OutwardReportFilter(), ct);
        return Ok(result);
    }

    /// <summary>
    /// In-stock report: current live balances grouped by material and bin, summing active tag balances.
    /// </summary>
    [HttpGet("in-stock")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PagedResult<InStockReportRowDto>>> GetInStockReport(
        [FromQuery] InStockReportFilter? filter,
        CancellationToken ct)
    {
        var result = await _reportService.GetInStockReportAsync(filter ?? new InStockReportFilter(), ct);
        return Ok(result);
    }

    /// <summary>
    /// Label scan audit: log of print, reprint, inward, outward, and transfer scanning events.
    /// </summary>
    [HttpGet("label-scan")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PagedResult<LabelScanReportRowDto>>> GetLabelScanReport(
        [FromQuery] LabelScanReportFilter? filter,
        CancellationToken ct)
    {
        var result = await _reportService.GetLabelScanReportAsync(filter ?? new LabelScanReportFilter(), ct);
        return Ok(result);
    }

    /// <summary>
    /// Overall summary report: consolidated inward, outward, and movement counts grouped by date or material.
    /// </summary>
    [HttpGet("overall")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PagedResult<OverallReportRowDto>>> GetOverallReport(
        [FromQuery] OverallReportFilter? filter,
        CancellationToken ct)
    {
        var result = await _reportService.GetOverallReportAsync(filter ?? new OverallReportFilter(), ct);
        return Ok(result);
    }

    /// <summary>
    /// Accept/Reject report: inspector verification logs with rejection reasons and timestamps.
    /// </summary>
    [HttpGet("accept-reject")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PagedResult<AcceptRejectReportRowDto>>> GetAcceptRejectReport(
        [FromQuery] AcceptRejectReportFilter? filter,
        CancellationToken ct)
    {
        var result = await _reportService.GetAcceptRejectReportAsync(filter ?? new AcceptRejectReportFilter(), ct);
        return Ok(result);
    }

    /// <summary>
    /// Location transfer report: bin-to-bin movements audit showing source and destination bins.
    /// </summary>
    [HttpGet("location-transfer")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PagedResult<LocationTransferReportRowDto>>> GetLocationTransferReport(
        [FromQuery] LocationTransferReportFilter? filter,
        CancellationToken ct)
    {
        var result = await _reportService.GetLocationTransferReportAsync(filter ?? new LocationTransferReportFilter(), ct);
        return Ok(result);
    }

    /// <summary>
    /// Live dashboard API: active branch operational cards (registered, pending, in-stock, alerts, and recent transactions).
    /// </summary>
    [HttpGet("dashboard-live")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<DashboardLiveMetricsDto>> GetDashboardLive(
        [FromQuery] int? branchId,
        [FromQuery] bool crossBranch = false,
        CancellationToken ct = default)
    {
        var result = await _reportService.GetDashboardLiveMetricsAsync(branchId, crossBranch, ct);
        return Ok(result);
    }
}
