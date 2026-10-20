using System.Text.Json.Serialization;

namespace Inventory.Application.Common.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ImportMode
{
    CreateOnly = 0,
    Upsert = 1
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ImportRowStatus
{
    New,
    Exists,
    Error,
    Imported,
    Updated,
    Skipped
}

public record ImportRowResult(
    int RowNumber,
    string Code,
    ImportRowStatus Status,
    string? Message);

public record ImportResultDto(
    int Total,
    int NewCount,
    int ExistsCount,
    int ErrorCount,
    int Imported,
    int Updated,
    int Skipped,
    IReadOnlyList<ImportRowResult> Rows);
