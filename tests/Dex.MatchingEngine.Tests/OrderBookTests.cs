using Dex.Domain.Orders;
using Dex.MatchingEngine.OrderBook;
using OrderBookType = Dex.MatchingEngine.OrderBook.OrderBook;
using FluentAssertions;
using Xunit;

namespace Dex.MatchingEngine.Tests;

public class OrderBookTests
{
    private static Order CreateLimit(long id, OrderSide side, decimal price, decimal qty, long accountId = 100)
    {
        return new Order(
            orderId: id,
            clientOrderId: id,
            accountId: accountId,
            marketId: 1,
            side: side,
            type: OrderType.Limit,
            timeInForce: TimeInForce.GTC,
            price: price,
            quantity: qty,
            createdAtTicks: DateTime.UtcNow.Ticks);
    }

    [Fact]
    public void PlaceLimit_RestingBuy_AddsToBidBook()
    {
        var book = new OrderBookType(1);
        var order = CreateLimit(1, OrderSide.Buy, 100m, 10m);

        var trades = book.PlaceLimitOrder(order);

        trades.Should().BeEmpty();
        order.Status.Should().Be(OrderStatus.New);
        book.BestBid.Should().Be(100m);
        book.BestAsk.Should().Be(0m);
    }

    [Fact]
    public void PlaceLimit_CrossingOrders_ProduceTrade()
    {
        var book = new OrderBookType(1);

        var bid = CreateLimit(1, OrderSide.Buy, 100m, 5m);
        book.PlaceLimitOrder(bid);

        var ask = CreateLimit(2, OrderSide.Sell, 100m, 5m, accountId: 200);
        var trades = book.PlaceLimitOrder(ask);

        trades.Should().HaveCount(1);
        trades[0].Price.Should().Be(100m);
        trades[0].Quantity.Should().Be(5m);
        trades[0].MakerOrderId.Should().Be(1);
        trades[0].TakerOrderId.Should().Be(2);

        bid.Status.Should().Be(OrderStatus.Filled);
        ask.Status.Should().Be(OrderStatus.Filled);
        book.BestBid.Should().Be(0m);
        book.BestAsk.Should().Be(0m);
    }

    [Fact]
    public void PlaceLimit_PartialFill_LeavesResidual()
    {
        var book = new OrderBookType(1);

        var bid = CreateLimit(1, OrderSide.Buy, 100m, 10m);
        book.PlaceLimitOrder(bid);

        var ask = CreateLimit(2, OrderSide.Sell, 100m, 4m, accountId: 200);
        var trades = book.PlaceLimitOrder(ask);

        trades.Should().HaveCount(1);
        trades[0].Quantity.Should().Be(4m);

        bid.Status.Should().Be(OrderStatus.PartiallyFilled);
        bid.RemainingQuantity.Should().Be(6m);
        ask.Status.Should().Be(OrderStatus.Filled);

        book.BestBid.Should().Be(100m);
    }

    [Fact]
    public void PlaceLimit_MultiplePriceLevels_RespectsPriceTimePriority()
    {
        var book = new OrderBookType(1);

        // Two bids at different prices
        book.PlaceLimitOrder(CreateLimit(1, OrderSide.Buy, 99m, 5m));
        book.PlaceLimitOrder(CreateLimit(2, OrderSide.Buy, 100m, 5m));

        // Aggressive sell that hits best bid first
        var sell = CreateLimit(3, OrderSide.Sell, 99m, 7m, accountId: 300);
        var trades = book.PlaceLimitOrder(sell);

        trades.Should().HaveCount(2);
        trades[0].Price.Should().Be(100m); // best price first
        trades[0].Quantity.Should().Be(5m);
        trades[1].Price.Should().Be(99m);
        trades[1].Quantity.Should().Be(2m);

        sell.RemainingQuantity.Should().Be(0m);
    }

    [Fact]
    public void CancelOrder_RemovesFromBook()
    {
        var book = new OrderBookType(1);
        var order = CreateLimit(1, OrderSide.Buy, 100m, 10m);
        book.PlaceLimitOrder(order);

        var cancelled = book.CancelOrder(1);

        cancelled.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.Cancelled);
        book.BestBid.Should().Be(0m);
    }

    [Fact]
    public void PostOnly_ThatWouldCross_IsRejected()
    {
        var book = new OrderBookType(1);
        book.PlaceLimitOrder(CreateLimit(1, OrderSide.Sell, 100m, 5m));

        var postOnlyBuy = new Order(
            orderId: 2,
            clientOrderId: 2,
            accountId: 200,
            marketId: 1,
            side: OrderSide.Buy,
            type: OrderType.Limit,
            timeInForce: TimeInForce.GTC,
            price: 100m,
            quantity: 5m,
            createdAtTicks: DateTime.UtcNow.Ticks,
            postOnly: true);

        var trades = book.PlaceLimitOrder(postOnlyBuy);

        trades.Should().BeEmpty();
        postOnlyBuy.Status.Should().Be(OrderStatus.Rejected);
    }

    [Fact]
    public void GetDepth_ReturnsCorrectLevels()
    {
        var book = new OrderBookType(1);
        book.PlaceLimitOrder(CreateLimit(1, OrderSide.Buy, 99m, 10m));
        book.PlaceLimitOrder(CreateLimit(2, OrderSide.Buy, 100m, 5m));
        book.PlaceLimitOrder(CreateLimit(3, OrderSide.Sell, 101m, 7m));
        book.PlaceLimitOrder(CreateLimit(4, OrderSide.Sell, 102m, 3m));

        var (bids, asks) = book.GetDepth(5);

        bids.Should().HaveCount(2);
        bids[0].Price.Should().Be(100m);
        bids[0].Quantity.Should().Be(5m);
        bids[1].Price.Should().Be(99m);

        asks.Should().HaveCount(2);
        asks[0].Price.Should().Be(101m);
        asks[0].Quantity.Should().Be(7m);
    }
}
