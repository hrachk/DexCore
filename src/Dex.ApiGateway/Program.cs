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

builder.Services.AddSingleton<MatchingEngineHost>(sp =>
    new MatchingEngineHost(sp.GetRequiredService<ILoggerFactory>()));

builder.Services.AddSingleton(sp =>
{
    var logger = sp.GetRequiredService<ILogger<global::Dex.RiskEngine.Services.RiskEngine>>();
    var risk = new global::Dex.RiskEngine.Services.RiskEngine(logger, initialInsuranceFund: 1_000_000m);
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

var host = app.Services.GetRequiredService<MatchingEngineHost>();
var broadcaster = app.Services.GetRequiredService<MarketDataBroadcaster>();
var coreMarkets = new (int Id, string Symbol)[]
{
    (1, "BTC-USDT"), (2, "ETH-USDT"), (3, "SOL-USDT"), (4, "BNB-USDT"),
    (5, "XRP-USDT"), (6, "DOGE-USDT"), (7, "ADA-USDT"), (8, "AVAX-USDT"),
    (9, "LINK-USDT"), (10, "DOT-USDT")
};
foreach (var (id, sym) in coreMarkets)
{
    var eng = host.GetOrCreate(id);
    broadcaster.RegisterMarket(id, sym, eng.Book);
}

_ = Task.Run(async () =>
{
    var engines = coreMarkets.Select(m => host.GetOrCreate(m.Id)).ToArray();
    var tasks = engines.Select(async eng =>
    {
        await foreach (var result in eng.Results.ReadAllAsync())
        {
            try { broadcaster.PublishBookUpdate(result.MarketId); }
            catch { }
        }
    });
    await Task.WhenAll(tasks);
});

app.MapHub<MarketDataHub>("/hubs/marketdata");

app.MapGet("/health", () =>
{
    var books = coreMarkets.Select(m =>
    {
        var eng = host.GetOrCreate(m.Id);
        return new { marketId = m.Id, symbol = m.Symbol, bestBid = eng.Book.BestBid, bestAsk = eng.Book.BestAsk };
    });
    return Results.Ok(new { status = "healthy", service = "Dex.ApiGateway", markets = books });
});

app.MapGet("/api/v1/book/{marketId:int}", (int marketId) =>
{
    if (!host.TryGet(marketId, out var eng) || eng is null)
        return Results.NotFound();
    var (bids, asks) = eng.Book.GetDepth(20);
    return Results.Ok(new
    {
        marketId,
        bestBid = eng.Book.BestBid,
        bestAsk = eng.Book.BestAsk,
        bids = bids.Select(l => new { price = l.Item1, quantity = l.Item2 }),
        asks = asks.Select(l => new { price = l.Item1, quantity = l.Item2 })
    });
});

app.MapPost("/api/v1/order", async (PlaceOrderHttpRequest req, TradingSessionService trading, HttpContext ctx) =>
{
    var ip = ctx.Connection.RemoteIpAddress?.ToString();
    var (ok, orderId, status, message, remaining, fills) = await trading.PlaceOrderAsync(
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
        ? Results.Ok(new { orderId, status = status.ToString(), remaining, fills, message })
        : Results.BadRequest(new { error = message, status = status.ToString() });
});

app.MapPost("/api/v1/order/cancel", async (CancelOrderHttpRequest req, TradingSessionService trading, HttpContext ctx) =>
{
    var ip = ctx.Connection.RemoteIpAddress?.ToString();
    var (ok, status, message) = await trading.CancelOrderAsync(req.OrderId, req.MarketId, req.ApiKey, ip);
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

public sealed record CancelOrderHttpRequest(long OrderId, int MarketId = 1, string? ApiKey = "demo-key");
