using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Inventory.Application.Tests;

public class AuthorizationPolicyMatrixTests
{
    private readonly IAuthorizationService _authorizationService;

    public AuthorizationPolicyMatrixTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization(options =>
        {
            options.AddPolicy("MastersEdit",  p => p.RequireRole("Admin"));
            options.AddPolicy("MastersView",  p => p.RequireRole("Admin", "StoreKeeper", "Viewer"));
            options.AddPolicy("ApiAdmin",     p => p.RequireRole("Admin"));
            options.AddPolicy("GrnManage",    p => p.RequireRole("Admin", "StoreKeeper"));
            options.AddPolicy("StockOps",     p => p.RequireRole("Admin", "StoreKeeper", "Operator"));
            options.AddPolicy("ReportsView",  p => p.RequireRole("Admin", "StoreKeeper", "Viewer"));
        });

        var provider = services.BuildServiceProvider();
        _authorizationService = provider.GetRequiredService<IAuthorizationService>();
    }

    private ClaimsPrincipal CreatePrincipal(string? role)
    {
        if (role == null)
            return new ClaimsPrincipal(new ClaimsIdentity()); // Anonymous

        var claims = new[]
        {
            new Claim("role", role)
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer", "name", "role"));
    }

    [Theory]
    [InlineData("Admin", "MastersEdit", true)]
    [InlineData("StoreKeeper", "MastersEdit", false)]
    [InlineData("Operator", "MastersEdit", false)]
    [InlineData("Viewer", "MastersEdit", false)]
    [InlineData(null, "MastersEdit", false)]
    // MastersView
    [InlineData("Admin", "MastersView", true)]
    [InlineData("StoreKeeper", "MastersView", true)]
    [InlineData("Viewer", "MastersView", true)]
    [InlineData("Operator", "MastersView", false)]
    [InlineData(null, "MastersView", false)]
    // ApiAdmin
    [InlineData("Admin", "ApiAdmin", true)]
    [InlineData("StoreKeeper", "ApiAdmin", false)]
    [InlineData("Operator", "ApiAdmin", false)]
    [InlineData("Viewer", "ApiAdmin", false)]
    [InlineData(null, "ApiAdmin", false)]
    // GrnManage
    [InlineData("Admin", "GrnManage", true)]
    [InlineData("StoreKeeper", "GrnManage", true)]
    [InlineData("Operator", "GrnManage", false)]
    [InlineData("Viewer", "GrnManage", false)]
    [InlineData(null, "GrnManage", false)]
    // StockOps
    [InlineData("Admin", "StockOps", true)]
    [InlineData("StoreKeeper", "StockOps", true)]
    [InlineData("Operator", "StockOps", true)]
    [InlineData("Viewer", "StockOps", false)]
    [InlineData(null, "StockOps", false)]
    // ReportsView
    [InlineData("Admin", "ReportsView", true)]
    [InlineData("StoreKeeper", "ReportsView", true)]
    [InlineData("Viewer", "ReportsView", true)]
    [InlineData("Operator", "ReportsView", false)]
    [InlineData(null, "ReportsView", false)]
    public async Task PolicyMatrix_EvaluatesCorrectly(string? role, string policyName, bool expectedSuccess)
    {
        var principal = CreatePrincipal(role);
        var result = await _authorizationService.AuthorizeAsync(principal, policyName);

        Assert.Equal(expectedSuccess, result.Succeeded);
    }
}
