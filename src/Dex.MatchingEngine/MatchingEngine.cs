using System.Collections.Concurrent;
using System.Threading.Channels;
using Dex.Domain.Orders;
using Dex.MatchingEngine.OrderBook;
using Microsoft.Extensions.Logging;

namespace Dex.MatchingEngine;

/// <summary>
/// Ultra-low-latency Matching Engine for a single market.
/// Single-writer processing loop; Place/Cancel return the actual MatchingResult.
/// </summary>
public sealed class MatchingEngine : IAsyncDisposable
{
    private readonly int _marketId;
    private readonly OrderBook.OrderBook _book;
    private readonly Channel<MatchingCommand> _inbound;
    private readonly Channel<MatchingResult> _outbound;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<MatchingResult>> _pending = new();
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _processingLoop;
    private long _nextOrderId = 1;

    public int MarketId => _marketId;
    public OrderBook.OrderBook Book => _book;
    public ChannelReader<MatchingResult> Results => _outbound.Reader;

    public MatchingEngine(int marketId, ILogger logger, int inboundCapacity = 65_536)
    {
        if (marketId <= 0) throw new ArgumentOutOfRangeException(nameof(marketId));
        _marketId = marketId;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _book = new OrderBook.OrderBook(marketId);

        _inbound = Channel.CreateBounded<MatchingCommand>(new BoundedChannelOptions(inboundCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
        _outbound = Channel.CreateUnbounded<MatchingResult>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = true
        });

        _processingLoop = Task.Run(ProcessLoopAsync);
    }

    public async Task<MatchingResult> PlaceOrderAsync(Order order, CancellationToken ct = default)
    {
        if (order.MarketId != _marketId)
            throw new ArgumentException($"Order market {order.MarketId} does not match engine {_marketId}.");

        var tcs = new TaskCompletionSource<MatchingResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(order.OrderId, tcs))
            throw new InvalidOperationException($"Duplicate pending order id {order.OrderId}");

        try
        {
            await using var reg = ct.Register(() =>
            {
                if (_pending.TryRemove(order.OrderId, out var pending))
                    pending.TrySetCanceled(ct);
            });

            await _inbound.Writer.WriteAsync(
                new MatchingCommand(MatchingCommandType.Place, order, null), ct).ConfigureAwait(false);

            return await tcs.Task.ConfigureAwait(false);
        }
        catch
        {
            _pending.TryRemove(order.OrderId, out _);
            throw;
        }
    }

    public async Task<MatchingResult> CancelOrderAsync(long orderId, CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<MatchingResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var key = -orderId;
        if (!_pending.TryAdd(key, tcs))
            throw new InvalidOperationException($"Duplicate pending cancel for {orderId}");

        try
        {
            await using var reg = ct.Register(() =>
            {
                if (_pending.TryRemove(key, out var pending))
                    pending.TrySetCanceled(ct);
            });

            await _inbound.Writer.WriteAsync(
                new MatchingCommand(MatchingCommandType.Cancel, null, orderId), ct).ConfigureAwait(false);

            return await tcs.Task.ConfigureAwait(false);
        }
        catch
        {
            _pending.TryRemove(key, out _);
            throw;
        }
    }

    private async Task ProcessLoopAsync()
    {
        var reader = _inbound.Reader;
        var token = _cts.Token;

        try
        {
            await foreach (var cmd in reader.ReadAllAsync(token).ConfigureAwait(false))
            {
                try
                {
                    switch (cmd.Type)
                    {
                        case MatchingCommandType.Place when cmd.Order is not null:
                            ProcessPlace(cmd.Order);
                            break;
                        case MatchingCommandType.Cancel when cmd.OrderId.HasValue:
                            ProcessCancel(cmd.OrderId.Value);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unhandled exception while processing {Type} market={MarketId}",
                        cmd.Type, _marketId);
                }
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            foreach (var kv in _pending)
                kv.Value.TrySetCanceled();
            _pending.Clear();
            _outbound.Writer.TryComplete();
        }
    }

    private void ProcessPlace(Order order)
    {
        if (order.OrderId <= 0)
            Interlocked.Increment(ref _nextOrderId);

        IReadOnlyList<Trade> trades = order.Type == OrderType.Market
            ? _book.PlaceMarketOrder(order)
            : _book.PlaceLimitOrder(order);

        var result = new MatchingResult(
            order.OrderId,
            _marketId,
            order.Status,
            order.RemainingQuantity,
            trades,
            DateTime.UtcNow.Ticks);

        CompletePending(order.OrderId, result);
        _outbound.Writer.TryWrite(result);
    }

    private void ProcessCancel(long orderId)
    {
        var success = _book.CancelOrder(orderId);
        var status = success ? OrderStatus.Cancelled : OrderStatus.Rejected;
        var result = new MatchingResult(
            orderId,
            _marketId,
            status,
            0m,
            Array.Empty<Trade>(),
            DateTime.UtcNow.Ticks);

        CompletePending(-orderId, result);
        _outbound.Writer.TryWrite(result);
    }

    private void CompletePending(long key, MatchingResult result)
    {
        if (_pending.TryRemove(key, out var tcs))
            tcs.TrySetResult(result);
    }

    public (IReadOnlyList<(decimal Price, decimal Quantity)> Bids,
            IReadOnlyList<(decimal Price, decimal Quantity)> Asks) GetDepth(int levels = 10)
        => _book.GetDepth(levels);

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _inbound.Writer.TryComplete();
        try { await _processingLoop.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        _cts.Dispose();
    }
}

public enum MatchingCommandType : byte
{
    Place = 0,
    Cancel = 1
}

public readonly struct MatchingCommand
{
    public MatchingCommandType Type { get; }
    public Order? Order { get; }
    public long? OrderId { get; }

    public MatchingCommand(MatchingCommandType type, Order? order, long? orderId)
    {
        Type = type;
        Order = order;
        OrderId = orderId;
    }
}

public readonly struct MatchingResult
{
    public long OrderId { get; }
    public int MarketId { get; }
    public OrderStatus Status { get; }
    public decimal RemainingQuantity { get; }
    public IReadOnlyList<Trade> Trades { get; }
    public long TimestampTicks { get; }

    public MatchingResult(
        long orderId,
        int marketId,
        OrderStatus status,
        decimal remainingQuantity,
        IReadOnlyList<Trade> trades,
        long timestampTicks)
    {
        OrderId = orderId;
        MarketId = marketId;
        Status = status;
        RemainingQuantity = remainingQuantity;
        Trades = trades;
        TimestampTicks = timestampTicks;
    }
}
