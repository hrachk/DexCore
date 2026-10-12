namespace Dex.Domain.Orders;

public enum OrderStatus : byte
{
    PendingNew = 0,
    New = 1,
    PartiallyFilled = 2,
    Filled = 3,
    PendingCancel = 4,
    Cancelled = 5,
    Rejected = 6,
    Expired = 7
}
