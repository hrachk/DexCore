using System.Diagnostics;
using Dex.Domain.Orders;
using Dex.MatchingEngine.OrderBook;
using OrderBookType = Dex.MatchingEngine.OrderBook.OrderBook;

// Standalone load runner: dotnet run --project tests/Dex.LoadTests
Console.WriteLine("=== DexCore Matching Engine Load Runner ===");
Console.WriteLine($"Runtime: {Environment.Version}  CPUs: {Environment.ProcessorCount}");

var book = new OrderBookType(1);
const int warmup = 5_000;
const int iterations = 200_000;
long orderId = 1;

for (int i = 0; i < warmup; i++)
{
    var side = i % 2 == 0 ? OrderSide.Buy : OrderSide.Sell;
    book.PlaceLimitOrder(new Order(orderId++, orderId, 1, 1, side, OrderType.Limit, TimeInForce.GTC,
        100m + (i % 30), 1m, DateTime.UtcNow.Ticks));
}

GC.Collect(2, GCCollectionMode.Forced, true);
var gen0 = GC.CollectionCount(0);

var sw = Stopwatch.StartNew();
for (int i = 0; i < iterations; i++)
{
    var side = i % 2 == 0 ? OrderSide.Buy : OrderSide.Sell;
    book.PlaceLimitOrder(new Order(orderId++, orderId, 1, 1, side, OrderType.Limit, TimeInForce.GTC,
        100m + (i % 30), 1m, DateTime.UtcNow.Ticks));
}
sw.Stop();

var ops = iterations / sw.Elapsed.TotalSeconds;
Console.WriteLine($"Throughput: {ops:N0} ops/sec");
Console.WriteLine($"Elapsed:    {sw.Elapsed.TotalMilliseconds:F1} ms");
Console.WriteLine($"Gen0 GC:    {GC.CollectionCount(0) - gen0}");
Console.WriteLine($"BestBid:    {book.BestBid}  BestAsk: {book.BestAsk}");
Console.WriteLine("Done.");
