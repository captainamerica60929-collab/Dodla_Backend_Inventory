using System.Text;
using Inventory.Application.ApiCredentials.DTOs;
using Inventory.Application.GrnMapping;
using Inventory.Application.GrnResponses.DTOs;
using Inventory.Domain.Entities;
using Inventory.Domain.Enums;
using Inventory.Domain.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Controllers;

[ApiController]
[Route("api/grn-mapping")]
[Authorize(Policy = "GrnManage")]
public class GrnMappingController : ControllerBase
{
    public const int MaxJsonSizeBytes = 256 * 1024; // 256 KB

    [HttpPost("preview-sample")]
    public ActionResult<SamplePreviewResultDto> PreviewSample([FromBody] PreviewSampleRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Json))
        {
            throw new DomainException("JSON string cannot be empty.");
        }

        var byteCount = Encoding.UTF8.GetByteCount(request.Json);
        if (byteCount > MaxJsonSizeBytes)
        {
            throw new DomainException($"JSON payload size ({byteCount} bytes) exceeds maximum allowed limit of {MaxJsonSizeBytes} bytes (256 KB).");
        }

        var entityMappings = request.Mappings
            .Select(m => new ApiFieldMapping
            {
                SourceField = m.SourceField.Trim(),
                TargetColumn = m.TargetColumn,
                Format = string.IsNullOrWhiteSpace(m.Format) ? null : m.Format.Trim(),
                DefaultValue = string.IsNullOrWhiteSpace(m.DefaultValue) ? null : m.DefaultValue.Trim(),
                IsRequired = m.IsRequired
            })
            .ToList();

        var mappedRows = GrnResponseMapper.Map(
            request.Json,
            entityMappings,
            request.ResponseRootPath,
            branchId: 0,
            apiCredentialId: 0,
            fetchBatchId: Guid.Empty,
            fetchedBy: null,
            fetchedAt: DateTime.UtcNow);

        var previewRows = mappedRows
            .Take(20)
            .Select(r => new GrnApiResponsePreviewDto(
                r.GrnNo,
                r.GrnYear,
                r.GrnItemNo,
                r.GrnDate,
                r.InvoiceNo,
                r.InvoiceDate,
                r.Qty,
                r.Uom,
                r.MaterialCode,
                r.PartName,
                r.Description,
                r.Plant,
                r.StorageLocation,
                r.BatchNo,
                r.PoNo,
                r.PoItem,
                r.VendorCode,
                r.VendorName,
                r.SourceKey,
                r.RawJson,
                r.Status,
                r.ErrorMessage))
            .ToList();

        var errorCount = mappedRows.Count(r => r.Status == GrnResponseStatus.Error);

        return Ok(new SamplePreviewResultDto(
            mappedRows.Count,
            previewRows.Count,
            errorCount,
            previewRows));
    }
}
