using Dex.ApiGateway.Auth;
using Dex.ApiGateway.RateLimiting;
using Dex.Domain.Orders;
using Dex.MatchingEngine;
using Dex.RiskEngine.Services;
using Microsoft.Extensions.Logging;

namespace Dex.ApiGateway.Services;

/// <summary>
/// Trading session: rate-limit → risk → MatchingEngineHost (sync result) → SignalR book fan-out.
/// </summary>
public sealed class TradingSessionService
{
    private readonly MatchingEngineHost _host;
    private readonly global::Dex.RiskEngine.Services.RiskEngine _riskEngine;
    private readonly MarketDataBroadcaster _broadcaster;
    private readonly HmacAuthService _auth;
    private readonly CompositeRateLimiter _rateLimiter;
    private readonly ILogger<TradingSessionService> _logger;
    private long _nextOrderId = 1_000_000;

    public TradingSessionService(
        MatchingEngineHost host,
        global::Dex.RiskEngine.Services.RiskEngine riskEngine,
        MarketDataBroadcaster broadcaster,
        HmacAuthService auth,
        CompositeRateLimiter rateLimiter,
        ILogger<TradingSessionService> logger)
    {
        _host = host;
        _riskEngine = riskEngine;
        _broadcaster = broadcaster;
        _auth = auth;
        _rateLimiter = rateLimiter;
        _logger = logger;
    }

    public bool Authenticate(string apiKey, string timestamp, string signature, string method, string path, string body)
        => _auth.TryAuthenticate(apiKey, timestamp, method, path, body, signature);

    public async Task<(bool Success, long OrderId, OrderStatus Status, string Message, decimal Remaining, int FillCount)> PlaceOrderAsync(
        long accountId,
        long clientOrderId,
        int marketId,
        OrderSide side,
        OrderType type,
        TimeInForce tif,
        decimal price,
        decimal quantity,
        bool reduceOnly,
        bool postOnly,
        string? apiKey,
        string? ip,
        CancellationToken ct = default)
    {
        if (!_rateLimiter.TryAcquire(ip, apiKey))
            return (false, 0, OrderStatus.Rejected, "Rate limit exceeded", 0m, 0);

        var orderId = Interlocked.Increment(ref _nextOrderId);
        var order = new Order(
            orderId: orderId,
            clientOrderId: clientOrderId,
            accountId: accountId,
            marketId: marketId,
            side: side,
            type: type,
            timeInForce: tif,
            price: price,
            quantity: quantity,
            createdAtTicks: DateTime.UtcNow.Ticks,
            reduceOnly: reduceOnly,
            postOnly: postOnly);

        var risk = _riskEngine.ValidateOrder(order, Domain.Risk.MarginMode.Cross);
        if (!risk.IsAccepted)
        {
            _logger.LogWarning("Order rejected by risk: {Reason} account={AccountId}", risk.RejectReason, accountId);
            return (false, orderId, OrderStatus.Rejected, risk.Message, quantity, 0);
        }

        var engine = _host.GetOrCreate(marketId);
        _broadcaster.RegisterMarket(marketId, $"MKT-{marketId}", engine.Book);

        MatchingResult result;
        try
        {
            result = await _host.PlaceOrderAsync(order, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Matching failed order={OrderId} market={MarketId}", orderId, marketId);
            return (false, orderId, OrderStatus.Rejected, "Matching engine error", quantity, 0);
        }

        _broadcaster.PublishBookUpdate(marketId);

        if (result.Trades.Count > 0)
        {
            foreach (var t in result.Trades)
            {
                _broadcaster.PublishTrade(new Models.TradeDto
                {
                    MarketId = marketId,
                    TradeId = t.TradeId,
                    Price = t.Price,
                    Quantity = t.Quantity,
                    IsBuyerMaker = t.TakerSide == OrderSide.Sell,
                    TimestampUtcTicks = t.ExecutedAtTicks
                });
            }
        }

        var msg = result.Trades.Count > 0
            ? $"Filled {result.Trades.Sum(x => x.Quantity)} across {result.Trades.Count} trade(s)"
            : result.Status.ToString();

        return (true, orderId, result.Status, msg, result.RemainingQuantity, result.Trades.Count);
    }

    public async Task<(bool Success, OrderStatus Status, string Message)> CancelOrderAsync(
        long orderId,
        int marketId,
        string? apiKey,
        string? ip,
        CancellationToken ct = default)
    {
        if (!_rateLimiter.TryAcquire(ip, apiKey))
            return (false, OrderStatus.Rejected, "Rate limit exceeded");

        if (!_host.TryGet(marketId, out var engine) || engine is null)
            return (false, OrderStatus.Rejected, "Unknown market");

        var result = await engine.CancelOrderAsync(orderId, ct).ConfigureAwait(false);
        _broadcaster.PublishBookUpdate(marketId);
        return (result.Status == OrderStatus.Cancelled, result.Status, result.Status.ToString());
    }
}
