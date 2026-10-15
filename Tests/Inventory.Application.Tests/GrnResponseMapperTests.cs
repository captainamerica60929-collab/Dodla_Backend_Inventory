using Inventory.Application.GrnMapping;
using Inventory.Domain.Entities;
using Inventory.Domain.Enums;

namespace Inventory.Application.Tests;

public class GrnResponseMapperTests
{
    private readonly List<ApiFieldMapping> _standardMappings = new()
    {
        new ApiFieldMapping { TargetColumn = TargetColumn.GrnNo, SourceField = "GRNNo", IsRequired = true },
        new ApiFieldMapping { TargetColumn = TargetColumn.GrnDate, SourceField = "GRN Date", Format = "yyyy-MM-dd hh:mm tt" },
        new ApiFieldMapping { TargetColumn = TargetColumn.PartName, SourceField = "PartName" },
        new ApiFieldMapping { TargetColumn = TargetColumn.InvoiceNo, SourceField = "InvoiceNo" },
        new ApiFieldMapping { TargetColumn = TargetColumn.Description, SourceField = "Description" },
        new ApiFieldMapping { TargetColumn = TargetColumn.Qty, SourceField = "Qty" },
        new ApiFieldMapping { TargetColumn = TargetColumn.Plant, SourceField = "Plant" }
    };

    [Fact]
    public void Map_SampleJson_SingleObject_ShouldMapCorrectly()
    {
        // Sample JSON with space in keys, 12-hour date, integer qty
        const string json = """
        {
          "GRNNo": "5000123456",
          "GRN Date": "2026-10-15 12:11 PM",
          "PartName": "Motor Assembly",
          "InvoiceNo": "INV-2026-001",
          "Description": "Motor Assembly - Goods Receipt",
          "Qty": 100,
          "Plant": "1000"
        }
        """;

        var rows = GrnResponseMapper.Map(
            json,
            _standardMappings,
            rootPath: null,
            branchId: 5,
            apiCredentialId: 2,
            fetchBatchId: Guid.NewGuid(),
            fetchedBy: 10,
            fetchedAt: DateTime.UtcNow);

        Assert.Single(rows);
        var row = rows[0];

        Assert.Equal(GrnResponseStatus.New, row.Status);
        Assert.Null(row.ErrorMessage);
        Assert.Equal("5000123456", row.GrnNo);
        Assert.Equal(new DateTime(2026, 10, 15, 12, 11, 0), row.GrnDate);
        Assert.Equal("Motor Assembly", row.PartName);
        Assert.Equal("INV-2026-001", row.InvoiceNo);
        Assert.Equal("Motor Assembly - Goods Receipt", row.Description);
        Assert.Equal(100m, row.Qty);
        Assert.Equal("1000", row.Plant);
        Assert.Equal(5, row.BranchId);
        Assert.Equal(2, row.ApiCredentialId);
        Assert.Contains("5000123456", row.RawJson);
    }

    [Fact]
    public void Map_JsonArray_ShouldMapMultipleRows()
    {
        const string json = """
        [
          { "GRNNo": "GRN-01", "Qty": 10 },
          { "GRNNo": "GRN-02", "Qty": 20 }
        ]
        """;

        var rows = GrnResponseMapper.Map(
            json,
            _standardMappings,
            rootPath: null,
            branchId: 1,
            apiCredentialId: 1,
            fetchBatchId: Guid.NewGuid(),
            fetchedBy: 1,
            fetchedAt: DateTime.UtcNow);

        Assert.Equal(2, rows.Count);
        Assert.Equal("GRN-01", rows[0].GrnNo);
        Assert.Equal(10m, rows[0].Qty);
        Assert.Equal("GRN-02", rows[1].GrnNo);
        Assert.Equal(20m, rows[1].Qty);
    }

    [Fact]
    public void Map_WithResponseRootPath_ShouldNavigateNestedPath()
    {
        const string json = """
        {
          "status": "success",
          "data": {
            "items": [
              { "GRNNo": "NESTED-01", "Qty": 55 }
            ]
          }
        }
        """;

        var rows = GrnResponseMapper.Map(
            json,
            _standardMappings,
            rootPath: "data.items",
            branchId: 7,
            apiCredentialId: 3,
            fetchBatchId: Guid.NewGuid(),
            fetchedBy: 1,
            fetchedAt: DateTime.UtcNow);

        Assert.Single(rows);
        Assert.Equal("NESTED-01", rows[0].GrnNo);
        Assert.Equal(55m, rows[0].Qty);
        Assert.Equal(7, rows[0].BranchId);
    }

    [Fact]
    public void Map_MissingGrnNo_ShouldReturnErrorRow()
    {
        const string json = """
        {
          "Qty": 100,
          "Plant": "1000"
        }
        """;

        var rows = GrnResponseMapper.Map(
            json,
            _standardMappings,
            rootPath: null,
            branchId: 1,
            apiCredentialId: 1,
            fetchBatchId: Guid.NewGuid(),
            fetchedBy: 1,
            fetchedAt: DateTime.UtcNow);

        Assert.Single(rows);
        var row = rows[0];
        Assert.Equal(GrnResponseStatus.Error, row.Status);
        Assert.NotNull(row.ErrorMessage);
        Assert.Contains("GrnNo is required", row.ErrorMessage);
        Assert.NotEmpty(row.RawJson);
    }

    [Fact]
    public void Map_InvalidQty_ShouldReturnErrorRowWithoutFailingBatch()
    {
        const string json = """
        [
          { "GRNNo": "VALID-01", "Qty": 40 },
          { "GRNNo": "INVALID-02", "Qty": "NOT_A_NUMBER" },
          { "GRNNo": "VALID-03", "Qty": 60 }
        ]
        """;

        var rows = GrnResponseMapper.Map(
            json,
            _standardMappings,
            rootPath: null,
            branchId: 1,
            apiCredentialId: 1,
            fetchBatchId: Guid.NewGuid(),
            fetchedBy: 1,
            fetchedAt: DateTime.UtcNow);

        Assert.Equal(3, rows.Count);
        Assert.Equal(GrnResponseStatus.New, rows[0].Status);
        Assert.Equal(GrnResponseStatus.Error, rows[1].Status);
        Assert.Contains("Invalid decimal for Qty", rows[1].ErrorMessage);
        Assert.Equal(GrnResponseStatus.New, rows[2].Status);
    }

    [Fact]
    public void Map_InvalidDate_ShouldReturnErrorRow()
    {
        const string json = """
        {
          "GRNNo": "DATE-FAIL-01",
          "GRN Date": "Invalid-Date-String"
        }
        """;

        var rows = GrnResponseMapper.Map(
            json,
            _standardMappings,
            rootPath: null,
            branchId: 1,
            apiCredentialId: 1,
            fetchBatchId: Guid.NewGuid(),
            fetchedBy: 1,
            fetchedAt: DateTime.UtcNow);

        Assert.Single(rows);
        Assert.Equal(GrnResponseStatus.Error, rows[0].Status);
        Assert.Contains("Failed to parse GrnDate", rows[0].ErrorMessage);
    }

    [Fact]
    public void Map_ShouldAssignCredentialBranchIdToAllMappedRows()
    {
        const int expectedBranchId = 42;
        const string json = """
        [
          { "GRNNo": "B1" },
          { "GRNNo": "B2" }
        ]
        """;

        var rows = GrnResponseMapper.Map(
            json,
            _standardMappings,
            rootPath: null,
            branchId: expectedBranchId,
            apiCredentialId: 9,
            fetchBatchId: Guid.NewGuid(),
            fetchedBy: null,
            fetchedAt: DateTime.UtcNow);

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(expectedBranchId, r.BranchId));
    }
}
