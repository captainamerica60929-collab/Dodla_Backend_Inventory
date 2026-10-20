namespace Inventory.Domain.Entities;

/// <summary>
/// Sequence counter tracking the last allocated tag sequence number per BranchId and Year.
/// Independent of tag string format. Primary key: (BranchId, Year).
/// </summary>
public class TagCounter
{
    public int BranchId { get; set; }
    public int Year { get; set; }
    public int LastNo { get; set; }
}
