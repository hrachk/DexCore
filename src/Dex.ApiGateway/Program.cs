using Dex.ApiGateway.Auth;
using Dex.ApiGateway.Hubs;
using Dex.ApiGateway.RateLimiting;
using Dex.ApiGateway.Services;
using Dex.MatchingEngine;
using Dex.MatchingEngine.OrderBook;
using Dex.RiskEngine.Services;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args); 

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
    return new global::Dex.RiskEngine.Services.RiskEngine(logger, initialInsuranceFund: 1_000_000m);
});
builder.Services.AddSingleton(sp =>
{
    var auth = new HmacAuthService(sp.GetRequiredService<ILogger<HmacAuthService>>(), TimeSpan.FromMinutes(2));
    // Demo key – replace with secure store
    auth.RegisterApiKey("demo-key", "demo-secret-change-me");
    return auth;
});
builder.Services.AddSingleton(new CompositeRateLimiter(
    ipCapacity: 100,
    ipRefillPerSecond: 20,
    apiKeyCapacity: 500,
    apiKeyRefillPerSecond: 100));
builder.Services.AddSingleton<TradingSessionService>();

var app = builder.Build();

// Wire a demo market into the broadcaster
var broadcaster = app.Services.GetRequiredService<MarketDataBroadcaster>();
var engine = app.Services.GetRequiredService<global::Dex.MatchingEngine.MatchingEngine>();
var book = new global::Dex.MatchingEngine.OrderBook.OrderBook(1);
broadcaster.RegisterMarket(1, "BTC-USD", book);

app.MapHub<MarketDataHub>("/hubs/marketdata");

app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "Dex.ApiGateway" }));

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
        ? Results.Ok(new { orderId, status = status.ToString(), remaining })
        : Results.BadRequest(new { error = message, status = status.ToString() });
});

app.Run();

public sealed record PlaceOrderHttpRequest(
    long AccountId,
    long ClientOrderId,
    int MarketId,
    Dex.Domain.Orders.OrderSide Side,
    Dex.Domain.Orders.OrderType Type,
    Dex.Domain.Orders.TimeInForce TimeInForce,
    decimal Price,
    decimal Quantity,
    bool ReduceOnly,
    bool PostOnly,
    string? ApiKey);
