using Dex.ApiGateway.Auth;
using Dex.ApiGateway.Hubs;
using Dex.ApiGateway.RateLimiting;
using Dex.ApiGateway.Services;
using Dex.MatchingEngine;
using Dex.RiskEngine.Services;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});

builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyHeader().AllowAnyMethod().AllowCredentials()
     .SetIsOriginAllowed(_ => true)));

builder.Services.AddSignalR();
builder.Services.AddSingleton<DeltaCompressionEngine>();
builder.Services.AddSingleton<MarketDataBroadcaster>();
builder.Services.AddSingleton(sp =>
{
    var logger = sp.GetRequiredService<ILogger<global::Dex.MatchingEngine.MatchingEngine>>();
    return new global::Dex.MatchingEngine.MatchingEngine(marketId: 1, logger);
});
builder.Services.AddSingleton(sp =>
{
    var logger = sp.GetRequiredService<ILogger<global::Dex.RiskEngine.Services.RiskEngine>>();
    var risk = new global::Dex.RiskEngine.Services.RiskEngine(logger, initialInsuranceFund: 1_000_000m);
    // Seed demo accounts so pre-trade checks pass
    risk.Deposit(1, 1_000_000m);
    risk.Deposit(100, 1_000_000m);
    risk.Deposit(200, 1_000_000m);
    return risk;
});
builder.Services.AddSingleton(sp =>
{
    var auth = new HmacAuthService(sp.GetRequiredService<ILogger<HmacAuthService>>(), TimeSpan.FromMinutes(2));
    auth.RegisterApiKey("demo-key", "demo-secret-change-me");
    return auth;
});
builder.Services.AddSingleton(new CompositeRateLimiter(
    ipCapacity: 200,
    ipRefillPerSecond: 40,
    apiKeyCapacity: 1000,
    apiKeyRefillPerSecond: 200));
builder.Services.AddSingleton<TradingSessionService>();

var app = builder.Build();
app.UseCors();

// CRITICAL: broadcaster must use the SAME book instance as MatchingEngine
var broadcaster = app.Services.GetRequiredService<MarketDataBroadcaster>();
var engine = app.Services.GetRequiredService<global::Dex.MatchingEngine.MatchingEngine>();
broadcaster.RegisterMarket(1, "BTC-USDT", engine.Book);

// Background: publish book deltas after matching results
_ = Task.Run(async () =>
{
    await foreach (var result in engine.Results.ReadAllAsync())
    {
        try { broadcaster.PublishBookUpdate(1); }
        catch { /* ignore */ }
    }
});

app.MapHub<MarketDataHub>("/hubs/marketdata");

app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    service = "Dex.ApiGateway",
    bestBid = engine.Book.BestBid,
    bestAsk = engine.Book.BestAsk
}));

app.MapGet("/api/v1/book/{marketId:int}", (int marketId) =>
{
    if (marketId != 1) return Results.NotFound();
    var (bids, asks) = engine.Book.GetDepth(20);
    return Results.Ok(new
    {
        marketId,
        bestBid = engine.Book.BestBid,
        bestAsk = engine.Book.BestAsk,
        bids = bids.Select(l => new { price = l.Item1, quantity = l.Item2 }),
        asks = asks.Select(l => new { price = l.Item1, quantity = l.Item2 })
    });
});

app.MapPost("/api/v1/order", async (PlaceOrderHttpRequest req, TradingSessionService trading, HttpContext ctx) =>
{
    var ip = ctx.Connection.RemoteIpAddress?.ToString();
    var (ok, orderId, status, message, remaining) = await trading.PlaceOrderAsync(
        accountId: req.AccountId,
        clientOrderId: req.ClientOrderId,
        marketId: req.MarketId,
        side: req.Side,
        type: req.Type,
        tif: req.TimeInForce,
        price: req.Price,
        quantity: req.Quantity,
        reduceOnly: req.ReduceOnly,
        postOnly: req.PostOnly,
        apiKey: req.ApiKey,
        ip: ip);

    return ok
        ? Results.Ok(new { orderId, status = status.ToString(), remaining, message })
        : Results.BadRequest(new { error = message, status = status.ToString() });
});

app.MapPost("/api/v1/order/cancel", async (CancelOrderHttpRequest req, TradingSessionService trading, HttpContext ctx) =>
{
    var ip = ctx.Connection.RemoteIpAddress?.ToString();
    var (ok, status, message) = await trading.CancelOrderAsync(req.OrderId, req.ApiKey, ip);
    return ok ? Results.Ok(new { status = status.ToString(), message })
              : Results.BadRequest(new { error = message });
});

app.Run("http://0.0.0.0:5080");

public sealed record PlaceOrderHttpRequest(
    long AccountId,
    long ClientOrderId,
    int MarketId,
    Dex.Domain.Orders.OrderSide Side,
    Dex.Domain.Orders.OrderType Type,
    Dex.Domain.Orders.TimeInForce TimeInForce,
    decimal Price,
    decimal Quantity,
    bool ReduceOnly = false,
    bool PostOnly = false,
    string? ApiKey = "demo-key");

public sealed record CancelOrderHttpRequest(long OrderId, string? ApiKey = "demo-key");
