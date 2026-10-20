using System.Globalization;
using System.Text.Json;
using Inventory.Domain.Entities;
using Inventory.Domain.Enums;
using Inventory.Domain.Exceptions;

namespace Inventory.Application.GrnMapping;

/// <summary>
/// Pure transformation service (no HTTP, no DB).
/// Maps arbitrary external JSON payloads into GrnApiResponse landing rows.
/// </summary>
public static class GrnResponseMapper
{
    public static List<GrnApiResponse> Map(
        string json,
        IReadOnlyCollection<ApiFieldMapping> mappings,
        string? rootPath,
        int branchId,
        int apiCredentialId,
        Guid fetchBatchId,
        int? fetchedBy,
        DateTime fetchedAt)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new DomainException("Response JSON payload cannot be empty.");

        using var document = JsonDocument.Parse(json);
        var rootElement = document.RootElement;

        // Navigate response root path if specified (e.g. "data.items")
        if (!string.IsNullOrWhiteSpace(rootPath))
        {
            var segments = rootPath.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var segment in segments)
            {
                if (rootElement.ValueKind != JsonValueKind.Object || !TryGetPropertyCaseInsensitive(rootElement, segment, out var child))
                {
                    throw new DomainException($"ResponseRootPath '{rootPath}' segment '{segment}' was not found in the response payload.");
                }
                rootElement = child;
            }
        }

        var results = new List<GrnApiResponse>();

        if (rootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in rootElement.EnumerateArray())
            {
                results.Add(MapSingleItem(item, mappings, branchId, apiCredentialId, fetchBatchId, fetchedBy, fetchedAt));
            }
        }
        else if (rootElement.ValueKind == JsonValueKind.Object)
        {
            results.Add(MapSingleItem(rootElement, mappings, branchId, apiCredentialId, fetchBatchId, fetchedBy, fetchedAt));
        }
        else
        {
            throw new DomainException("Target JSON payload must be an array or an object.");
        }

        return results;
    }

    private static GrnApiResponse MapSingleItem(
        JsonElement itemElement,
        IReadOnlyCollection<ApiFieldMapping> mappings,
        int branchId,
        int apiCredentialId,
        Guid fetchBatchId,
        int? fetchedBy,
        DateTime fetchedAt)
    {
        var rawJson = itemElement.GetRawText();
        var row = new GrnApiResponse
        {
            BranchId = branchId,
            ApiCredentialId = apiCredentialId,
            FetchBatchId = fetchBatchId,
            FetchedAt = fetchedAt,
            FetchedBy = fetchedBy,
            RawJson = rawJson
        };

        var rowErrors = new List<string>();

        foreach (var mapping in mappings)
        {
            var hasValue = TryExtractValue(itemElement, mapping.SourceField, out var stringValue);

            if (!hasValue || string.IsNullOrWhiteSpace(stringValue))
            {
                stringValue = mapping.DefaultValue;
            }

            if (string.IsNullOrWhiteSpace(stringValue))
            {
                if (mapping.IsRequired)
                {
                    rowErrors.Add($"Required field '{mapping.TargetColumn}' ({mapping.SourceField}) is missing.");
                }
                continue;
            }

            ApplyMapping(row, mapping, stringValue, rowErrors);
        }

        if (string.IsNullOrWhiteSpace(row.GrnNo))
        {
            rowErrors.Add("GrnNo is required but was not provided or empty.");
        }

        if (rowErrors.Count > 0)
        {
            row.Status = GrnResponseStatus.Error;
            row.ErrorMessage = string.Join("; ", rowErrors);
        }
        else
        {
            row.Status = GrnResponseStatus.New;
            row.SourceKey = null; // De-duplication is PARKED per domain decisions
        }

        return row;
    }

    private static void ApplyMapping(
        GrnApiResponse row,
        ApiFieldMapping mapping,
        string value,
        List<string> rowErrors)
    {
        switch (mapping.TargetColumn)
        {
            case TargetColumn.GrnNo:
                row.GrnNo = value.Trim();
                break;

            case TargetColumn.GrnYear:
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var yr))
                    row.GrnYear = yr;
                else
                    rowErrors.Add($"Invalid integer for GrnYear: '{value}'.");
                break;

            case TargetColumn.GrnItemNo:
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var itemNo))
                    row.GrnItemNo = itemNo;
                else
                    rowErrors.Add($"Invalid integer for GrnItemNo: '{value}'.");
                break;

            case TargetColumn.GrnDate:
                var formats = !string.IsNullOrWhiteSpace(mapping.Format)
                    ? mapping.Format
                        .Split('|', StringSplitOptions.RemoveEmptyEntries)
                        .Select(f => f.Trim())
                        .Where(f => !string.IsNullOrEmpty(f))
                        .ToArray()
                    : Array.Empty<string>();

                if (formats.Length > 0)
                {
                    DateTime? parsedDate = null;
                    foreach (var fmt in formats)
                    {
                        if (DateTime.TryParseExact(value.Trim(), fmt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dtExact))
                        {
                            parsedDate = dtExact;
                            break;
                        }
                    }

                    if (parsedDate.HasValue)
                    {
                        row.GrnDate = parsedDate.Value;
                    }
                    else
                    {
                        var displayVal = value.Length > 40 ? value[..40] : value;
                        var formatLabel = formats.Length > 1 ? "formats" : "format";
                        var formatsText = string.Join(" | ", formats);
                        rowErrors.Add($"Failed to parse GrnDate for field '{mapping.SourceField}' with value '{displayVal}' using {formatLabel} '{formatsText}'.");
                    }
                }
                else
                {
                    if (DateTime.TryParse(value.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                    {
                        row.GrnDate = dt;
                    }
                    else
                    {
                        var displayVal = value.Length > 40 ? value[..40] : value;
                        rowErrors.Add($"Failed to parse GrnDate for field '{mapping.SourceField}' with value '{displayVal}'.");
                    }
                }
                break;

            case TargetColumn.InvoiceNo:
                row.InvoiceNo = value.Trim();
                break;

            case TargetColumn.InvoiceDate:
                if (!string.IsNullOrWhiteSpace(mapping.Format))
                {
                    if (DateOnly.TryParseExact(value.Trim(), mapping.Format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dOnlyExact))
                        row.InvoiceDate = dOnlyExact;
                    else
                        rowErrors.Add($"Failed to parse InvoiceDate '{value}' using format '{mapping.Format}'.");
                }
                else
                {
                    if (DateOnly.TryParse(value.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var dOnly))
                        row.InvoiceDate = dOnly;
                    else
                        rowErrors.Add($"Failed to parse InvoiceDate '{value}'.");
                }
                break;

            case TargetColumn.Qty:
                if (decimal.TryParse(value.Trim(), NumberStyles.Number | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var qty))
                    row.Qty = qty;
                else
                    rowErrors.Add($"Invalid decimal for Qty: '{value}'.");
                break;

            case TargetColumn.Uom:
                row.Uom = value.Trim();
                break;

            case TargetColumn.MaterialCode:
                row.MaterialCode = value.Trim();
                break;

            case TargetColumn.PartName:
                row.PartName = value.Trim();
                break;

            case TargetColumn.Description:
                row.Description = value.Trim();
                break;

            case TargetColumn.Plant:
                row.Plant = value.Trim();
                break;

            case TargetColumn.StorageLocation:
                row.StorageLocation = value.Trim();
                break;

            case TargetColumn.BatchNo:
                row.BatchNo = value.Trim();
                break;

            case TargetColumn.PoNo:
                row.PoNo = value.Trim();
                break;

            case TargetColumn.PoItem:
                row.PoItem = value.Trim();
                break;

            case TargetColumn.VendorCode:
                row.VendorCode = value.Trim();
                break;

            case TargetColumn.VendorName:
                row.VendorName = value.Trim();
                break;
        }
    }

    private static bool TryExtractValue(JsonElement element, string sourceField, out string? value)
    {
        value = null;
        if (element.ValueKind != JsonValueKind.Object)
            return false;

        // Support direct key match first (supports keys containing dots or spaces like "GRN Date")
        if (TryGetPropertyCaseInsensitive(element, sourceField, out var directProp))
        {
            value = ExtractString(directProp);
            return true;
        }

        // Support dotted paths (e.g. "vendor.code")
        if (sourceField.Contains('.'))
        {
            var segments = sourceField.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var current = element;
            foreach (var segment in segments)
            {
                if (current.ValueKind != JsonValueKind.Object || !TryGetPropertyCaseInsensitive(current, segment, out var child))
                {
                    return false;
                }
                current = child;
            }
            value = ExtractString(current);
            return true;
        }

        return false;
    }

    private static bool TryGetPropertyCaseInsensitive(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static string? ExtractString(JsonElement prop)
    {
        return prop.ValueKind switch
        {
            JsonValueKind.String => prop.GetString(),
            JsonValueKind.Number => prop.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => null,
            JsonValueKind.Undefined => null,
            _ => prop.GetRawText()
        };
    }
}
