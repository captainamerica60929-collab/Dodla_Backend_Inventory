using Inventory.Application.Common.Interfaces;

namespace Inventory.Infrastructure.Services;

/// <summary>
/// System-scope user for seeders and migrations.
/// BranchId=0 is the sentinel that bypasses the branch guard in SaveChangesAsync.
/// The seeder must set BranchId explicitly on any branch-scoped entities it creates.
/// </summary>
public class SystemCurrentUser : ICurrentUser
{
    public static readonly SystemCurrentUser Instance = new();

    public int UserId => 0;
    public int BranchId => 0;
    public string Role => "System";
    public bool IsSystem => true;
}
