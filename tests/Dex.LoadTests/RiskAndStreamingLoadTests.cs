using System.Diagnostics;
using Dex.ApiGateway.Services;
using Dex.Domain.Orders;
using Dex.Domain.Risk;
using Dex.MatchingEngine.OrderBook;
using Dex.RiskEngine.Services;
using RiskEngineType = Dex.RiskEngine.Services.RiskEngine;
using OrderBookType = Dex.MatchingEngine.OrderBook.OrderBook;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace Dex.LoadTests;

public class RiskAndStreamingLoadTests
{
    private readonly ITestOutputHelper _output;

    public RiskAndStreamingLoadTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void RiskEngine_ConcurrentValidations()
    {
        var risk = new RiskEngineType(NullLogger<RiskEngineType>.Instance);
        risk.RegisterMarket(new MarketRiskParameters(1, 0.05m, 0.025m, 1_000m, 100_000m));
        risk.UpdateMarkPrice(1, 50_000m);

        const int accounts = 200;
        const int ordersPerAccount = 50;

        for (int a = 1; a <= accounts; a++)
            risk.Deposit(a, 1_000_000m);

        var sw = Stopwatch.StartNew();
        var accepted = 0;

        Parallel.For(1, accounts + 1, a =>
        {
            for (int i = 0; i < ordersPerAccount; i++)
            {
                var order = new Order(
                    orderId: a * 10_000L + i,
                    clientOrderId: i,
                    accountId: a,
                    marketId: 1,
                    side: OrderSide.Buy,
                    type: OrderType.Limit,
                    timeInForce: TimeInForce.GTC,
                    price: 50_000m,
                    quantity: 0.01m,
                    createdAtTicks: DateTime.UtcNow.Ticks);

                var r = risk.ValidateOrder(order, MarginMode.Cross);
                if (r.IsAccepted) Interlocked.Increment(ref accepted);
            }
        });

        sw.Stop();
        var total = accounts * ordersPerAccount;
        var opsPerSec = total / sw.Elapsed.TotalSeconds;
        _output.WriteLine($"Risk validations: {total} in {sw.ElapsedMilliseconds} ms → {opsPerSec:N0} ops/sec (accepted={accepted})");
        opsPerSec.Should().BeGreaterThan(1_000);
        accepted.Should().Be(total);
    }

    [Fact]
    public async Task MarketDataBroadcaster_FanOut_ManySubscribers()
    {
        var delta = new DeltaCompressionEngine();
        var broadcaster = new MarketDataBroadcaster(delta, NullLogger<MarketDataBroadcaster>.Instance);
        var book = new OrderBookType(1);
        broadcaster.RegisterMarket(1, "BTC-USD", book);

        const int subscriberCount = 500; // scaled for CI; production target 10k+
        var readers = new List<System.Threading.Channels.ChannelReader<object>>(subscriberCount);
        var subIds = new List<string>(subscriberCount);

        for (int i = 0; i < subscriberCount; i++)
        {
            var (id, reader) = broadcaster.Subscribe(new[] { 1 });
            subIds.Add(id);
            readers.Add(reader);
            // drain initial snapshot
            _ = await reader.ReadAsync();
        }

        // Mutate book and publish
        var buy = new Order(1, 1, 1, 1, OrderSide.Buy, OrderType.Limit, TimeInForce.GTC,
            49_000m, 1m, DateTime.UtcNow.Ticks);
        book.PlaceLimitOrder(buy);

        var sw = Stopwatch.StartNew();
        broadcaster.PublishBookUpdate(1);
        sw.Stop();

        // Each subscriber should receive a delta
        var received = 0;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await Parallel.ForEachAsync(readers, cts.Token, async (reader, ct) =>
        {
            try
            {
                var msg = await reader.ReadAsync(ct);
                if (msg is ApiGateway.Models.OrderBookDeltaDto)
                    Interlocked.Increment(ref received);
            }
            catch (OperationCanceledException) { }
        });

        _output.WriteLine($"Fan-out to {subscriberCount} subs in {sw.Elapsed.TotalMilliseconds:F2} ms, received={received}");
        received.Should().BeGreaterThan(subscriberCount / 2); // majority got the delta

        foreach (var id in subIds)
            broadcaster.Unsubscribe(id);

        await broadcaster.DisposeAsync();
    }

    [Fact]
    public void RiskEngine_LiquidationScan_UnderLoad()
    {
        var risk = new RiskEngineType(NullLogger<RiskEngineType>.Instance, initialInsuranceFund: 1_000_000m);
        risk.RegisterMarket(new MarketRiskParameters(1, 0.05m, 0.025m, 100m, 10_000m));
        risk.UpdateMarkPrice(1, 50_000m);

        // Open many thin positions
        for (int a = 1; a <= 100; a++)
        {
            risk.Deposit(a, 2_500m);
            risk.Deposit(10_000 + a, 100_000m);
            var trade = new Trade(a, a, a + 1000, 10_000 + a, a, 1, OrderSide.Buy, 50_000m, 1m,
                DateTime.UtcNow.Ticks, a);
            risk.ApplyTrade(trade, MarginMode.Cross, MarginMode.Cross);
        }

        risk.UpdateMarkPrice(1, 45_000m);

        var sw = Stopwatch.StartNew();
        var candidates = risk.ScanLiquidations();
        sw.Stop();

        _output.WriteLine($"Liquidation scan: {candidates.Count} candidates in {sw.Elapsed.TotalMilliseconds:F2} ms");
        candidates.Count.Should().BeGreaterThan(0);
        sw.ElapsedMilliseconds.Should().BeLessThan(2_000);
    }
}
