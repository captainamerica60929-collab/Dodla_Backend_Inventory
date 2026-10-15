using System.IO;
using System.Net.Http;
using Inventory.Application.ApiCredentials.Services;
using Inventory.Application.Branches.Services;
using Inventory.Application.Common.Interfaces;
using Inventory.Infrastructure.Persistence;
using Inventory.Infrastructure.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services,
        IConfiguration configuration,
        bool isDevelopment = false)
    {
        services.AddSingleton<IClock, DateTimeClock>();

        if (isDevelopment)
        {
            // TODO: Replace with JWT claims-based CurrentUser implementation when JWT auth is added
            services.AddScoped<ICurrentUser, DevCurrentUser>();
        }

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

        // HTTP Client for API Credential testing and mapping preview (AllowAutoRedirect = false)
        services.AddHttpClient(ApiCredentialService.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false
            });

        // Services
        services.AddScoped<IBranchService, BranchService>();
        services.AddScoped<IApiCredentialService, ApiCredentialService>();
        services.AddScoped<Inventory.Application.GrnResponses.Services.IGrnResponseService, GrnResponseService>();
        services.AddScoped<DevDataSeeder>();

        services.AddDbContext<InventoryDbContext>(options =>
        {
            var connectionString = configuration.GetConnectionString("Default");
            options.UseSqlServer(connectionString);
        });

        return services;
    }
}
