namespace Inventory.Domain.Common;

/// <summary>
/// Marks an entity as scoped to a specific warehouse branch.
/// Branch scoping is automatically enforced in EF Core via Global Query Filters.
/// NOTE: Admin cross-branch reads must use IgnoreQueryFilters() or Dapper explicitly.
/// </summary>
public interface IBranchScoped
{
    int BranchId { get; set; }
}
