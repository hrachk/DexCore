using System.Collections.Concurrent;
using System.Threading.Channels;
using Dex.ApiGateway.Models;
using Dex.MatchingEngine.OrderBook;
using OrderBookEngine = Dex.MatchingEngine.OrderBook.OrderBook;
using Microsoft.Extensions.Logging;

namespace Dex.ApiGateway.Services;

/// <summary>
/// Fan-out broadcaster for L2/L3 market data.
/// Each subscriber receives a dedicated Channel; the engine publishes
/// snapshots on subscribe and deltas thereafter.
/// Uses ArrayPool-friendly DTOs and Channels for zero-copy style fan-out.
/// </summary>
public sealed class MarketDataBroadcaster : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, Subscriber> _subscribers = new();
    private readonly ConcurrentDictionary<int, OrderBookEngine> _books = new();
    private readonly ConcurrentDictionary<int, string> _symbols = new();
    private readonly DeltaCompressionEngine _deltaEngine;
    private readonly ILogger<MarketDataBroadcaster> _logger;
    private readonly int _defaultDepth;

    public MarketDataBroadcaster(DeltaCompressionEngine deltaEngine, ILogger<MarketDataBroadcaster> logger, int defaultDepth = 10)
    {
        _deltaEngine = deltaEngine ?? throw new ArgumentNullException(nameof(deltaEngine));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _defaultDepth = defaultDepth;
    }

    public void RegisterMarket(int marketId, string symbol, OrderBookEngine book)
    {
        _books[marketId] = book;
        _symbols[marketId] = symbol;
    }

    /// <summary>
    /// Subscribe a client to one or more markets. Returns a reader that will
    /// first receive snapshots then continuous deltas / trades / tickers.
    /// </summary>
    public (string SubscriptionId, ChannelReader<object> Reader) Subscribe(
        IReadOnlyList<int> marketIds,
        bool orderBook = true,
        bool trades = true,
        bool ticker = true,
        int depthLevels = 0)
    {
        var id = Guid.NewGuid().ToString("N");
        var channel = Channel.CreateBounded<object>(new BoundedChannelOptions(256)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

        var depth = depthLevels > 0 ? depthLevels : _defaultDepth;
        var sub = new Subscriber(id, marketIds.ToHashSet(), orderBook, trades, ticker, depth, channel.Writer);
        _subscribers[id] = sub;

        // Immediately push snapshots
        if (orderBook)
        {
            foreach (var mid in marketIds)
            {
                if (_books.TryGetValue(mid, out var book))
                {
                    var symbol = _symbols.GetValueOrDefault(mid, mid.ToString());
                    var snap = _deltaEngine.CreateSnapshot(mid, symbol, book, depth);
                    channel.Writer.TryWrite(snap);
                }
            }
        }

        _logger.LogInformation("Subscriber {Id} joined markets [{Markets}]", id, string.Join(",", marketIds));
        return (id, channel.Reader);
    }

    public void Unsubscribe(string subscriptionId)
    {
        if (_subscribers.TryRemove(subscriptionId, out var sub))
        {
            sub.Writer.TryComplete();
            _logger.LogInformation("Subscriber {Id} left", subscriptionId);
        }
    }

    /// <summary>
    /// Called by MatchingEngine after book mutation. Publishes delta to all interested subscribers.
    /// </summary>
    public void PublishBookUpdate(int marketId)
    {
        if (!_books.TryGetValue(marketId, out var book))
            return;

        foreach (var sub in _subscribers.Values)
        {
            if (!sub.OrderBook || !sub.MarketIds.Contains(marketId))
                continue;

            var delta = _deltaEngine.CreateDelta(marketId, book, sub.DepthLevels);
            if (delta is not null)
                sub.Writer.TryWrite(delta);
        }
    }

    public void PublishTrade(TradeDto trade)
    {
        foreach (var sub in _subscribers.Values)
        {
            if (!sub.Trades || !sub.MarketIds.Contains(trade.MarketId))
                continue;
            sub.Writer.TryWrite(trade);
        }
    }

    public void PublishTicker(TickerDto ticker)
    {
        foreach (var sub in _subscribers.Values)
        {
            if (!sub.Ticker || !sub.MarketIds.Contains(ticker.MarketId))
                continue;
            sub.Writer.TryWrite(ticker);
        }
    }

    public void PublishMarkPrice(MarkPriceDto mark)
    {
        foreach (var sub in _subscribers.Values)
        {
            if (!sub.MarketIds.Contains(mark.MarketId))
                continue;
            sub.Writer.TryWrite(mark);
        }
    }

    public int SubscriberCount => _subscribers.Count;

    public ValueTask DisposeAsync()
    {
        foreach (var sub in _subscribers.Values)
            sub.Writer.TryComplete();
        _subscribers.Clear();
        return ValueTask.CompletedTask;
    }

    private sealed class Subscriber
    {
        public string Id { get; }
        public HashSet<int> MarketIds { get; }
        public bool OrderBook { get; }
        public bool Trades { get; }
        public bool Ticker { get; }
        public int DepthLevels { get; }
        public ChannelWriter<object> Writer { get; }

        public Subscriber(
            string id,
            HashSet<int> marketIds,
            bool orderBook,
            bool trades,
            bool ticker,
            int depthLevels,
            ChannelWriter<object> writer)
        {
            Id = id;
            MarketIds = marketIds;
            OrderBook = orderBook;
            Trades = trades;
            Ticker = ticker;
            DepthLevels = depthLevels;
            Writer = writer;
        }
    }
}
