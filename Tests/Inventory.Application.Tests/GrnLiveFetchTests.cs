using System.Net;
using System.Text;
using Inventory.Application.ApiCredentials.DTOs;
using Inventory.Application.Common.Interfaces;
using Inventory.Application.GrnResponses.DTOs;
using Inventory.Domain.Entities;
using Inventory.Domain.Enums;
using Inventory.Domain.Exceptions;
using Inventory.Infrastructure.Persistence;
using Inventory.Infrastructure.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Inventory.Application.Tests;

public class GrnLiveFetchTests
{
    private readonly ISecretProtector _protector = new SecretProtector(new EphemeralDataProtectionProvider());

    private DbContextOptions<InventoryDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<InventoryDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    [Fact]
    public async Task FetchAsync_FullFetchAgainstMockResponse_ShouldSave10NewAnd2ErrorRows()
    {
        // Arrange
        var options = CreateOptions();
        var user = new FakeCurrentUser { UserId = 5, BranchId = 1 };
        var clock = new FakeClock();

        using (var setup = new InventoryDbContext(options, user, clock))
        {
            var cred = new ApiCredential
            {
                Id = 1,
                BranchId = 1,
                Name = "Mock GRN API (dev)",
                BaseUrl = "http://localhost:5114/api/dev/mock-grn",
                HttpMethod = "POST",
                AuthType = AuthType.ApiKey,
                HeaderName = "X-Api-Key",
                SecretEncrypted = _protector.Encrypt("dev-mock-key"),
                ContentType = "application/json",
                RequestBodyTemplate = "{\"fromDate\":\"{{fromDate}}\",\"toDate\":\"{{toDate}}\",\"plant\":\"{{plant}}\"}",
                ResponseRootPath = "data.items",
                TimeoutSeconds = 30,
                IsActive = true
            };

            cred.FieldMappings.Add(new ApiFieldMapping { TargetColumn = TargetColumn.GrnNo, SourceField = "GRNNo", IsRequired = true });
            cred.FieldMappings.Add(new ApiFieldMapping { TargetColumn = TargetColumn.GrnDate, SourceField = "GRN Date", Format = "yyyy-MM-dd hh:mm tt" });
            cred.FieldMappings.Add(new ApiFieldMapping { TargetColumn = TargetColumn.PartName, SourceField = "PartName" });
            cred.FieldMappings.Add(new ApiFieldMapping { TargetColumn = TargetColumn.InvoiceNo, SourceField = "InvoiceNo" });
            cred.FieldMappings.Add(new ApiFieldMapping { TargetColumn = TargetColumn.Description, SourceField = "Description" });
            cred.FieldMappings.Add(new ApiFieldMapping { TargetColumn = TargetColumn.Qty, SourceField = "Qty", IsRequired = true });
            cred.FieldMappings.Add(new ApiFieldMapping { TargetColumn = TargetColumn.Plant, SourceField = "Plant" });

            setup.ApiCredentials.Add(cred);
            await setup.SaveChangesAsync();
        }

        // Mock 12-item response exactly as MockGrnController generates (10 valid, row 11 bad qty, row 12 missing GRNNo)
        const string mockJsonResponse = """
        {
          "status": "OK",
          "data": {
            "items": [
              { "GRNNo": "50001001", "GRN Date": "2026-10-01 09:30 AM", "PartName": "Motor Rotor", "InvoiceNo": "INV-1001", "Description": "Rotor Part", "Qty": 50, "Plant": "1000" },
              { "GRNNo": "50001002", "GRN Date": "2026-10-02 10:15 AM", "PartName": "Stator Frame", "InvoiceNo": "INV-1002", "Description": "Stator Part", "Qty": 75, "Plant": "1000" },
              { "GRNNo": "50001003", "GRN Date": "2026-10-03 11:00 AM", "PartName": "Copper Wire Spool", "InvoiceNo": "INV-1003", "Description": "Wire Spool", "Qty": 120, "Plant": "1000" },
              { "GRNNo": "50001004", "GRN Date": "2026-10-04 02:45 PM", "PartName": "Terminal Box", "InvoiceNo": "INV-1004", "Description": "Terminal Box", "Qty": 30, "Plant": "1000" },
              { "GRNNo": "50001005", "GRN Date": "2026-10-05 04:20 PM", "PartName": "Ball Bearing 6205", "InvoiceNo": "INV-1005", "Description": "Deep groove ball bearing", "Qty": 200, "Plant": "1000" },
              { "GRNNo": "50002001", "GRN Date": "2026-10-06 08:30 AM", "PartName": "Cast Iron Flange", "InvoiceNo": "INV-2001", "Description": "Mounting Flange", "Qty": 40, "Plant": "2000" },
              { "GRNNo": "50002002", "GRN Date": "2026-10-07 01:10 PM", "PartName": "Cooling Fan Blade", "InvoiceNo": "INV-2002", "Description": "Plastic cooling fan", "Qty": 150, "Plant": "2000" },
              { "GRNNo": "50002003", "GRN Date": "2026-10-08 03:50 PM", "PartName": "Drive Shaft", "InvoiceNo": "INV-2003", "Description": "Steel shaft 28mm", "Qty": 60, "Plant": "2000" },
              { "GRNNo": "50002004", "GRN Date": "2026-10-09 11:30 AM", "PartName": "Gasket Seal", "InvoiceNo": "INV-2004", "Description": "Rubber seal ring", "Qty": 500, "Plant": "2000" },
              { "GRNNo": "50002005", "GRN Date": "2026-10-10 09:00 AM", "PartName": "Nameplate Decal", "InvoiceNo": "INV-2005", "Description": "Rating plate sticker", "Qty": 300, "Plant": "2000" },
              { "GRNNo": "50009991", "GRN Date": "2026-10-11 12:00 PM", "PartName": "Defective Qty Item", "InvoiceNo": "INV-9991", "Description": "Invalid qty sample", "Qty": "abc", "Plant": "1000" },
              { "GRN Date": "2026-10-12 02:00 PM", "PartName": "Missing GrnNo Item", "InvoiceNo": "INV-9992", "Description": "Missing GrnNo sample", "Qty": 10, "Plant": "2000" }
            ]
          }
        }
        """;

        var handler = new FakeHttpMessageHandler(req =>
        {
            Assert.True(req.Headers.Contains("X-Api-Key"));
            Assert.Equal("dev-mock-key", req.Headers.GetValues("X-Api-Key").First());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(mockJsonResponse, Encoding.UTF8, "application/json")
            };
        });

        using var context = new InventoryDbContext(options, user, clock);
        var service = new ApiCredentialService(
            context,
            _protector,
            new FakeHttpClientFactory(handler),
            clock,
            user,
            new DummyHostEnvironment(),
            NullLogger<ApiCredentialService>.Instance);

        // Act
        var result = await service.FetchAsync(1, new FetchGrnRequestDto("2026-10-01", "2026-10-15", "1000"));

        // Assert
        Assert.NotEqual(Guid.Empty, result.FetchBatchId);
        Assert.Equal(12, result.Total);
        Assert.Equal(12, result.Saved);
        Assert.Equal(2, result.ErrorRows);

        // Verify database persistence
        var dbRows = await context.GrnApiResponses
            .Where(r => r.FetchBatchId == result.FetchBatchId)
            .ToListAsync();

        Assert.Equal(12, dbRows.Count);
        Assert.Equal(10, dbRows.Count(r => r.Status == GrnResponseStatus.New));
        Assert.Equal(2, dbRows.Count(r => r.Status == GrnResponseStatus.Error));
        Assert.All(dbRows, r =>
        {
            Assert.Equal(1, r.BranchId);
            Assert.Null(r.SourceKey); // De-duplication parked
            Assert.NotEmpty(r.RawJson);
            Assert.Equal(user.UserId, r.FetchedBy);
            Assert.Equal(clock.UtcNow, r.FetchedAt);
        });
    }

    [Fact]
    public async Task FetchAsync_WithInactiveCredential_ShouldThrowDomainException422()
    {
        // Arrange
        var options = CreateOptions();
        var user = new FakeCurrentUser { UserId = 1, BranchId = 1 };
        var clock = new FakeClock();

        using (var setup = new InventoryDbContext(options, user, clock))
        {
            setup.ApiCredentials.Add(new ApiCredential
            {
                Id = 2,
                BranchId = 1,
                Name = "Inactive Credential",
                BaseUrl = "http://localhost:5114/api/dev/mock-grn",
                IsActive = false
            });
            await setup.SaveChangesAsync();
        }

        using var context = new InventoryDbContext(options, user, clock);
        var service = new ApiCredentialService(
            context,
            _protector,
            new FakeHttpClientFactory(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK))),
            clock,
            user,
            new DummyHostEnvironment(),
            NullLogger<ApiCredentialService>.Instance);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            service.FetchAsync(2, new FetchGrnRequestDto(null, null, null)));

        Assert.Contains("inactive", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FetchAsync_ResponseExceeding10MB_ShouldThrowDomainException()
    {
        // Arrange
        var options = CreateOptions();
        var user = new FakeCurrentUser { UserId = 1, BranchId = 1 };
        var clock = new FakeClock();

        using (var setup = new InventoryDbContext(options, user, clock))
        {
            var cred = new ApiCredential
            {
                Id = 3,
                BranchId = 1,
                Name = "Big Response Cred",
                BaseUrl = "http://localhost:5114/api/dev/mock-grn",
                IsActive = true
            };
            cred.FieldMappings.Add(new ApiFieldMapping { TargetColumn = TargetColumn.GrnNo, SourceField = "GRNNo" });
            setup.ApiCredentials.Add(cred);
            await setup.SaveChangesAsync();
        }

        // Generate response larger than 10 MB
        var bigString = new string('X', 10 * 1024 * 1024 + 100);
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(bigString, Encoding.UTF8, "application/json")
        });

        using var context = new InventoryDbContext(options, user, clock);
        var service = new ApiCredentialService(
            context,
            _protector,
            new FakeHttpClientFactory(handler),
            clock,
            user,
            new DummyHostEnvironment(),
            NullLogger<ApiCredentialService>.Instance);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            service.FetchAsync(3, new FetchGrnRequestDto(null, null, null)));

        Assert.Contains("10 MB", ex.Message);
    }
}
