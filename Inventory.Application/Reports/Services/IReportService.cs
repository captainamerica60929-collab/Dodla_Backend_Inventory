using Inventory.Application.Common.Models;
using Inventory.Application.Reports.DTOs;

namespace Inventory.Application.Reports.Services;

public interface IReportService
{
    Task<PagedResult<InwardReportRowDto>> GetInwardReportAsync(InwardReportFilter filter, CancellationToken ct = default);

    Task<PagedResult<OutwardReportRowDto>> GetOutwardReportAsync(OutwardReportFilter filter, CancellationToken ct = default);

    Task<PagedResult<InStockReportRowDto>> GetInStockReportAsync(InStockReportFilter filter, CancellationToken ct = default);

    Task<PagedResult<LabelScanReportRowDto>> GetLabelScanReportAsync(LabelScanReportFilter filter, CancellationToken ct = default);

    Task<PagedResult<OverallReportRowDto>> GetOverallReportAsync(OverallReportFilter filter, CancellationToken ct = default);

    Task<PagedResult<AcceptRejectReportRowDto>> GetAcceptRejectReportAsync(AcceptRejectReportFilter filter, CancellationToken ct = default);

    Task<PagedResult<LocationTransferReportRowDto>> GetLocationTransferReportAsync(LocationTransferReportFilter filter, CancellationToken ct = default);

    Task<DashboardLiveMetricsDto> GetDashboardLiveMetricsAsync(int? branchId, bool crossBranch = false, CancellationToken ct = default);
}
