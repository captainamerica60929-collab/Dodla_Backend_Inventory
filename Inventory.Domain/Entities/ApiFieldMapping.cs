using Inventory.Domain.Common;
using Inventory.Domain.Enums;

namespace Inventory.Domain.Entities;

public class ApiFieldMapping : BaseEntity
{
    public int ApiCredentialId { get; set; }
    public ApiCredential? ApiCredential { get; set; }

    /// <summary>
    /// Source JSON key or dotted path (case-insensitive, can contain spaces like "GRN Date").
    /// </summary>
    public string SourceField { get; set; } = string.Empty;

    /// <summary>
    /// Whitelisted target column to map into.
    /// </summary>
    public TargetColumn TargetColumn { get; set; }

    /// <summary>
    /// Optional format string (e.g. "yyyy-MM-dd hh:mm tt" for dates).
    /// </summary>
    public string? Format { get; set; }

    /// <summary>
    /// Optional default fallback value if source field is missing or null.
    /// </summary>
    public string? DefaultValue { get; set; }

    /// <summary>
    /// Whether this field is mandatory for creating a valid GrnApiResponse.
    /// </summary>
    public bool IsRequired { get; set; }
}
