using System.IO;
using System.Net.Http;
using System.Text;
using Inventory.Application.Auth.Services;
using Inventory.Application.ApiCredentials.Services;
using Inventory.Application.Branches.Services;
using Inventory.Application.Common.Interfaces;
using Inventory.Application.Employees.Services;
using Inventory.Application.Users.Services;
using Inventory.Infrastructure.Auth;
using Inventory.Infrastructure.Persistence;
using Inventory.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Inventory.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services,
        IConfiguration configuration,
        bool isDevelopment = false)
    {
        services.AddSingleton<IClock, DateTimeClock>();

        // Claims-based current user with scoped override support for seeders
        services.AddHttpContextAccessor();
        services.AddScoped<CurrentUserContext>();
        services.AddScoped<ClaimsCurrentUser>();
        services.AddScoped<ICurrentUser>(sp =>
        {
            var context = sp.GetRequiredService<CurrentUserContext>();
            return context.CurrentUser ?? sp.GetRequiredService<ClaimsCurrentUser>();
        });

        // ASP.NET Core Data Protection for credential secrets (persisted outside repo)
        var keyStoragePath = configuration["DataProtection:KeyStoragePath"];
        if (string.IsNullOrWhiteSpace(keyStoragePath))
        {
            keyStoragePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WareTrack",
                "DataProtection-Keys");
        }

        services.AddDataProtection()
            .SetApplicationName("WareTrack")
            .PersistKeysToFileSystem(new DirectoryInfo(keyStoragePath));

        services.AddSingleton<ISecretProtector, SecretProtector>();

        // Password service (PBKDF2 via ASP.NET Identity PasswordHasher)
        services.AddScoped<IPasswordService, PasswordService>();

        // JWT settings and token service
        var jwtSettings = new JwtSettings();
        configuration.GetSection(JwtSettings.Section).Bind(jwtSettings);

        if (string.IsNullOrWhiteSpace(jwtSettings.Issuer))
        {
            throw new InvalidOperationException(
                "JWT issuer (Jwt:Issuer) is missing. Configure it in appsettings.json: \"Jwt\": { \"Issuer\": \"WareTrack\" }");
        }

        if (string.IsNullOrWhiteSpace(jwtSettings.Audience))
        {
            throw new InvalidOperationException(
                "JWT audience (Jwt:Audience) is missing. Configure it in appsettings.json: \"Jwt\": { \"Audience\": \"WareTrack.Client\" }");
        }

        if (jwtSettings.LifetimeMinutes < 5 || jwtSettings.LifetimeMinutes > 1440)
        {
            throw new InvalidOperationException(
                $"JWT lifetime (Jwt:LifetimeMinutes) must be between 5 and 1440 minutes, but was {jwtSettings.LifetimeMinutes}.");
        }

        services.AddSingleton(jwtSettings);
        services.AddScoped<IJwtTokenService, JwtTokenService>();

        // JWT Bearer authentication — must fail startup if key missing or < 32 bytes
        var jwtKey = configuration["Jwt:Key"];
        if (string.IsNullOrWhiteSpace(jwtKey))
        {
            throw new InvalidOperationException(
                "JWT signing key (Jwt:Key) is missing. Configure it via: dotnet user-secrets set \"Jwt:Key\" \"<your-32+-char-key>\"");
        }

        if (Encoding.UTF8.GetByteCount(jwtKey) < 32)
        {
            throw new InvalidOperationException(
                "JWT signing key (Jwt:Key) must be at least 32 bytes (256 bits). Configure a longer key via user-secrets.");
        }

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwtSettings.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwtSettings.Audience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(1),
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
                    NameClaimType = "name",
                    RoleClaimType = "role"
                };
            });

        // HTTP Client for API Credential testing and mapping preview (AllowAutoRedirect = false)
        services.AddHttpClient(ApiCredentialService.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false
            });

        // Application services
        services.AddScoped<IBranchService, BranchService>();
        services.AddScoped<IApiCredentialService, ApiCredentialService>();
        services.AddScoped<Inventory.Application.GrnResponses.Services.IGrnResponseService, GrnResponseService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IEmployeeService, EmployeeService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<Inventory.Application.Materials.Services.IMaterialService, MaterialService>();
        services.AddScoped<Inventory.Application.Locations.Services.ILocationService, LocationService>();
        services.AddScoped<ITagNumberAllocator, SqlServerTagNumberAllocator>();
        services.AddScoped<Inventory.Application.Tags.Services.ITagService, TagService>();
        services.AddScoped<Inventory.Application.Stock.Services.IStockService, StockService>();
        services.AddScoped<Inventory.Application.Grn.Services.IGrnImportService, GrnImportService>();
        services.AddScoped<Inventory.Application.Reports.Services.IReportService, ReportService>();

        // Seeders
        services.AddScoped<DevDataSeeder>();
        services.AddScoped<AppUserSeeder>();

        services.AddDbContext<InventoryDbContext>(options =>
        {
            var connectionString = configuration.GetConnectionString("Default");
            options.UseSqlServer(connectionString);
        });

        return services;
    }
}
