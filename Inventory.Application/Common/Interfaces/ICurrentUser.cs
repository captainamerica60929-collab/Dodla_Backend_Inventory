namespace Inventory.Application.Common.Interfaces;

public interface ICurrentUser
{
    int UserId { get; }
    int BranchId { get; }
}
