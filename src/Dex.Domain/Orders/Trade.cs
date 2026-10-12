namespace Dex.Domain.Orders;

/// <summary>
/// Immutable trade record produced by the Matching Engine.
/// </summary>
public readonly struct Trade
{
    public long TradeId { get; }
    public long MakerOrderId { get; }
    public long TakerOrderId { get; }
    public long MakerAccountId { get; }
    public long TakerAccountId { get; }
    public int MarketId { get; }
    public OrderSide TakerSide { get; }
    public decimal Price { get; }
    public decimal Quantity { get; }
    public long ExecutedAtTicks { get; }
    public long SequenceNumber { get; }

    public Trade(
        long tradeId,
        long makerOrderId,
        long takerOrderId,
        long makerAccountId,
        long takerAccountId,
        int marketId,
        OrderSide takerSide,
        decimal price,
        decimal quantity,
        long executedAtTicks,
        long sequenceNumber)
    {
        TradeId = tradeId;
        MakerOrderId = makerOrderId;
        TakerOrderId = takerOrderId;
        MakerAccountId = makerAccountId;
        TakerAccountId = takerAccountId;
        MarketId = marketId;
        TakerSide = takerSide;
        Price = price;
        Quantity = quantity;
        ExecutedAtTicks = executedAtTicks;
        SequenceNumber = sequenceNumber;
    }
}
