using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Inventory.Application.ApiCredentials.DTOs;
using Inventory.Application.ApiCredentials.Services;
using Inventory.Application.Common.Interfaces;
using Inventory.Application.GrnMapping;
using Inventory.Application.GrnResponses.DTOs;
using Inventory.Domain.Entities;
using Inventory.Domain.Enums;
using Inventory.Domain.Exceptions;
using Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Inventory.Infrastructure.Services;

public class ApiCredentialService : IApiCredentialService
{
    private readonly InventoryDbContext _context;
    private readonly ISecretProtector _secretProtector;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<ApiCredentialService> _logger;

    public const string HttpClientName = "ApiCredentialClient";
    public const int MaxResponseSizeBytes = 10 * 1024 * 1024; // 10 MB
    public const int MaxRowCount = 5000;

    private static readonly Regex PlantRegex = new("^[A-Za-z0-9_-]{1,10}$", RegexOptions.Compiled);

    public ApiCredentialService(
        InventoryDbContext context,
        ISecretProtector secretProtector,
        IHttpClientFactory httpClientFactory,
        IClock clock,
        ICurrentUser currentUser,
        IHostEnvironment environment,
        ILogger<ApiCredentialService> logger)
    {
        _context = context;
        _secretProtector = secretProtector;
        _httpClientFactory = httpClientFactory;
        _clock = clock;
        _currentUser = currentUser;
        _environment = environment;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ApiCredentialDto>> GetAllAsync(CancellationToken ct = default)
    {
        return await _context.ApiCredentials
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => MapToDto(c))
            .ToListAsync(ct);
    }

    public async Task<ApiCredentialDto> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var credential = await _context.ApiCredentials
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, ct);

        if (credential == null)
            throw new NotFoundException(nameof(ApiCredential), id);

        return MapToDto(credential);
    }

    public async Task<ApiCredentialDto> CreateAsync(CreateApiCredentialDto dto, CancellationToken ct = default)
    {
        var credential = new ApiCredential
        {
            Name = dto.Name.Trim(),
            BaseUrl = dto.BaseUrl.Trim(),
            HttpMethod = string.IsNullOrWhiteSpace(dto.HttpMethod) ? "GET" : dto.HttpMethod.Trim().ToUpperInvariant(),
            AuthType = dto.AuthType,
            UserName = string.IsNullOrWhiteSpace(dto.UserName) ? null : dto.UserName.Trim(),
            HeaderName = string.IsNullOrWhiteSpace(dto.HeaderName) ? null : dto.HeaderName.Trim(),
            ExtraHeadersJson = string.IsNullOrWhiteSpace(dto.ExtraHeadersJson) ? null : dto.ExtraHeadersJson.Trim(),
            TimeoutSeconds = dto.TimeoutSeconds > 0 ? dto.TimeoutSeconds : 30,
            ContentType = string.IsNullOrWhiteSpace(dto.ContentType) ? "application/json" : dto.ContentType.Trim(),
            RequestBodyTemplate = dto.RequestBodyTemplate,
            ResponseRootPath = string.IsNullOrWhiteSpace(dto.ResponseRootPath) ? null : dto.ResponseRootPath.Trim(),
            IsActive = true
        };

        if (!string.IsNullOrWhiteSpace(dto.Secret))
        {
            credential.SecretEncrypted = _secretProtector.Encrypt(dto.Secret);
        }

        credential.Validate();

        _context.ApiCredentials.Add(credential);
        await _context.SaveChangesAsync(ct);

        return MapToDto(credential);
    }

    public async Task<ApiCredentialDto> UpdateAsync(int id, UpdateApiCredentialDto dto, CancellationToken ct = default)
    {
        var credential = await _context.ApiCredentials.FindAsync([id], ct);

        if (credential == null)
            throw new NotFoundException(nameof(ApiCredential), id);

        credential.Name = dto.Name.Trim();
        credential.BaseUrl = dto.BaseUrl.Trim();
        credential.HttpMethod = string.IsNullOrWhiteSpace(dto.HttpMethod) ? "GET" : dto.HttpMethod.Trim().ToUpperInvariant();
        credential.AuthType = dto.AuthType;
        credential.UserName = string.IsNullOrWhiteSpace(dto.UserName) ? null : dto.UserName.Trim();
        credential.HeaderName = string.IsNullOrWhiteSpace(dto.HeaderName) ? null : dto.HeaderName.Trim();
        credential.ExtraHeadersJson = string.IsNullOrWhiteSpace(dto.ExtraHeadersJson) ? null : dto.ExtraHeadersJson.Trim();
        credential.TimeoutSeconds = dto.TimeoutSeconds > 0 ? dto.TimeoutSeconds : 30;
        credential.ContentType = string.IsNullOrWhiteSpace(dto.ContentType) ? "application/json" : dto.ContentType.Trim();
        credential.RequestBodyTemplate = dto.RequestBodyTemplate;
        credential.ResponseRootPath = string.IsNullOrWhiteSpace(dto.ResponseRootPath) ? null : dto.ResponseRootPath.Trim();
        credential.IsActive = dto.IsActive;

        if (!string.IsNullOrWhiteSpace(dto.Secret))
        {
            credential.SecretEncrypted = _secretProtector.Encrypt(dto.Secret);
        }

        credential.Validate();

        await _context.SaveChangesAsync(ct);

        return MapToDto(credential);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var credential = await _context.ApiCredentials.FindAsync([id], ct);

        if (credential == null)
            throw new NotFoundException(nameof(ApiCredential), id);

        credential.Deactivate();
        await _context.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ApiFieldMappingDto>> GetMappingsAsync(int id, CancellationToken ct = default)
    {
        var credentialExists = await _context.ApiCredentials.AnyAsync(c => c.Id == id, ct);
        if (!credentialExists)
            throw new NotFoundException(nameof(ApiCredential), id);

        return await _context.ApiFieldMappings
            .AsNoTracking()
            .Where(m => m.ApiCredentialId == id)
            .OrderBy(m => m.TargetColumn)
            .Select(m => new ApiFieldMappingDto(
                m.Id,
                m.ApiCredentialId,
                m.SourceField,
                m.TargetColumn,
                m.Format,
                m.DefaultValue,
                m.IsRequired))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ApiFieldMappingDto>> ReplaceMappingsAsync(
        int id,
        IReadOnlyList<UpsertApiFieldMappingDto> mappings,
        CancellationToken ct = default)
    {
        var credential = await _context.ApiCredentials
            .Include(c => c.FieldMappings)
            .FirstOrDefaultAsync(c => c.Id == id, ct);

        if (credential == null)
            throw new NotFoundException(nameof(ApiCredential), id);

        var duplicates = mappings
            .GroupBy(m => m.TargetColumn)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicates.Count > 0)
        {
            throw new DomainException($"Duplicate target column mappings are not allowed: {string.Join(", ", duplicates)}.");
        }

        credential.FieldMappings.Clear();

        foreach (var m in mappings)
        {
            if (string.IsNullOrWhiteSpace(m.SourceField))
                throw new DomainException($"SourceField is required for mapping {m.TargetColumn}.");

            credential.FieldMappings.Add(new ApiFieldMapping
            {
                ApiCredentialId = id,
                SourceField = m.SourceField.Trim(),
                TargetColumn = m.TargetColumn,
                Format = string.IsNullOrWhiteSpace(m.Format) ? null : m.Format.Trim(),
                DefaultValue = string.IsNullOrWhiteSpace(m.DefaultValue) ? null : m.DefaultValue.Trim(),
                IsRequired = m.IsRequired
            });
        }

        await _context.SaveChangesAsync(ct);

        return credential.FieldMappings
            .Select(m => new ApiFieldMappingDto(
                m.Id,
                m.ApiCredentialId,
                m.SourceField,
                m.TargetColumn,
                m.Format,
                m.DefaultValue,
                m.IsRequired))
            .ToList();
    }

    public async Task<ApiTestResultDto> TestConnectionAsync(int id, CancellationToken ct = default)
    {
        var credential = await _context.ApiCredentials
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, ct);

        if (credential == null)
            throw new NotFoundException(nameof(ApiCredential), id);

        var (success, statusCode, reasonPhrase, elapsedMs, body) = await ExecuteRequestInternalAsync(credential, null, null, null, ct);

        string? preview = null;
        if (!string.IsNullOrEmpty(body))
        {
            preview = body.Length > 2000 ? body[..2000] : body;
        }

        return new ApiTestResultDto(success, statusCode, reasonPhrase, elapsedMs, preview);
    }

    public async Task<MappingPreviewResultDto> PreviewMappingAsync(int id, CancellationToken ct = default)
    {
        var credential = await _context.ApiCredentials
            .Include(c => c.FieldMappings)
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, ct);

        if (credential == null)
            throw new NotFoundException(nameof(ApiCredential), id);

        if (credential.FieldMappings.Count == 0)
        {
            return new MappingPreviewResultDto(false, 0, 0, new List<GrnApiResponsePreviewDto>(), "No field mappings defined for this credential.");
        }

        var (success, statusCode, reasonPhrase, _, body) = await ExecuteRequestInternalAsync(credential, null, null, null, ct);

        if (!success || string.IsNullOrWhiteSpace(body))
        {
            return new MappingPreviewResultDto(false, 0, 0, new List<GrnApiResponsePreviewDto>(), $"API request failed with status {statusCode} ({reasonPhrase}).");
        }

        try
        {
            var mappedRows = GrnResponseMapper.Map(
                body,
                credential.FieldMappings.ToList(),
                credential.ResponseRootPath,
                credential.BranchId,
                credential.Id,
                Guid.NewGuid(),
                _currentUser.UserId,
                _clock.UtcNow);

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

            return new MappingPreviewResultDto(true, mappedRows.Count, previewRows.Count, previewRows, null);
        }
        catch (Exception ex)
        {
            return new MappingPreviewResultDto(false, 0, 0, new List<GrnApiResponsePreviewDto>(), $"Mapper failure: {ex.Message}");
        }
    }

    public async Task<FetchResultDto> FetchAsync(int id, FetchGrnRequestDto request, CancellationToken ct = default)
    {
        var credential = await _context.ApiCredentials
            .Include(c => c.FieldMappings)
            .FirstOrDefaultAsync(c => c.Id == id, ct);

        if (credential == null)
            throw new NotFoundException(nameof(ApiCredential), id);

        if (!credential.IsActive)
        {
            throw new DomainException($"ApiCredential '{credential.Name}' is inactive.");
        }

        if (credential.FieldMappings.Count == 0)
        {
            throw new DomainException($"ApiCredential '{credential.Name}' has no field mappings configured.");
        }

        // Validate inputs
        ValidateFetchInputs(request.FromDate, request.ToDate, request.Plant);

        // Execute request with 10 MB size cap
        var (success, statusCode, reasonPhrase, _, body) = await ExecuteRequestInternalAsync(
            credential,
            request.FromDate,
            request.ToDate,
            request.Plant,
            ct);

        if (!success || string.IsNullOrWhiteSpace(body))
        {
            throw new DomainException($"API request failed with status {statusCode} ({reasonPhrase}).");
        }

        var fetchBatchId = Guid.NewGuid();
        var fetchedAt = _clock.UtcNow;
        var fetchedBy = _currentUser.UserId;

        List<GrnApiResponse> mappedRows;
        try
        {
            mappedRows = GrnResponseMapper.Map(
                body,
                credential.FieldMappings.ToList(),
                credential.ResponseRootPath,
                credential.BranchId,
                credential.Id,
                fetchBatchId,
                fetchedBy,
                fetchedAt);
        }
        catch (Exception ex)
        {
            throw new DomainException($"Failed to map external API response: {ex.Message}", ex);
        }

        if (mappedRows.Count > MaxRowCount)
        {
            throw new DomainException($"Response row count ({mappedRows.Count}) exceeded maximum allowed limit of {MaxRowCount} rows.");
        }

        // Ensure SourceKey is null and audit fields are populated
        foreach (var row in mappedRows)
        {
            row.SourceKey = null;
            row.FetchBatchId = fetchBatchId;
            row.FetchedAt = fetchedAt;
            row.FetchedBy = fetchedBy;
        }

        // Save in one single transaction
        if (_context.Database.IsRelational())
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(ct);
            _context.GrnApiResponses.AddRange(mappedRows);
            await _context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        else
        {
            _context.GrnApiResponses.AddRange(mappedRows);
            await _context.SaveChangesAsync(ct);
        }

        var errorCount = mappedRows.Count(r => r.Status == GrnResponseStatus.Error);
        return new FetchResultDto(fetchBatchId, mappedRows.Count, mappedRows.Count, errorCount);
    }

    private void ValidateFetchInputs(string? fromDate, string? toDate, string? plant)
    {
        if (!string.IsNullOrWhiteSpace(fromDate))
        {
            if (!DateTime.TryParseExact(fromDate.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            {
                throw new DomainException("fromDate must be in ISO yyyy-MM-dd format.");
            }
        }

        if (!string.IsNullOrWhiteSpace(toDate))
        {
            if (!DateTime.TryParseExact(toDate.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            {
                throw new DomainException("toDate must be in ISO yyyy-MM-dd format.");
            }
        }

        if (!string.IsNullOrWhiteSpace(plant))
        {
            if (!PlantRegex.IsMatch(plant.Trim()))
            {
                throw new DomainException("plant must match ^[A-Za-z0-9_-]{1,10}$.");
            }
        }
    }

    private async Task<(bool Success, int StatusCode, string ReasonPhrase, long ElapsedMs, string? Body)> ExecuteRequestInternalAsync(
        ApiCredential credential,
        string? fromDate,
        string? toDate,
        string? plant,
        CancellationToken ct)
    {
        credential.Validate();

        // SSRF / Host Guard
        ValidateUrlHostSecurity(credential.BaseUrl);

        var client = _httpClientFactory.CreateClient(HttpClientName);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(credential.TimeoutSeconds));

        using var request = new HttpRequestMessage(new HttpMethod(credential.HttpMethod), credential.BaseUrl);

        // Setup authentication
        var secretPlain = !string.IsNullOrEmpty(credential.SecretEncrypted)
            ? _secretProtector.Decrypt(credential.SecretEncrypted)
            : string.Empty;

        switch (credential.AuthType)
        {
            case AuthType.ApiKey when !string.IsNullOrEmpty(secretPlain):
                var headerName = string.IsNullOrWhiteSpace(credential.HeaderName) ? "X-API-KEY" : credential.HeaderName;
                request.Headers.TryAddWithoutValidation(headerName, secretPlain);
                break;

            case AuthType.Bearer when !string.IsNullOrEmpty(secretPlain):
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secretPlain);
                break;

            case AuthType.Basic when !string.IsNullOrEmpty(credential.UserName) || !string.IsNullOrEmpty(secretPlain):
                var credentials = $"{credential.UserName ?? ""}:{secretPlain}";
                var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", base64);
                break;
        }

        // Apply ExtraHeadersJson if present
        if (!string.IsNullOrWhiteSpace(credential.ExtraHeadersJson))
        {
            try
            {
                var headers = JsonSerializer.Deserialize<Dictionary<string, string>>(credential.ExtraHeadersJson);
                if (headers != null)
                {
                    foreach (var (k, v) in headers)
                    {
                        request.Headers.TryAddWithoutValidation(k, v);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse ExtraHeadersJson for credential {CredentialId}", credential.Id);
            }
        }

        // Fill template placeholders
        if (!string.IsNullOrWhiteSpace(credential.RequestBodyTemplate))
        {
            var isJson = (credential.ContentType ?? "").Contains("json", StringComparison.OrdinalIgnoreCase);

            var defaultFrom = fromDate ?? _clock.UtcNow.AddDays(-7).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var defaultTo = toDate ?? _clock.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var defaultPlant = plant ?? "1000";

            string escapedFrom = isJson ? EscapeJsonValue(defaultFrom) : Uri.EscapeDataString(defaultFrom);
            string escapedTo = isJson ? EscapeJsonValue(defaultTo) : Uri.EscapeDataString(defaultTo);
            string escapedPlant = isJson ? EscapeJsonValue(defaultPlant) : Uri.EscapeDataString(defaultPlant);

            var body = credential.RequestBodyTemplate
                .Replace("{{fromDate}}", escapedFrom)
                .Replace("{{toDate}}", escapedTo)
                .Replace("{{plant}}", escapedPlant);

            request.Content = new StringContent(body, Encoding.UTF8, credential.ContentType ?? "application/json");
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            stopwatch.Stop();

            // Stream response reading with 10 MB cap
            var content = await ReadContentWithLimitAsync(response.Content, MaxResponseSizeBytes, cts.Token);
            return (response.IsSuccessStatusCode, (int)response.StatusCode, response.ReasonPhrase ?? response.StatusCode.ToString(), stopwatch.ElapsedMilliseconds, content);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            stopwatch.Stop();
            return (false, 408, "Request Timeout", stopwatch.ElapsedMilliseconds, "The request timed out according to the configured TimeoutSeconds.");
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            var statusCode = ex.StatusCode.HasValue ? (int)ex.StatusCode.Value : 502;
            return (false, statusCode, ex.Message, stopwatch.ElapsedMilliseconds, null);
        }
        catch (DomainException)
        {
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return (false, 500, ex.Message, stopwatch.ElapsedMilliseconds, null);
        }
    }

    private void ValidateUrlHostSecurity(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            throw new DomainException("Invalid URL format.");

        var host = uri.DnsSafeHost.ToLowerInvariant();

        // Localhost / loopback is permitted ONLY in Development environment
        var isLocal = host == "localhost" || host == "127.0.0.1" || host == "::1";

        if (isLocal)
        {
            if (!_environment.IsDevelopment())
            {
                throw new DomainException("Localhost and loopback destinations are forbidden outside the Development environment.");
            }
            return;
        }

        // In production, block cloud metadata endpoints and private IP ranges
        if (IPAddress.TryParse(host, out var ip))
        {
            if (IPAddress.IsLoopback(ip) || ip.ToString() == "169.254.169.254") // AWS/Azure metadata service
            {
                throw new DomainException("Access to link-local/metadata IP addresses is forbidden.");
            }

            var bytes = ip.GetAddressBytes();
            if (bytes.Length == 4)
            {
                // 10.0.0.0/8
                if (bytes[0] == 10 && !_environment.IsDevelopment())
                    throw new DomainException("Private IP addresses are forbidden.");

                // 172.16.0.0/12
                if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31 && !_environment.IsDevelopment())
                    throw new DomainException("Private IP addresses are forbidden.");

                // 192.168.0.0/16
                if (bytes[0] == 192 && bytes[1] == 168 && !_environment.IsDevelopment())
                    throw new DomainException("Private IP addresses are forbidden.");
            }
        }
    }

    private static async Task<string> ReadContentWithLimitAsync(HttpContent content, int maxBytes, CancellationToken ct)
    {
        await using var stream = await content.ReadAsStreamAsync(ct);
        using var memoryStream = new MemoryStream();
        var buffer = new byte[81920];
        int totalBytesRead = 0;
        int bytesRead;

        while ((bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
        {
            totalBytesRead += bytesRead;
            if (totalBytesRead > maxBytes)
            {
                throw new DomainException($"Response size exceeded maximum allowed limit of 10 MB.");
            }
            await memoryStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
        }

        return Encoding.UTF8.GetString(memoryStream.ToArray());
    }

    private static string EscapeJsonValue(string value)
    {
        return JavaScriptEncoder.Default.Encode(value);
    }

    private static ApiCredentialDto MapToDto(ApiCredential c)
    {
        return new ApiCredentialDto(
            c.Id,
            c.BranchId,
            c.Name,
            c.BaseUrl,
            c.HttpMethod,
            c.AuthType,
            c.UserName,
            c.HeaderName,
            !string.IsNullOrEmpty(c.SecretEncrypted),
            c.ExtraHeadersJson,
            c.TimeoutSeconds,
            c.ContentType,
            c.RequestBodyTemplate,
            c.ResponseRootPath,
            c.IsActive,
            c.CreatedAt,
            c.CreatedBy,
            c.ModifiedAt,
            c.ModifiedBy);
    }
}
