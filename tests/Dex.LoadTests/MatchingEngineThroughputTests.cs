using System.Diagnostics;
using Dex.Domain.Orders;
using Dex.MatchingEngine.OrderBook;
using OrderBookType = Dex.MatchingEngine.OrderBook.OrderBook;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Dex.LoadTests;

/// <summary>
/// High-throughput benchmarks for the Matching Engine OrderBook.
/// Targets (single-threaded hot path on modern CPU):
///   - >= 100_000 order operations/sec
///   - p99 latency well under 500 µs for individual place/cancel
/// </summary>
public class MatchingEngineThroughputTests
{
    private readonly ITestOutputHelper _output;

    public MatchingEngineThroughputTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void OrderBook_PlaceAndMatch_Throughput()
    {
        var book = new OrderBookType(1);
        const int iterations = 50_000;
        long orderId = 1;

        // Warmup
        for (int i = 0; i < 1_000; i++)
        {
            var o = MakeLimit(orderId++, OrderSide.Buy, 100m + (i % 10), 1m);
            book.PlaceLimitOrder(o);
        }

        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        var sw = Stopwatch.StartNew();

        for (int i = 0; i < iterations; i++)
        {
            var side = (i % 2 == 0) ? OrderSide.Buy : OrderSide.Sell;
            var price = 100m + (i % 20);
            var o = MakeLimit(orderId++, side, price, 1m);
            book.PlaceLimitOrder(o);
        }

        sw.Stop();
        var opsPerSec = iterations / sw.Elapsed.TotalSeconds;
        _output.WriteLine($"Place+Match throughput: {opsPerSec:N0} ops/sec ({iterations} ops in {sw.ElapsedMilliseconds} ms)");

        // Soft target — CI machines vary; assert a reasonable floor
        opsPerSec.Should().BeGreaterThan(10_000);
    }

    [Fact]
    public void OrderBook_Cancel_Throughput()
    {
        var book = new OrderBookType(1);
        const int n = 20_000;
        var ids = new long[n];

        for (int i = 0; i < n; i++)
        {
            var o = MakeLimit(i + 1, OrderSide.Buy, 50m + (i % 50), 1m);
            book.PlaceLimitOrder(o);
            ids[i] = o.OrderId;
        }

        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < n; i++)
            book.CancelOrder(ids[i]);
        sw.Stop();

        var opsPerSec = n / sw.Elapsed.TotalSeconds;
        _output.WriteLine($"Cancel throughput: {opsPerSec:N0} ops/sec");
        opsPerSec.Should().BeGreaterThan(5_000);
    }

    [Fact]
    public void OrderBook_SingleOp_LatencyP99()
    {
        var book = new OrderBookType(1);
        const int samples = 5_000;
        var latencies = new long[samples];

        // Seed liquidity
        for (int i = 0; i < 100; i++)
            book.PlaceLimitOrder(MakeLimit(i + 1, OrderSide.Sell, 100m + i, 10m));

        long nextId = 10_000;
        for (int i = 0; i < samples; i++)
        {
            var o = MakeLimit(nextId++, OrderSide.Buy, 100m, 1m);
            var t0 = Stopwatch.GetTimestamp();
            book.PlaceLimitOrder(o);
            var t1 = Stopwatch.GetTimestamp();
            latencies[i] = (t1 - t0) * 1_000_000 / Stopwatch.Frequency; // µs
        }

        Array.Sort(latencies);
        var p50 = latencies[samples / 2];
        var p99 = latencies[(int)(samples * 0.99)];
        var p999 = latencies[(int)(samples * 0.999)];

        _output.WriteLine($"Latency µs — p50={p50} p99={p99} p99.9={p999}");

        // Guardrail: p99 should be sub-millisecond on any reasonable host
        p99.Should().BeLessThan(5_000); // 5 ms hard ceiling for CI
    }

    [Fact]
    public void OrderBook_NoUnboundedAllocations_OnHotPath()
    {
        var book = new OrderBookType(1);
        // Seed
        for (int i = 0; i < 50; i++)
            book.PlaceLimitOrder(MakeLimit(i + 1, OrderSide.Sell, 200m, 1m));

        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        var gen0Before = GC.CollectionCount(0);
        var gen1Before = GC.CollectionCount(1);

        long id = 1_000;
        for (int i = 0; i < 10_000; i++)
        {
            var o = MakeLimit(id++, OrderSide.Buy, 200m, 1m);
            book.PlaceLimitOrder(o);
        }

        var gen0After = GC.CollectionCount(0);
        var gen1After = GC.CollectionCount(1);
        _output.WriteLine($"GC gen0 delta={gen0After - gen0Before} gen1 delta={gen1After - gen1Before}");

        // We allow some gen0 (LinkedList nodes etc.) but gen1 should stay low
        (gen1After - gen1Before).Should().BeLessThan(20);
    }

    private static Order MakeLimit(long id, OrderSide side, decimal price, decimal qty)
        => new(id, id, 1, 1, side, OrderType.Limit, TimeInForce.GTC, price, qty, DateTime.UtcNow.Ticks);
}
