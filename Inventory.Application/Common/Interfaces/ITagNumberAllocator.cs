namespace Inventory.Application.Common.Interfaces;

/// <summary>
/// Allocates sequential tag numbers atomically within the current database transaction.
/// </summary>
public interface ITagNumberAllocator
{
    /// <summary>
    /// Atomically increments the tag counter for the given branch and year,
    /// returning the first sequence number of the allocated range.
    /// Must run within an active database transaction.
    /// </summary>
    Task<int> AllocateAsync(int branchId, int year, int count, CancellationToken ct = default);
}
