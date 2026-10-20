using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Inventory.Application.Auth.DTOs;
using Inventory.Application.Common.Interfaces;
using Inventory.Domain.Entities;
using Inventory.Domain.Enums;
using Inventory.Infrastructure.Auth;
using Inventory.Infrastructure.Persistence;
using Inventory.Infrastructure.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Inventory.Application.Tests;

public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string TestJwtKey = "super-secret-test-signing-key-that-is-at-least-32-bytes!";
    public const string TestIssuer = "WareTrackTest";
    public const string TestAudience = "WareTrackTest.Client";
    public const string AdminPassword = "TestAdminPassword@123!";

    private readonly string _dbName = Guid.NewGuid().ToString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.UseSetting("Jwt:Key", TestJwtKey);
        builder.UseSetting("Jwt:Issuer", TestIssuer);
        builder.UseSetting("Jwt:Audience", TestAudience);
        builder.UseSetting("Jwt:LifetimeMinutes", "480");
        builder.UseSetting("Seed:AdminPassword", AdminPassword);
        builder.UseSetting("ConnectionStrings:Default", "Server=inmemory;Database=test;");

        builder.ConfigureServices(services =>
        {
            var descriptors = services.Where(d =>
                d.ServiceType == typeof(DbContextOptions<InventoryDbContext>) ||
                d.ServiceType == typeof(DbContextOptions) ||
                d.ServiceType == typeof(InventoryDbContext)).ToList();

            foreach (var d in descriptors)
                services.Remove(d);

            services.AddDbContext<InventoryDbContext>(options =>
            {
                options.UseInMemoryDatabase(_dbName);
            });

            var allocatorDesc = services.FirstOrDefault(d => d.ServiceType == typeof(ITagNumberAllocator));
            if (allocatorDesc != null)
                services.Remove(allocatorDesc);
            services.AddSingleton<ITagNumberAllocator, Inventory.Application.Tests.Fakes.FakeTagNumberAllocator>();
        });
    }

    public void EnsureSeeded()
    {
        using var scope = Services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        if (!db.Branches.Any(b => b.Id == 1))
        {
            db.Branches.Add(new Branch { Id = 1, Code = "MAIN", Name = "Main Branch", IsActive = true });
            db.SaveChanges();
        }

        var seeder = scope.ServiceProvider.GetRequiredService<AppUserSeeder>();
        seeder.SeedAsync().GetAwaiter().GetResult();
    }

    public string CreateTokenWithAudience(string audience, string? issuer = TestIssuer)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestJwtKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, "1"),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim("name", "Test Admin"),
            new Claim("role", "Admin"),
            new Claim("branch_id", "1"),
            new Claim("employee_id", "1")
        };

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddMinutes(60),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string CreateTokenForRole(string role, int branchId = 1, int userId = 1)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestJwtKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim("name", $"Test {role}"),
            new Claim("role", role),
            new Claim("branch_id", branchId.ToString()),
            new Claim("employee_id", userId.ToString())
        };

        var token = new JwtSecurityToken(
            issuer: TestIssuer,
            audience: TestAudience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddMinutes(60),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string CreateOldShapeTokenWithoutIssuerAndAudience()
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestJwtKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, "1"),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim("name", "Test Admin"),
            new Claim("role", "Admin"),
            new Claim("branch_id", "1"),
            new Claim("employee_id", "1")
        };

        // Old shape: no issuer, no audience
        var token = new JwtSecurityToken(
            issuer: null,
            audience: null,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddMinutes(60),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

public class JwtBearerAuthenticationPipelineTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public JwtBearerAuthenticationPipelineTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.EnsureSeeded();
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Login_ReturnsTokenWithRequiredClaims_AndMeEndpointSucceeds200()
    {
        // 1. Log in through real endpoint
        var loginRequest = new LoginRequest(1, "admin", TestWebApplicationFactory.AdminPassword);
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", loginRequest);

        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var body = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(body);
        Assert.NotEmpty(body.Token);

        // 2. Validate token structure and claims
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(body.Token);

        Assert.Equal(TestWebApplicationFactory.TestIssuer, jwt.Issuer);
        Assert.Contains(TestWebApplicationFactory.TestAudience, jwt.Audiences);

        // Verify claims exist strictly as short names
        Assert.NotNull(jwt.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Sub));
        Assert.NotNull(jwt.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Jti));
        Assert.NotNull(jwt.Claims.FirstOrDefault(c => c.Type == "name"));
        Assert.NotNull(jwt.Claims.FirstOrDefault(c => c.Type == "role"));
        Assert.NotNull(jwt.Claims.FirstOrDefault(c => c.Type == "branch_id"));
        Assert.NotNull(jwt.Claims.FirstOrDefault(c => c.Type == "employee_id"));

        // Confirm no legacy long URI duplicate claims exist
        Assert.Null(jwt.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role));
        Assert.Null(jwt.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name));
        Assert.Null(jwt.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier));

        // 3. Authenticate with returned Bearer token against GET /api/auth/me
        using var meRequest = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        meRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", body.Token);

        var meResponse = await _client.SendAsync(meRequest);
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);

        var me = await meResponse.Content.ReadFromJsonAsync<MeDto>();
        Assert.NotNull(me);
        Assert.Equal("admin", me.Username);
        Assert.Equal("Admin", me.Role);
        Assert.Equal(1, me.BranchId);
    }

    [Fact]
    public async Task ChangePassword_WithValidBearerToken_Succeeds204()
    {
        // Log in to get token
        var loginRequest = new LoginRequest(1, "admin", TestWebApplicationFactory.AdminPassword);
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", loginRequest);
        var body = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();

        // Call change-password with valid Bearer token
        using var changePwdRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password");
        changePwdRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", body!.Token);
        changePwdRequest.Content = JsonContent.Create(new ChangePasswordRequest(
            TestWebApplicationFactory.AdminPassword,
            "BrandNewAdminPassword@12345!"));

        var changePwdResponse = await _client.SendAsync(changePwdRequest);
        Assert.Equal(HttpStatusCode.NoContent, changePwdResponse.StatusCode);

        // Revert password back so subsequent runs remain idempotent
        using var revertRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password");
        revertRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", body.Token);
        revertRequest.Content = JsonContent.Create(new ChangePasswordRequest(
            "BrandNewAdminPassword@12345!",
            TestWebApplicationFactory.AdminPassword));
        await _client.SendAsync(revertRequest);
    }

    [Fact]
    public async Task Request_WithWrongAudienceToken_Returns401Unauthorized()
    {
        // Token has wrong audience
        var token = _factory.CreateTokenWithAudience("Wrong.Audience.Client");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Request_WithOldShapeToken_NoIssuerOrAudience_Returns401Unauthorized()
    {
        // Token lacks iss and aud claims
        var token = _factory.CreateOldShapeTokenWithoutIssuerAndAudience();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
