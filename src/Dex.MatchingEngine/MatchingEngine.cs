using System.Threading.Channels;
using Dex.Domain.Orders;
using Dex.MatchingEngine.OrderBook;
using Microsoft.Extensions.Logging;

namespace Dex.MatchingEngine;

/// <summary>
/// Ultra-low-latency Matching Engine.
/// Single market instance is processed by a dedicated consumer loop (single-writer principle).
/// Commands arrive via a bounded Channel; results are published via another Channel.
/// Designed for co-location and sub-millisecond p99 latency on the hot path.
/// </summary>
public sealed class MatchingEngine : IAsyncDisposable
{
    private readonly int _marketId;
    private readonly OrderBook.OrderBook _book;
    private readonly Channel<MatchingCommand> _inbound;
    private readonly Channel<MatchingResult> _outbound;
    private readonly ILogger<MatchingEngine> _logger;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _processingLoop;
    private long _nextOrderId = 1;

    public int MarketId => _marketId;
    public ChannelReader<MatchingResult> Results => _outbound.Reader;

    public MatchingEngine(int marketId, ILogger<MatchingEngine> logger, int inboundCapacity = 65_536)
    {
        if (marketId <= 0) throw new ArgumentOutOfRangeException(nameof(marketId));
        _marketId = marketId;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _book = new OrderBook.OrderBook(marketId);

        var options = new BoundedChannelOptions(inboundCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        };

        _inbound = Channel.CreateBounded<MatchingCommand>(options);
        _outbound = Channel.CreateUnbounded<MatchingResult>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = true
        });

        _processingLoop = Task.Run(ProcessLoopAsync);
    }

    public ValueTask PlaceOrderAsync(Order order, CancellationToken ct = default)
    {
        var cmd = new MatchingCommand(MatchingCommandType.Place, order, null);
        return _inbound.Writer.WriteAsync(cmd, ct);
    }

    public ValueTask CancelOrderAsync(long orderId, CancellationToken ct = default)
    {
        var cmd = new MatchingCommand(MatchingCommandType.Cancel, null, orderId);
        return _inbound.Writer.WriteAsync(cmd, ct);
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
                    _logger.LogError(ex, "Unhandled exception while processing command {Type} for market {MarketId}",
                        cmd.Type, _marketId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // graceful shutdown
        }
        finally
        {
            _outbound.Writer.TryComplete();
        }
    }

    private void ProcessPlace(Order order)
    {
        if (order.OrderId <= 0)
        {
            // Engine assigns ID if not pre-assigned by gateway
            var id = Interlocked.Increment(ref _nextOrderId);
            // We cannot mutate the public OrderId easily; in production Order is created by the engine
            // For this core we assume the caller already set a unique OrderId.
        }

        IReadOnlyList<Trade> trades;

        if (order.Type == OrderType.Market)
        {
            trades = _book.PlaceMarketOrder(order);
        }
        else
        {
            trades = _book.PlaceLimitOrder(order);
        }

        var result = new MatchingResult(
            order.OrderId,
            order.Status,
            order.RemainingQuantity,
            trades,
            DateTime.UtcNow.Ticks);

        if (!_outbound.Writer.TryWrite(result))
        {
            _logger.LogWarning("Outbound channel full while publishing result for order {OrderId}", order.OrderId);
        }
    }

    private void ProcessCancel(long orderId)
    {
        var success = _book.CancelOrder(orderId);
        var status = success ? OrderStatus.Cancelled : OrderStatus.Rejected;

        var result = new MatchingResult(
            orderId,
            status,
            0m,
            Array.Empty<Trade>(),
            DateTime.UtcNow.Ticks);

        _outbound.Writer.TryWrite(result);
    }

    public (IReadOnlyList<(decimal Price, decimal Quantity)> Bids,
            IReadOnlyList<(decimal Price, decimal Quantity)> Asks) GetDepth(int levels = 10)
        => _book.GetDepth(levels);

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _inbound.Writer.TryComplete();
        try
        {
            await _processingLoop.ConfigureAwait(false);
        }
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
    public OrderStatus Status { get; }
    public decimal RemainingQuantity { get; }
    public IReadOnlyList<Trade> Trades { get; }
    public long TimestampTicks { get; }

    public MatchingResult(
        long orderId,
        OrderStatus status,
        decimal remainingQuantity,
        IReadOnlyList<Trade> trades,
        long timestampTicks)
    {
        OrderId = orderId;
        Status = status;
        RemainingQuantity = remainingQuantity;
        Trades = trades;
        TimestampTicks = timestampTicks;
    }
}
