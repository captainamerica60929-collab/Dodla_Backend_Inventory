using Inventory.Application.Common.Interfaces;

namespace Inventory.Infrastructure.Services;

/// <summary>
/// Temporary development user context until JWT authentication is implemented.
/// TODO: Remove when JWT auth and claims-based CurrentUser are added.
/// </summary>
public class DevCurrentUser : ICurrentUser
{
    public int UserId => 1;
    public int BranchId => 1;
}
