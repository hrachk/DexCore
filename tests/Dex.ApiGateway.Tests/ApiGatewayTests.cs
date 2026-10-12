using Dex.ApiGateway.Auth;
using Dex.ApiGateway.Models;
using Dex.ApiGateway.RateLimiting;
using Dex.ApiGateway.Services;
using Dex.Domain.Orders;
using Dex.MatchingEngine.OrderBook;
using OrderBookType = Dex.MatchingEngine.OrderBook.OrderBook;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Dex.ApiGateway.Tests;

public class ApiGatewayTests
{
    [Fact]
    public void TokenBucket_AllowsUpToCapacity_ThenRejects()
    {
        var limiter = new TokenBucketRateLimiter(capacity: 3, refillPerSecond: 0.001); // almost no refill
        limiter.TryAcquire("k1").Should().BeTrue();
        limiter.TryAcquire("k1").Should().BeTrue();
        limiter.TryAcquire("k1").Should().BeTrue();
        limiter.TryAcquire("k1").Should().BeFalse();
    }

    [Fact]
    public void TokenBucket_RefillsOverTime()
    {
        var limiter = new TokenBucketRateLimiter(capacity: 1, refillPerSecond: 1000);
        limiter.TryAcquire("k2").Should().BeTrue();
        // After short wait tokens should refill
        Thread.Sleep(5);
        limiter.TryAcquire("k2").Should().BeTrue();
    }

    [Fact]
    public void CompositeRateLimiter_ChecksBothIpAndApiKey()
    {
        var limiter = new CompositeRateLimiter(
            ipCapacity: 1, ipRefillPerSecond: 0.001,
            apiKeyCapacity: 10, apiKeyRefillPerSecond: 1);

        limiter.TryAcquire("1.2.3.4", "keyA").Should().BeTrue();
        limiter.TryAcquire("1.2.3.4", "keyA").Should().BeFalse(); // IP exhausted
    }

    [Fact]
    public void HmacAuth_AcceptsValidSignature()
    {
        var auth = new HmacAuthService(NullLogger<HmacAuthService>.Instance, TimeSpan.FromMinutes(5));
        auth.RegisterApiKey("test-key", "super-secret");

        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        var method = "POST";
        var path = "/api/v1/order";
        var body = "{}";
        var payload = ts + method + path + body;

        var keyBytes = System.Text.Encoding.UTF8.GetBytes("super-secret");
        var payloadBytes = System.Text.Encoding.UTF8.GetBytes(payload);
        var hash = System.Security.Cryptography.HMACSHA256.HashData(keyBytes, payloadBytes);
        var sig = Convert.ToHexString(hash).ToLowerInvariant();

        auth.TryAuthenticate("test-key", ts, method, path, body, sig).Should().BeTrue();
    }

    [Fact]
    public void HmacAuth_RejectsInvalidSignature()
    {
        var auth = new HmacAuthService(NullLogger<HmacAuthService>.Instance, TimeSpan.FromMinutes(5));
        auth.RegisterApiKey("test-key", "super-secret");

        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        auth.TryAuthenticate("test-key", ts, "POST", "/api/v1/order", "{}", "deadbeef").Should().BeFalse();
    }

    [Fact]
    public void DeltaEngine_SnapshotThenDelta_ProducesOnlyChanges()
    {
        var engine = new DeltaCompressionEngine();
        var book = new OrderBookType(1);

        // Seed book
        var buy = new Order(1, 1, 100, 1, OrderSide.Buy, OrderType.Limit, TimeInForce.GTC, 100m, 5m, DateTime.UtcNow.Ticks);
        book.PlaceLimitOrder(buy);
        var sell = new Order(2, 2, 200, 1, OrderSide.Sell, OrderType.Limit, TimeInForce.GTC, 101m, 3m, DateTime.UtcNow.Ticks);
        book.PlaceLimitOrder(sell);

        var snap = engine.CreateSnapshot(1, "BTC-USD", book, 10);
        snap.Bids.Should().HaveCount(1);
        snap.Asks.Should().HaveCount(1);

        // No change → null delta
        engine.CreateDelta(1, book, 10).Should().BeNull();

        // Add another bid
        var buy2 = new Order(3, 3, 100, 1, OrderSide.Buy, OrderType.Limit, TimeInForce.GTC, 99m, 2m, DateTime.UtcNow.Ticks);
        book.PlaceLimitOrder(buy2);

        var delta = engine.CreateDelta(1, book, 10);
        delta.Should().NotBeNull();
        delta!.Updates.Should().Contain(u => u.IsBid && u.Price == 99m && u.Quantity == 2m);
    }

    [Fact]
    public async Task MarketDataBroadcaster_SendsSnapshotOnSubscribe()
    {
        var deltaEngine = new DeltaCompressionEngine();
        var broadcaster = new MarketDataBroadcaster(deltaEngine, NullLogger<MarketDataBroadcaster>.Instance);
        var book = new OrderBookType(1);
        var buy = new Order(1, 1, 100, 1, OrderSide.Buy, OrderType.Limit, TimeInForce.GTC, 50_000m, 1m, DateTime.UtcNow.Ticks);
        book.PlaceLimitOrder(buy);
        broadcaster.RegisterMarket(1, "BTC-USD", book);

        var (subId, reader) = broadcaster.Subscribe(new[] { 1 }, orderBook: true, trades: false, ticker: false, depthLevels: 5);

        var msg = await reader.ReadAsync();
        msg.Should().BeOfType<OrderBookSnapshotDto>();
        var snap = (OrderBookSnapshotDto)msg;
        snap.MarketId.Should().Be(1);
        snap.Bids.Should().NotBeEmpty();

        broadcaster.Unsubscribe(subId);
    }

    [Fact]
    public async Task MarketDataBroadcaster_PublishesDeltaToSubscribers()
    {
        var deltaEngine = new DeltaCompressionEngine();
        var broadcaster = new MarketDataBroadcaster(deltaEngine, NullLogger<MarketDataBroadcaster>.Instance);
        var book = new OrderBookType(1);
        broadcaster.RegisterMarket(1, "BTC-USD", book);

        var (subId, reader) = broadcaster.Subscribe(new[] { 1 });

        // consume initial snapshot
        _ = await reader.ReadAsync();

        // mutate book
        var buy = new Order(10, 10, 100, 1, OrderSide.Buy, OrderType.Limit, TimeInForce.GTC, 49_000m, 2m, DateTime.UtcNow.Ticks);
        book.PlaceLimitOrder(buy);
        broadcaster.PublishBookUpdate(1);

        // should receive delta (or another snapshot-like update)
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var received = await reader.ReadAsync(cts.Token);
        received.Should().BeOfType<OrderBookDeltaDto>();

        broadcaster.Unsubscribe(subId);
    }
}
