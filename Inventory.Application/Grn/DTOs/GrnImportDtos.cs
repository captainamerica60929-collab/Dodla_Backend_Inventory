using System.Text.Json.Serialization;

namespace Inventory.Application.Grn.DTOs;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GrnImportRowStatus
{
    New,
    Error
}

public record GrnImportRowResult(
    int RowNumber,
    string GrnNo,
    string PartCode,
    GrnImportRowStatus Status,
    string? Message);

public record GrnImportResultDto(
    int Total,
    int NewCount,
    int ErrorCount,
    IReadOnlyList<GrnImportRowResult> Rows);
