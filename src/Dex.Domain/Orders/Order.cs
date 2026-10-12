using System.Runtime.CompilerServices;

namespace Dex.Domain.Orders;

/// <summary>
/// Immutable core order representation used across Matching Engine and Risk Engine.
/// Designed for zero-allocation hot path: all fields are value types or immutable.
/// </summary>
public sealed class Order
{
    public long OrderId { get; }
    public long ClientOrderId { get; }
    public long AccountId { get; }
    public int MarketId { get; }
    public OrderSide Side { get; }
    public OrderType Type { get; }
    public TimeInForce TimeInForce { get; }
    public decimal Price { get; }          // Limit price (0 for pure market)
    public decimal Quantity { get; }       // Original quantity
    public decimal RemainingQuantity { get; private set; }
    public decimal FilledQuantity => Quantity - RemainingQuantity;
    public OrderStatus Status { get; private set; }
    public long CreatedAtTicks { get; }
    public long SequenceNumber { get; private set; } // Matching engine sequence for time priority
    public bool ReduceOnly { get; }
    public bool PostOnly { get; }

    public Order(
        long orderId,
        long clientOrderId,
        long accountId,
        int marketId,
        OrderSide side,
        OrderType type,
        TimeInForce timeInForce,
        decimal price,
        decimal quantity,
        long createdAtTicks,
        bool reduceOnly = false,
        bool postOnly = false)
    {
        if (orderId <= 0) throw new ArgumentOutOfRangeException(nameof(orderId));
        if (accountId <= 0) throw new ArgumentOutOfRangeException(nameof(accountId));
        if (marketId <= 0) throw new ArgumentOutOfRangeException(nameof(marketId));
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
        if (type == OrderType.Limit && price <= 0) throw new ArgumentOutOfRangeException(nameof(price));
        if (postOnly && type != OrderType.Limit) throw new ArgumentException("PostOnly is only valid for Limit orders.");

        OrderId = orderId;
        ClientOrderId = clientOrderId;
        AccountId = accountId;
        MarketId = marketId;
        Side = side;
        Type = type;
        TimeInForce = timeInForce;
        Price = price;
        Quantity = quantity;
        RemainingQuantity = quantity;
        Status = OrderStatus.PendingNew;
        CreatedAtTicks = createdAtTicks;
        ReduceOnly = reduceOnly;
        PostOnly = postOnly;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AssignSequence(long sequence)
    {
        SequenceNumber = sequence;
        if (Status == OrderStatus.PendingNew)
            Status = OrderStatus.New;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public decimal ApplyFill(decimal fillQty)
    {
        if (fillQty <= 0) throw new ArgumentOutOfRangeException(nameof(fillQty));
        if (fillQty > RemainingQuantity) throw new InvalidOperationException("Fill exceeds remaining quantity.");

        RemainingQuantity -= fillQty;
        Status = RemainingQuantity == 0 ? OrderStatus.Filled : OrderStatus.PartiallyFilled;
        return RemainingQuantity;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void MarkCancelled()
    {
        if (Status is OrderStatus.Filled or OrderStatus.Cancelled or OrderStatus.Rejected)
            throw new InvalidOperationException($"Cannot cancel order in status {Status}");

        Status = OrderStatus.Cancelled;
        RemainingQuantity = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void MarkRejected()
    {
        Status = OrderStatus.Rejected;
        RemainingQuantity = 0;
    }

    public bool IsActive => Status is OrderStatus.New or OrderStatus.PartiallyFilled;
}
