using System.Collections.Concurrent;
using Dex.Domain.Orders;
using Microsoft.Extensions.Logging;

namespace Dex.MatchingEngine;

/// <summary>
/// Multi-market registry: one MatchingEngine (and OrderBook) per marketId.
/// </summary>
public sealed class MatchingEngineHost : IAsyncDisposable
{
    private readonly ConcurrentDictionary<int, MatchingEngine> _engines = new();
    private readonly ILoggerFactory _loggerFactory;
    private readonly int _inboundCapacity;

    public MatchingEngineHost(ILoggerFactory loggerFactory, int inboundCapacity = 65_536)
    {
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _inboundCapacity = inboundCapacity;
    }

    public MatchingEngine GetOrCreate(int marketId)
    {
        return _engines.GetOrAdd(marketId, id =>
            new MatchingEngine(id, _loggerFactory.CreateLogger<MatchingEngine>(), _inboundCapacity));
    }

    public bool TryGet(int marketId, out MatchingEngine? engine) =>
        _engines.TryGetValue(marketId, out engine!);

    public IReadOnlyCollection<int> MarketIds => _engines.Keys.ToArray();

    public Task<MatchingResult> PlaceOrderAsync(Order order, CancellationToken ct = default)
    {
        var engine = GetOrCreate(order.MarketId);
        return engine.PlaceOrderAsync(order, ct);
    }

    public Task<MatchingResult> CancelOrderAsync(int marketId, long orderId, CancellationToken ct = default)
    {
        var engine = GetOrCreate(marketId);
        return engine.CancelOrderAsync(orderId, ct);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var eng in _engines.Values)
            await eng.DisposeAsync().ConfigureAwait(false);
        _engines.Clear();
    }
}
