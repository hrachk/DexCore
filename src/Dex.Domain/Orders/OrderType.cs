namespace Dex.Domain.Orders;

public enum OrderType : byte
{
    Limit = 0,
    Market = 1,
    StopLimit = 2,
    StopMarket = 3,
    TakeProfitLimit = 4,
    TakeProfitMarket = 5
}
