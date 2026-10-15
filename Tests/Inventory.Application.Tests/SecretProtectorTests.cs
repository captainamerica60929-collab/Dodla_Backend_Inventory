using Inventory.Application.ApiCredentials.DTOs;
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

public class SecretProtectorTests
{
    private readonly ISecretProtector _protector;

    public SecretProtectorTests()
    {
        var dataProtectionProvider = new EphemeralDataProtectionProvider();
        _protector = new SecretProtector(dataProtectionProvider);
    }

    [Fact]
    public void Encrypt_And_Decrypt_ShouldRestoreOriginalPlainText()
    {
        // Arrange
        const string plainText = "MySuperSecretApiToken123!#";

        // Act
        var encrypted = _protector.Encrypt(plainText);
        var decrypted = _protector.Decrypt(encrypted);

        // Assert
        Assert.NotEqual(plainText, encrypted);
        Assert.NotEmpty(encrypted);
        Assert.Equal(plainText, decrypted);
    }

    [Fact]
    public void ApiCredentialDto_HasSecret_ShouldReflectPresenceOfEncryptedSecretWithoutRevealingIt()
    {
        // Arrange
        var credWithSecret = new ApiCredential
        {
            Id = 1,
            BranchId = 1,
            Name = "SapApi",
            BaseUrl = "https://sap.local/api",
            SecretEncrypted = _protector.Encrypt("Secret123")
        };

        var credWithoutSecret = new ApiCredential
        {
            Id = 2,
            BranchId = 1,
            Name = "OpenApi",
            BaseUrl = "https://open.local/api",
            SecretEncrypted = null
        };

        // Act
        var dtoWithSecret = new ApiCredentialDto(
            credWithSecret.Id,
            credWithSecret.BranchId,
            credWithSecret.Name,
            credWithSecret.BaseUrl,
            credWithSecret.HttpMethod,
            credWithSecret.AuthType,
            credWithSecret.UserName,
            credWithSecret.HeaderName,
            !string.IsNullOrEmpty(credWithSecret.SecretEncrypted),
            credWithSecret.ExtraHeadersJson,
            credWithSecret.TimeoutSeconds,
            credWithSecret.ContentType,
            credWithSecret.RequestBodyTemplate,
            credWithSecret.ResponseRootPath,
            credWithSecret.IsActive,
            credWithSecret.CreatedAt,
            credWithSecret.CreatedBy,
            credWithSecret.ModifiedAt,
            credWithSecret.ModifiedBy);

        var dtoWithoutSecret = new ApiCredentialDto(
            credWithoutSecret.Id,
            credWithoutSecret.BranchId,
            credWithoutSecret.Name,
            credWithoutSecret.BaseUrl,
            credWithoutSecret.HttpMethod,
            credWithoutSecret.AuthType,
            credWithoutSecret.UserName,
            credWithoutSecret.HeaderName,
            !string.IsNullOrEmpty(credWithoutSecret.SecretEncrypted),
            credWithoutSecret.ExtraHeadersJson,
            credWithoutSecret.TimeoutSeconds,
            credWithoutSecret.ContentType,
            credWithoutSecret.RequestBodyTemplate,
            credWithoutSecret.ResponseRootPath,
            credWithoutSecret.IsActive,
            credWithoutSecret.CreatedAt,
            credWithoutSecret.CreatedBy,
            credWithoutSecret.ModifiedAt,
            credWithoutSecret.ModifiedBy);

        // Assert
        Assert.True(dtoWithSecret.HasSecret);
        Assert.False(dtoWithoutSecret.HasSecret);
    }

    [Fact]
    public async Task Update_WithNullOrEmptySecret_ShouldRetainExistingEncryptedSecret()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var user = new FakeCurrentUser { UserId = 1, BranchId = 1 };
        var clock = new FakeClock();

        using (var setupContext = new InventoryDbContext(options, user, clock))
        {
            var initial = new ApiCredential
            {
                Id = 1,
                BranchId = 1,
                Name = "Initial Cred",
                BaseUrl = "https://api.test.com",
                SecretEncrypted = _protector.Encrypt("OriginalSecret")
            };
            setupContext.ApiCredentials.Add(initial);
            await setupContext.SaveChangesAsync();
        }

        using var testContext = new InventoryDbContext(options, user, clock);
        var httpClientFactory = new FakeHttpClientFactory(new FakeHttpMessageHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)));
        var service = new ApiCredentialService(
            testContext,
            _protector,
            httpClientFactory,
            clock,
            user,
            new DummyHostEnvironment(),
            NullLogger<ApiCredentialService>.Instance);

        // Act - Update without providing a new secret
        var updateDto = new UpdateApiCredentialDto(
            Name: "Updated Name",
            BaseUrl: "https://api.test.com",
            HttpMethod: "GET",
            AuthType: AuthType.None,
            UserName: null,
            HeaderName: null,
            Secret: "", // Empty: should retain existing
            ExtraHeadersJson: null,
            TimeoutSeconds: 30,
            ContentType: "application/json",
            RequestBodyTemplate: null,
            ResponseRootPath: null,
            IsActive: true);

        var result = await service.UpdateAsync(1, updateDto);

        // Assert
        var entity = await testContext.ApiCredentials.FindAsync(1);
        Assert.NotNull(entity);
        Assert.Equal("Updated Name", entity.Name);
        Assert.NotNull(entity.SecretEncrypted);
        Assert.Equal("OriginalSecret", _protector.Decrypt(entity.SecretEncrypted));
        Assert.True(result.HasSecret);
    }
}
