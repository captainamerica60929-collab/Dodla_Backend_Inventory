using Inventory.Application.ApiCredentials.DTOs;
using Inventory.Application.GrnResponses.DTOs;
using Inventory.Controllers;
using Inventory.Domain.Enums;
using Inventory.Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Application.Tests;

public class SamplePreviewEndpointTests
{
    [Fact]
    public void PreviewSample_WithValidPayload_ShouldReturnPreviewAndCounts()
    {
        // Arrange
        var controller = new GrnMappingController();
        const string json = """
        [
          { "GRNNo": "GRN-001", "Qty": 100 },
          { "GRNNo": "GRN-002", "Qty": "invalid" }
        ]
        """;

        var request = new PreviewSampleRequestDto(
            json,
            null,
            new List<UpsertApiFieldMappingDto>
            {
                new("GRNNo", TargetColumn.GrnNo, null, null, true),
                new("Qty", TargetColumn.Qty, null, null, true)
            });

        // Act
        var result = controller.PreviewSample(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var preview = Assert.IsType<SamplePreviewResultDto>(okResult.Value);

        Assert.Equal(2, preview.TotalRows);
        Assert.Equal(2, preview.ReturnedPreviewCount);
        Assert.Equal(1, preview.ErrorRowsCount);
        Assert.Equal("GRN-001", preview.Rows[0].GrnNo);
        Assert.Equal(GrnResponseStatus.New, preview.Rows[0].Status);
        Assert.Equal(GrnResponseStatus.Error, preview.Rows[1].Status);
    }

    [Fact]
    public void PreviewSample_PayloadExceeding256KB_ShouldThrowDomainException()
    {
        // Arrange
        var controller = new GrnMappingController();
        var bigJson = new string('A', 256 * 1024 + 1);

        var request = new PreviewSampleRequestDto(
            bigJson,
            null,
            new List<UpsertApiFieldMappingDto>());

        // Act & Assert
        var ex = Assert.Throws<DomainException>(() => controller.PreviewSample(request));
        Assert.Contains("256 KB", ex.Message);
    }
}
