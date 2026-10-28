using Inventory.Application.Common.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.Infrastructure.Services;

/// <summary>
/// Scoped holder for overriding the request-time ICurrentUser.
/// By default CurrentUser is null, meaning ClaimsCurrentUser is used.
/// Only seeders and testing harnesses set CurrentUser (e.g. to SystemCurrentUser.Instance).
/// </summary>
public class CurrentUserContext
{
    public ICurrentUser? CurrentUser { get; set; }
}

public static class SystemScopeExtensions
{
    /// <summary>
    /// Creates an IServiceScope configured with SystemCurrentUser.Instance.
    /// Used by seeders and system jobs to ensure branch scoping guard is bypassed.
    /// </summary>
    public static IServiceScope CreateSystemScope(this IServiceProvider serviceProvider)
    {
        var scope = serviceProvider.CreateScope();
        scope.ServiceProvider.GetRequiredService<CurrentUserContext>().CurrentUser = SystemCurrentUser.Instance;
        return scope;
    }
}
