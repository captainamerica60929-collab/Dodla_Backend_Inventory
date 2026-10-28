namespace Inventory.Domain.Enums;

public enum StockMovementType
{
    In,         // Inward putaway mapping
    Out,        // Outward FIFO issue
    Transfer,   // Bin-to-bin transfer
    Accept,     // Operator confirmation of receipt
    Reject,     // Operator rejection of receipt
    VoidIn,     // Void inward
    UndoOut     // Undo outward
}
