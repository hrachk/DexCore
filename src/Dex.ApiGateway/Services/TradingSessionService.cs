using System.Threading.Channels;
using Dex.ApiGateway.Auth;
using Dex.ApiGateway.RateLimiting;
using Dex.Domain.Orders;
using Dex.MatchingEngine;
using Dex.RiskEngine.Models;
using Dex.RiskEngine.Services;
using Microsoft.Extensions.Logging;

namespace Dex.ApiGateway.Services;

/// <summary>
/// In-process trading session handler used by both gRPC bi-di stream and REST fallbacks.
/// Performs HMAC auth, rate limiting, pre-trade risk check, then forwards to MatchingEngine.
/// </summary>
public sealed class TradingSessionService
{
    private readonly MatchingEngine.MatchingEngine _matchingEngine;
    private readonly global::Dex.RiskEngine.Services.RiskEngine _riskEngine;
    private readonly HmacAuthService _auth;
    private readonly CompositeRateLimiter _rateLimiter;
    private readonly ILogger<TradingSessionService> _logger;
    private long _nextOrderId = 1_000_000;

    public TradingSessionService(
        MatchingEngine.MatchingEngine matchingEngine,
        global::Dex.RiskEngine.Services.RiskEngine riskEngine,
        HmacAuthService auth,
        CompositeRateLimiter rateLimiter,
        ILogger<TradingSessionService> logger)
    {
        _matchingEngine = matchingEngine;
        _riskEngine = riskEngine;
        _auth = auth;
        _rateLimiter = rateLimiter;
        _logger = logger;
    }

    public bool Authenticate(string apiKey, string timestamp, string signature, string method, string path, string body)
        => _auth.TryAuthenticate(apiKey, timestamp, method, path, body, signature);

    public async Task<(bool Success, long OrderId, OrderStatus Status, string Message, decimal Remaining)> PlaceOrderAsync(
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
            return (false, 0, OrderStatus.Rejected, "Rate limit exceeded", 0m);

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

        // Pre-trade risk
        var risk = _riskEngine.ValidateOrder(order, Domain.Risk.MarginMode.Cross);
        if (!risk.IsAccepted)
        {
            _logger.LogWarning("Order rejected by risk: {Reason} for account {AccountId}", risk.RejectReason, accountId);
            return (false, orderId, OrderStatus.Rejected, risk.Message, quantity);
        }

        await _matchingEngine.PlaceOrderAsync(order, ct).ConfigureAwait(false);

        // In a full system we would await the MatchingResult from the outbound channel.
        // Here we optimistically return New; ExecutionReports are pushed asynchronously.
        return (true, orderId, OrderStatus.New, "Accepted", quantity);
    }

    public async Task<(bool Success, OrderStatus Status, string Message)> CancelOrderAsync(
        long orderId,
        string? apiKey,
        string? ip,
        CancellationToken ct = default)
    {
        if (!_rateLimiter.TryAcquire(ip, apiKey))
            return (false, OrderStatus.Rejected, "Rate limit exceeded");

        await _matchingEngine.CancelOrderAsync(orderId, ct).ConfigureAwait(false);
        return (true, OrderStatus.PendingCancel, "Cancel submitted");
    }
}
