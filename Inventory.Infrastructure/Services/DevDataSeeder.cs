using Inventory.Application.Common.Interfaces;
using Inventory.Domain.Entities;
using Inventory.Domain.Enums;
using Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Inventory.Infrastructure.Services;

/// <summary>
/// Development-only seeder for the Mock GRN API credential.
/// Uses SystemCurrentUser scope so the branch guard is bypassed.
/// </summary>
public class DevDataSeeder
{
    private readonly InventoryDbContext _context;
    private readonly ISecretProtector _secretProtector;
    private readonly IHostEnvironment? _environment;
    private readonly ILogger<DevDataSeeder> _logger;

    public DevDataSeeder(
        InventoryDbContext context,
        ISecretProtector secretProtector,
        IHostEnvironment? environment,
        ILogger<DevDataSeeder> logger)
    {
        _context = context;
        _secretProtector = secretProtector;
        _environment = environment;
        _logger = logger;
    }

    public DevDataSeeder(
        InventoryDbContext context,
        ISecretProtector secretProtector,
        ILogger<DevDataSeeder> logger) : this(context, secretProtector, null, logger)
    {
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        if (_environment != null && !_environment.IsDevelopment())
            return;

        // Idempotent check
        var exists = await _context.ApiCredentials
            .IgnoreQueryFilters()
            .AnyAsync(c => c.Name == "Mock GRN API (dev)", ct);

        if (exists)
            return;

        _logger.LogInformation("Seeding Development Mock GRN API credential and field mappings...");

        var credential = new ApiCredential
        {
            BranchId = 1,   // explicitly set; system scope bypasses branch guard
            Name = "Mock GRN API (dev)",
            BaseUrl = "http://localhost:5114/api/dev/mock-grn",
            HttpMethod = "POST",
            AuthType = AuthType.ApiKey,
            HeaderName = "X-Api-Key",
            SecretEncrypted = _secretProtector.Encrypt("dev-mock-key"),
            ContentType = "application/json",
            RequestBodyTemplate = "{\"fromDate\":\"{{fromDate}}\",\"toDate\":\"{{toDate}}\",\"plant\":\"{{plant}}\"}",
            ResponseRootPath = "data.items",
            TimeoutSeconds = 30,
            IsActive = true
        };

        credential.FieldMappings.Add(new ApiFieldMapping { SourceField = "GRNNo",      TargetColumn = TargetColumn.GrnNo,      IsRequired = true });
        credential.FieldMappings.Add(new ApiFieldMapping { SourceField = "GRN Date",   TargetColumn = TargetColumn.GrnDate,    Format = "yyyy-MM-dd hh:mm tt", IsRequired = false });
        credential.FieldMappings.Add(new ApiFieldMapping { SourceField = "PartName",   TargetColumn = TargetColumn.PartName,   IsRequired = false });
        credential.FieldMappings.Add(new ApiFieldMapping { SourceField = "InvoiceNo",  TargetColumn = TargetColumn.InvoiceNo,  IsRequired = false });
        credential.FieldMappings.Add(new ApiFieldMapping { SourceField = "Description",TargetColumn = TargetColumn.Description,IsRequired = false });
        credential.FieldMappings.Add(new ApiFieldMapping { SourceField = "Qty",        TargetColumn = TargetColumn.Qty,        IsRequired = true });
        credential.FieldMappings.Add(new ApiFieldMapping { SourceField = "Plant",      TargetColumn = TargetColumn.Plant,      IsRequired = false });

        _context.ApiCredentials.Add(credential);
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation("Mock GRN API credential seeded successfully.");
    }
}
