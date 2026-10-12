using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Dex.Domain.Orders;

namespace Dex.MatchingEngine.OrderBook;

/// <summary>
/// High-performance Price-Time Priority Order Book.
/// Bids are stored in descending price order, Asks in ascending price order.
/// Thread-safety: the MatchingEngine guarantees single-writer access to a given market's book.
/// </summary>
public sealed class OrderBook
{
    private readonly int _marketId;
    private readonly SortedDictionary<decimal, PriceLevel> _bids; // high → low
    private readonly SortedDictionary<decimal, PriceLevel> _asks; // low → high
    private readonly ConcurrentDictionary<long, Order> _orderIndex = new();

    private long _nextTradeId = 1;
    private long _sequence;

    public int MarketId => _marketId;
    public decimal BestBid => _bids.Count > 0 ? _bids.Keys.First() : 0m;
    public decimal BestAsk => _asks.Count > 0 ? _asks.Keys.First() : 0m;
    public decimal Spread => BestAsk > 0 && BestBid > 0 ? BestAsk - BestBid : 0m;

    public OrderBook(int marketId)
    {
        if (marketId <= 0) throw new ArgumentOutOfRangeException(nameof(marketId));
        _marketId = marketId;

        // Bids: highest price first
        _bids = new SortedDictionary<decimal, PriceLevel>(Comparer<decimal>.Create((a, b) => b.CompareTo(a)));
        // Asks: lowest price first
        _asks = new SortedDictionary<decimal, PriceLevel>();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public long NextSequence() => Interlocked.Increment(ref _sequence);

    public bool TryGetOrder(long orderId, out Order? order) => _orderIndex.TryGetValue(orderId, out order);

    /// <summary>
    /// Places a limit order into the book or matches it against the opposite side.
    /// Returns generated trades (empty if resting).
    /// </summary>
    public IReadOnlyList<Trade> PlaceLimitOrder(Order order)
    {
        if (order.MarketId != _marketId)
            throw new ArgumentException("Order market mismatch.");

        if (order.Type != OrderType.Limit)
            throw new ArgumentException("Only Limit orders are accepted by PlaceLimitOrder.");

        if (order.PostOnly && WouldImmediatelyMatch(order))
        {
            order.MarkRejected();
            return Array.Empty<Trade>();
        }

        var trades = MatchAgainstBook(order);

        // New orders start as PendingNew — IsActive is false until AssignSequence in RestOrder.
        // Rest residual only for GTC (and similar) after matching.
        if (order.RemainingQuantity > 0
            && order.Status is not (OrderStatus.Rejected or OrderStatus.Cancelled or OrderStatus.Filled)
            && order.TimeInForce != TimeInForce.IOC
            && order.TimeInForce != TimeInForce.FOK)
        {
            RestOrder(order);
        }
        else if (order.TimeInForce == TimeInForce.FOK && order.RemainingQuantity > 0)
        {
            // FOK not fully filled → cancel residual (and roll back is simplified here)
            order.MarkCancelled();
        }
        else if (order.RemainingQuantity > 0
                 && (order.TimeInForce == TimeInForce.IOC || order.Type == OrderType.Market)
                 && order.Status is not (OrderStatus.Rejected or OrderStatus.Cancelled or OrderStatus.Filled))
        {
            order.MarkCancelled(); // residual of IOC/Market is cancelled
        }

        return trades;
    }

    /// <summary>
    /// Aggressive market order – consumes liquidity until filled or book exhausted.
    /// </summary>
    public IReadOnlyList<Trade> PlaceMarketOrder(Order order)
    {
        if (order.Type != OrderType.Market)
            throw new ArgumentException("Only Market orders are accepted by PlaceMarketOrder.");

        var trades = MatchAgainstBook(order);

        if (order.RemainingQuantity > 0)
            order.MarkCancelled(); // residual of market is cancelled

        return trades;
    }

    public bool CancelOrder(long orderId)
    {
        if (!_orderIndex.TryGetValue(orderId, out var order) || !order.IsActive)
            return false;

        var book = order.Side == OrderSide.Buy ? _bids : _asks;
        if (book.TryGetValue(order.Price, out var level))
        {
            if (level.TryRemoveOrder(orderId, out _))
            {
                if (level.IsEmpty)
                    book.Remove(order.Price);
            }
        }

        order.MarkCancelled();
        _orderIndex.TryRemove(orderId, out _);
        return true;
    }

    private bool WouldImmediatelyMatch(Order order)
    {
        if (order.Side == OrderSide.Buy)
            return BestAsk > 0 && order.Price >= BestAsk;
        return BestBid > 0 && order.Price <= BestBid;
    }

    private List<Trade> MatchAgainstBook(Order taker)
    {
        var trades = new List<Trade>(8);
        var opposite = taker.Side == OrderSide.Buy ? _asks : _bids;

        while (taker.RemainingQuantity > 0 && opposite.Count > 0)
        {
            var bestPrice = opposite.Keys.First();
            var canMatch = taker.Side == OrderSide.Buy
                ? (taker.Type == OrderType.Market || taker.Price >= bestPrice)
                : (taker.Type == OrderType.Market || taker.Price <= bestPrice);

            if (!canMatch)
                break;

            var level = opposite[bestPrice];
            var fills = level.Match(taker.RemainingQuantity);

            foreach (var (maker, fillQty) in fills)
            {
                taker.ApplyFill(fillQty);

                var trade = new Trade(
                    tradeId: Interlocked.Increment(ref _nextTradeId),
                    makerOrderId: maker.OrderId,
                    takerOrderId: taker.OrderId,
                    makerAccountId: maker.AccountId,
                    takerAccountId: taker.AccountId,
                    marketId: _marketId,
                    takerSide: taker.Side,
                    price: bestPrice,
                    quantity: fillQty,
                    executedAtTicks: DateTime.UtcNow.Ticks,
                    sequenceNumber: NextSequence());

                trades.Add(trade);

                if (maker.RemainingQuantity == 0)
                    _orderIndex.TryRemove(maker.OrderId, out _);
            }

            if (level.IsEmpty)
                opposite.Remove(bestPrice);
        }

        return trades;
    }

    private void RestOrder(Order order)
    {
        var book = order.Side == OrderSide.Buy ? _bids : _asks;

        if (!book.TryGetValue(order.Price, out var level))
        {
            level = new PriceLevel(order.Price);
            book[order.Price] = level;
        }

        level.AddOrder(order);
        _orderIndex[order.OrderId] = order;
        order.AssignSequence(NextSequence());
    }

    /// <summary>
    /// Returns a snapshot of the top N levels for L2 market data.
    /// </summary>
    public (IReadOnlyList<(decimal Price, decimal Quantity)> Bids,
            IReadOnlyList<(decimal Price, decimal Quantity)> Asks) GetDepth(int levels)
    {
        var bids = _bids.Take(levels)
            .Select(kv => (kv.Key, kv.Value.TotalQuantity))
            .ToList();

        var asks = _asks.Take(levels)
            .Select(kv => (kv.Key, kv.Value.TotalQuantity))
            .ToList();

        return (bids, asks);
    }
}
