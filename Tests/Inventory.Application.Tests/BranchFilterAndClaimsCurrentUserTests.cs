using System.Security.Claims;
using Inventory.Application.Common.Interfaces;
using Inventory.Domain.Common;
using Inventory.Domain.Entities;
using Inventory.Domain.Enums;
using Inventory.Domain.Exceptions;
using Inventory.Infrastructure.Persistence;
using Inventory.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Inventory.Application.Tests;

public class BranchFilterAndClaimsCurrentUserTests
{
    private ClaimsCurrentUser CreateClaimsCurrentUser(ClaimsPrincipal? principal)
    {
        var httpContext = new DefaultHttpContext();
        if (principal != null)
            httpContext.User = principal;

        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        return new ClaimsCurrentUser(accessor);
    }

    [Fact]
    public void ClaimsCurrentUser_Anonymous_ReturnsBranchMinusOneAndIsSystemFalse()
    {
        var currentUser = CreateClaimsCurrentUser(null);

        Assert.False(currentUser.IsSystem);
        Assert.Equal(0, currentUser.UserId);
        Assert.Equal(-1, currentUser.BranchId);
        Assert.Equal(string.Empty, currentUser.Role);
    }

    [Fact]
    public void ClaimsCurrentUser_ValidClaims_ReturnsUserIdAndBranchId()
    {
        var claims = new[]
        {
            new Claim("sub", "42"),
            new Claim("branch_id", "2"),
            new Claim("role", "StoreKeeper")
        };
        var identity = new ClaimsIdentity(claims, "Bearer", "name", "role");
        var principal = new ClaimsPrincipal(identity);

        var currentUser = CreateClaimsCurrentUser(principal);

        Assert.False(currentUser.IsSystem);
        Assert.Equal(42, currentUser.UserId);
        Assert.Equal(2, currentUser.BranchId);
        Assert.Equal("StoreKeeper", currentUser.Role);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("abc")]
    [InlineData(null)]
    public void ClaimsCurrentUser_AuthenticatedWithInvalidBranchId_ThrowsUnauthorized(string? branchIdValue)
    {
        var claims = new List<Claim> { new Claim("sub", "42") };
        if (branchIdValue != null)
            claims.Add(new Claim("branch_id", branchIdValue));

        var identity = new ClaimsIdentity(claims, "Bearer");
        var principal = new ClaimsPrincipal(identity);

        var currentUser = CreateClaimsCurrentUser(principal);

        var ex = Assert.Throws<UnauthorizedException>(() => _ = currentUser.BranchId);
        Assert.Equal("Invalid credentials", ex.Message);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("xyz")]
    [InlineData(null)]
    public void ClaimsCurrentUser_AuthenticatedWithInvalidSub_ThrowsUnauthorized(string? subValue)
    {
        var claims = new List<Claim> { new Claim("branch_id", "1") };
        if (subValue != null)
            claims.Add(new Claim("sub", subValue));

        var identity = new ClaimsIdentity(claims, "Bearer");
        var principal = new ClaimsPrincipal(identity);

        var currentUser = CreateClaimsCurrentUser(principal);

        var ex = Assert.Throws<UnauthorizedException>(() => _ = currentUser.UserId);
        Assert.Equal("Invalid credentials", ex.Message);
    }

    [Fact]
    public async Task BranchFilter_WithRealClaims_EnforcesTenantIsolation()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        var clock = new FakeClock();

        // Seed with SystemCurrentUser
        using (var seedContext = new InventoryDbContext(options, SystemCurrentUser.Instance, clock))
        {
            seedContext.ApiCredentials.AddRange(
                new ApiCredential { Id = 1, BranchId = 1, Name = "Cred Branch 1", BaseUrl = "http://b1.com", HttpMethod = "GET", AuthType = AuthType.None },
                new ApiCredential { Id = 2, BranchId = 2, Name = "Cred Branch 2", BaseUrl = "http://b2.com", HttpMethod = "GET", AuthType = AuthType.None }
            );
            await seedContext.SaveChangesAsync();
        }

        // Read with Branch 1 ClaimsUser
        var claimsUserBranch1 = CreateClaimsCurrentUser(new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("sub", "10"),
            new Claim("branch_id", "1"),
            new Claim("role", "StoreKeeper")
        }, "Bearer", "name", "role")));

        using (var readContext1 = new InventoryDbContext(options, claimsUserBranch1, clock))
        {
            var creds = await readContext1.ApiCredentials.ToListAsync();
            Assert.Single(creds);
            Assert.Equal("Cred Branch 1", creds[0].Name);
        }

        // Read with Anonymous User (BranchId = -1)
        var anonUser = CreateClaimsCurrentUser(null);
        using (var anonContext = new InventoryDbContext(options, anonUser, clock))
        {
            var creds = await anonContext.ApiCredentials.ToListAsync();
            Assert.Empty(creds); // BranchId = -1 matches no rows
        }
    }

    [Fact]
    public async Task SaveChangesAsync_IsSystemTrue_BypassesBranchGuard()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        var clock = new FakeClock();

        // SystemCurrentUser has IsSystem = true and BranchId = 0
        using var systemContext = new InventoryDbContext(options, SystemCurrentUser.Instance, clock);

        var credential = new ApiCredential
        {
            BranchId = 5, // Explicitly branch 5
            Name = "System Credential",
            BaseUrl = "http://system.com",
            HttpMethod = "GET",
            AuthType = AuthType.None
        };

        systemContext.ApiCredentials.Add(credential);
        var savedCount = await systemContext.SaveChangesAsync();
        Assert.True(savedCount > 0);
        Assert.Equal(5, credential.BranchId);
    }

    [Fact]
    public async Task SaveChangesAsync_IsSystemFalse_CrossBranchThrowsDomainException()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        var clock = new FakeClock();

        var claimsUser = CreateClaimsCurrentUser(new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("sub", "10"),
            new Claim("branch_id", "1"),
            new Claim("role", "StoreKeeper")
        }, "Bearer", "name", "role")));

        using var userContext = new InventoryDbContext(options, claimsUser, clock);

        var credential = new ApiCredential
        {
            BranchId = 2, // Mismatched branch!
            Name = "Hacked Credential",
            BaseUrl = "http://hacked.com",
            HttpMethod = "GET",
            AuthType = AuthType.None
        };

        userContext.ApiCredentials.Add(credential);
        var ex = await Assert.ThrowsAsync<DomainException>(() => userContext.SaveChangesAsync());
        Assert.Contains("Cannot create branch-scoped entity for branch 2 when current user belongs to branch 1", ex.Message);
    }
}
