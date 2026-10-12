using Dex.ApiGateway.Models;
using Dex.ApiGateway.RateLimiting;
using Dex.ApiGateway.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Dex.ApiGateway.Hubs;

/// <summary>
/// SignalR hub for web / mobile clients.
/// Channels: orderbook:{marketId}, trades:{marketId}, ticker:{marketId}, mark:{marketId}
/// </summary>
public sealed class MarketDataHub : Hub
{
    private readonly MarketDataBroadcaster _broadcaster;
    private readonly CompositeRateLimiter _rateLimiter;
    private readonly ILogger<MarketDataHub> _logger;
    private readonly Dictionary<string, string> _connectionSubscriptions = new();

    public MarketDataHub(
        MarketDataBroadcaster broadcaster,
        CompositeRateLimiter rateLimiter,
        ILogger<MarketDataHub> logger)
    {
        _broadcaster = broadcaster;
        _rateLimiter = rateLimiter;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        var ip = Context.GetHttpContext()?.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (!_rateLimiter.TryAcquire(ip, apiKey: null))
        {
            _logger.LogWarning("Rate limit exceeded on connect from {Ip}", ip);
            Context.Abort();
            return;
        }

        _logger.LogInformation("SignalR connected {ConnectionId} from {Ip}", Context.ConnectionId, ip);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (_connectionSubscriptions.TryGetValue(Context.ConnectionId, out var subId))
        {
            _broadcaster.Unsubscribe(subId);
            _connectionSubscriptions.Remove(Context.ConnectionId);
        }
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Client calls: subscribeToMarketData([1,2], true, true, true, 10)
    /// </summary>
    public async Task SubscribeToMarketData(
        int[] marketIds,
        bool orderBook = true,
        bool trades = true,
        bool ticker = true,
        int depthLevels = 10)
    {
        var ip = Context.GetHttpContext()?.Connection.RemoteIpAddress?.ToString();
        if (!_rateLimiter.TryAcquire(ip, apiKey: null))
        {
            await Clients.Caller.SendAsync("Error", "Rate limit exceeded");
            return;
        }

        if (marketIds is null || marketIds.Length == 0)
        {
            await Clients.Caller.SendAsync("Error", "marketIds required");
            return;
        }

        // Unsubscribe previous
        if (_connectionSubscriptions.TryGetValue(Context.ConnectionId, out var oldId))
            _broadcaster.Unsubscribe(oldId);

        var (subId, reader) = _broadcaster.Subscribe(marketIds, orderBook, trades, ticker, depthLevels);
        _connectionSubscriptions[Context.ConnectionId] = subId;

        // Pump channel → SignalR client
        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var msg in reader.ReadAllAsync(Context.ConnectionAborted))
                {
                    switch (msg)
                    {
                        case OrderBookSnapshotDto snap:
                            await Clients.Caller.SendAsync("OrderBookSnapshot", snap, Context.ConnectionAborted);
                            break;
                        case OrderBookDeltaDto delta:
                            await Clients.Caller.SendAsync("OrderBookDelta", delta, Context.ConnectionAborted);
                            break;
                        case TradeDto trade:
                            await Clients.Caller.SendAsync("Trade", trade, Context.ConnectionAborted);
                            break;
                        case TickerDto tick:
                            await Clients.Caller.SendAsync("Ticker", tick, Context.ConnectionAborted);
                            break;
                        case MarkPriceDto mark:
                            await Clients.Caller.SendAsync("MarkPrice", mark, Context.ConnectionAborted);
                            break;
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Pump failed for {ConnectionId}", Context.ConnectionId);
            }
        });

        await Clients.Caller.SendAsync("Subscribed", subId);
    }

    public Task Unsubscribe()
    {
        if (_connectionSubscriptions.TryGetValue(Context.ConnectionId, out var subId))
        {
            _broadcaster.Unsubscribe(subId);
            _connectionSubscriptions.Remove(Context.ConnectionId);
        }
        return Task.CompletedTask;
    }
}
