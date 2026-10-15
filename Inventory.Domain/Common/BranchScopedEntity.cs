namespace Inventory.Domain.Common;

/// <summary>
/// Base class for branch-scoped entities.
/// NOTE: Admin cross-branch reads must use IgnoreQueryFilters() or Dapper explicitly.
/// </summary>
public abstract class BranchScopedEntity : BaseEntity, IBranchScoped
{
    public int BranchId { get; set; }
}
