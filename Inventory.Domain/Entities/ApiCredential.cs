using Inventory.Domain.Common;
using Inventory.Domain.Enums;
using Inventory.Domain.Exceptions;

namespace Inventory.Domain.Entities;

public class ApiCredential : BranchScopedEntity
{
    public string Name { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string HttpMethod { get; set; } = "GET";
    public AuthType AuthType { get; set; } = AuthType.None;
    public string? UserName { get; set; }
    public string? HeaderName { get; set; }
    public string? SecretEncrypted { get; set; }
    public string? ExtraHeadersJson { get; set; }
    public int TimeoutSeconds { get; set; } = 30;
    public string? ContentType { get; set; }
    public string? RequestBodyTemplate { get; set; }
    public string? ResponseRootPath { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<ApiFieldMapping> FieldMappings { get; set; } = new List<ApiFieldMapping>();

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
            throw new DomainException("ApiCredential name is required.");

        if (string.IsNullOrWhiteSpace(BaseUrl))
            throw new DomainException("BaseUrl is required.");

        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new DomainException("BaseUrl must be a valid HTTP or HTTPS URL.");
        }

        if (TimeoutSeconds <= 0)
            throw new DomainException("TimeoutSeconds must be greater than 0.");
    }

    public void Deactivate()
    {
        IsActive = false;
    }

    public void Activate()
    {
        IsActive = true;
    }
}
