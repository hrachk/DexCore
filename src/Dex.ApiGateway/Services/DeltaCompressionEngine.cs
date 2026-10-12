using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Dex.ApiGateway.Models;
using Dex.MatchingEngine.OrderBook;
using OrderBookEngine = Dex.MatchingEngine.OrderBook.OrderBook;

namespace Dex.ApiGateway.Services;

/// <summary>
/// Maintains last published L2 state per market and produces minimal deltas.
/// Snapshots are issued on subscribe; subsequent updates are deltas only.
/// </summary>
public sealed class DeltaCompressionEngine
{
    private readonly ConcurrentDictionary<int, MarketBookState> _states = new();
    private long _globalSequence;

    public long NextSequence() => Interlocked.Increment(ref _globalSequence);

    public OrderBookSnapshotDto CreateSnapshot(int marketId, string symbol, OrderBookEngine book, int depthLevels)
    {
        var (bids, asks) = book.GetDepth(depthLevels);
        var seq = NextSequence();
        var ts = DateTime.UtcNow.Ticks;

        var state = _states.GetOrAdd(marketId, _ => new MarketBookState());
        state.UpdateFromSnapshot(bids, asks, seq);

        return new OrderBookSnapshotDto
        {
            MarketId = marketId,
            Symbol = symbol,
            Sequence = seq,
            TimestampUtcTicks = ts,
            Bids = bids.Select(b => new PriceLevelDto(b.Price, b.Quantity)).ToList(),
            Asks = asks.Select(a => new PriceLevelDto(a.Price, a.Quantity)).ToList()
        };
    }

    /// <summary>
    /// Diff current book against last published state and return only changed levels.
    /// </summary>
    public OrderBookDeltaDto? CreateDelta(int marketId, OrderBookEngine book, int depthLevels)
    {
        if (!_states.TryGetValue(marketId, out var state))
            return null; // no prior snapshot → client must request snapshot first

        var (bids, asks) = book.GetDepth(depthLevels);
        var updates = new List<PriceLevelUpdateDto>(16);

        DiffSide(state.Bids, bids, isBid: true, updates);
        DiffSide(state.Asks, asks, isBid: false, updates);

        if (updates.Count == 0)
            return null;

        var seq = NextSequence();
        state.UpdateFromSnapshot(bids, asks, seq);

        return new OrderBookDeltaDto
        {
            MarketId = marketId,
            Sequence = seq,
            TimestampUtcTicks = DateTime.UtcNow.Ticks,
            Updates = updates
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void DiffSide(
        Dictionary<decimal, decimal> previous,
        IReadOnlyList<(decimal Price, decimal Quantity)> current,
        bool isBid,
        List<PriceLevelUpdateDto> updates)
    {
        var currentMap = new Dictionary<decimal, decimal>(current.Count);
        foreach (var (price, qty) in current)
            currentMap[price] = qty;

        // Removed or changed levels
        foreach (var kv in previous)
        {
            if (!currentMap.TryGetValue(kv.Key, out var newQty))
            {
                updates.Add(new PriceLevelUpdateDto(isBid, kv.Key, 0m)); // remove
            }
            else if (newQty != kv.Value)
            {
                updates.Add(new PriceLevelUpdateDto(isBid, kv.Key, newQty));
            }
        }

        // New levels
        foreach (var kv in currentMap)
        {
            if (!previous.ContainsKey(kv.Key))
                updates.Add(new PriceLevelUpdateDto(isBid, kv.Key, kv.Value));
        }
    }

    private sealed class MarketBookState
    {
        public Dictionary<decimal, decimal> Bids { get; private set; } = new();
        public Dictionary<decimal, decimal> Asks { get; private set; } = new();
        public long Sequence { get; private set; }

        public void UpdateFromSnapshot(
            IReadOnlyList<(decimal Price, decimal Quantity)> bids,
            IReadOnlyList<(decimal Price, decimal Quantity)> asks,
            long sequence)
        {
            Bids = bids.ToDictionary(x => x.Price, x => x.Quantity);
            Asks = asks.ToDictionary(x => x.Price, x => x.Quantity);
            Sequence = sequence;
        }
    }
}
