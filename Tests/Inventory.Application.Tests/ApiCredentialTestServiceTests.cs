using System.Net;
using System.Text;
using Inventory.Application.ApiCredentials.Services;
using Inventory.Application.Common.Interfaces;
using Inventory.Domain.Entities;
using Inventory.Domain.Enums;
using Inventory.Infrastructure.Persistence;
using Inventory.Infrastructure.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Inventory.Application.Tests;

public class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public HttpRequestMessage? LastRequest { get; private set; }

    public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        _responder = responder;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        var response = _responder(request);
        return Task.FromResult(response);
    }
}

public class FakeHttpClientFactory : IHttpClientFactory
{
    private readonly HttpMessageHandler _handler;

    public FakeHttpClientFactory(HttpMessageHandler handler)
    {
        _handler = handler;
    }

    public HttpClient CreateClient(string name)
    {
        return new HttpClient(_handler, disposeHandler: false);
    }
}

public class ApiCredentialTestServiceTests
{
    private readonly ISecretProtector _protector = new SecretProtector(new EphemeralDataProtectionProvider());

    private DbContextOptions<InventoryDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<InventoryDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    [Fact]
    public async Task TestConnectionAsync_Success_ShouldReturn200AndBodyPreview()
    {
        // Arrange
        var options = CreateOptions();
        var user = new FakeCurrentUser { UserId = 1, BranchId = 1 };
        var clock = new FakeClock();

        using (var setup = new InventoryDbContext(options, user, clock))
        {
            setup.ApiCredentials.Add(new ApiCredential
            {
                Id = 10,
                BranchId = 1,
                Name = "Test Success",
                BaseUrl = "https://external.api/goods-receipt",
                HttpMethod = "GET",
                AuthType = AuthType.Bearer,
                SecretEncrypted = _protector.Encrypt("token-xyz"),
                TimeoutSeconds = 5
            });
            await setup.SaveChangesAsync();
        }

        var longBody = new string('A', 2500);
        var handler = new FakeHttpMessageHandler(req =>
        {
            Assert.Equal("Bearer", req.Headers.Authorization?.Scheme);
            Assert.Equal("token-xyz", req.Headers.Authorization?.Parameter);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(longBody, Encoding.UTF8, "application/json")
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
        var result = await service.TestConnectionAsync(10);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(200, result.StatusCode);
        Assert.NotNull(result.BodyPreview);
        Assert.Equal(2000, result.BodyPreview.Length);
        Assert.True(result.ElapsedMs >= 0);
    }

    [Fact]
    public async Task TestConnectionAsync_401Unauthorized_ShouldReturnFailedStatusWithoutSecrets()
    {
        // Arrange
        var options = CreateOptions();
        var user = new FakeCurrentUser { UserId = 1, BranchId = 1 };
        var clock = new FakeClock();

        using (var setup = new InventoryDbContext(options, user, clock))
        {
            setup.ApiCredentials.Add(new ApiCredential
            {
                Id = 20,
                BranchId = 1,
                Name = "Test 401",
                BaseUrl = "https://external.api/unauthorized",
                HttpMethod = "GET",
                AuthType = AuthType.ApiKey,
                HeaderName = "X-CUSTOM-KEY",
                SecretEncrypted = _protector.Encrypt("invalid-key"),
                TimeoutSeconds = 5
            });
            await setup.SaveChangesAsync();
        }

        var handler = new FakeHttpMessageHandler(req =>
        {
            Assert.True(req.Headers.Contains("X-CUSTOM-KEY"));
            return new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                ReasonPhrase = "Unauthorized",
                Content = new StringContent("Invalid credentials provided", Encoding.UTF8, "text/plain")
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
        var result = await service.TestConnectionAsync(20);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(401, result.StatusCode);
        Assert.Equal("Unauthorized", result.ReasonPhrase);
        Assert.DoesNotContain("invalid-key", result.BodyPreview ?? "");
    }

    [Fact]
    public async Task PreviewMappingAsync_ShouldApplyMapperAndSaveNothingToDatabase()
    {
        // Arrange
        var options = CreateOptions();
        var user = new FakeCurrentUser { UserId = 1, BranchId = 1 };
        var clock = new FakeClock();

        using (var setup = new InventoryDbContext(options, user, clock))
        {
            var cred = new ApiCredential
            {
                Id = 30,
                BranchId = 1,
                Name = "Preview Cred",
                BaseUrl = "https://external.api/grn",
                HttpMethod = "GET",
                AuthType = AuthType.None,
                TimeoutSeconds = 5
            };
            cred.FieldMappings.Add(new ApiFieldMapping { TargetColumn = TargetColumn.GrnNo, SourceField = "GRNNo", IsRequired = true });
            cred.FieldMappings.Add(new ApiFieldMapping { TargetColumn = TargetColumn.Qty, SourceField = "Qty" });
            setup.ApiCredentials.Add(cred);
            await setup.SaveChangesAsync();
        }

        const string responseJson = """
        [
          { "GRNNo": "GRN-PREVIEW-01", "Qty": 15 },
          { "GRNNo": "GRN-PREVIEW-02", "Qty": 25 }
        ]
        """;

        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
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
        var preview = await service.PreviewMappingAsync(30);

        // Assert
        Assert.True(preview.Success);
        Assert.Equal(2, preview.TotalItemsFound);
        Assert.Equal(2, preview.ReturnedPreviewCount);
        Assert.Equal("GRN-PREVIEW-01", preview.Rows[0].GrnNo);
        Assert.Equal(15m, preview.Rows[0].Qty);

        // Verify NOTHING was written to database
        var dbCount = await context.GrnApiResponses.CountAsync();
        Assert.Equal(0, dbCount);
    }
}
