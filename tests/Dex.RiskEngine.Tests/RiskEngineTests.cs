using Dex.Domain.Orders;
using Dex.Domain.Risk;
using Dex.RiskEngine.Models;
using Dex.RiskEngine.Services;
using RiskEngineType = Dex.RiskEngine.Services.RiskEngine;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Dex.RiskEngine.Tests;

public class RiskEngineTests
{
    private readonly RiskEngineType _engine;

    public RiskEngineTests()
    {
        _engine = new RiskEngineType(NullLogger<RiskEngineType>.Instance, initialInsuranceFund: 1_000_000m);

        // BTC-PERP style market: 5% IM (20x), 2.5% MM, max pos 100 BTC, OI cap 10_000
        _engine.RegisterMarket(new MarketRiskParameters(
            marketId: 1,
            initialMarginRate: 0.05m,
            maintenanceMarginRate: 0.025m,
            maxPositionSize: 100m,
            openInterestCap: 10_000m,
            liquidationPenaltyRate: 0.005m,
            minNotional: 10m));

        _engine.UpdateMarkPrice(1, 50_000m);
        _engine.UpdateIndexPrice(1, 50_010m);
    }

    private static Order LimitBuy(long accountId, decimal price, decimal qty, long orderId = 1)
        => new(orderId, orderId, accountId, 1, OrderSide.Buy, OrderType.Limit, TimeInForce.GTC, price, qty, DateTime.UtcNow.Ticks);

    private static Order LimitSell(long accountId, decimal price, decimal qty, long orderId = 2)
        => new(orderId, orderId, accountId, 1, OrderSide.Sell, OrderType.Limit, TimeInForce.GTC, price, qty, DateTime.UtcNow.Ticks);

    [Fact]
    public void ValidateOrder_SufficientMargin_IsAccepted()
    {
        _engine.Deposit(100, 20_000m); // enough for ~0.4 BTC at 5% IM

        var order = LimitBuy(100, 50_000m, 0.2m);
        var result = _engine.ValidateOrder(order, MarginMode.Cross);

        result.IsAccepted.Should().BeTrue();
        result.RequiredInitialMargin.Should().Be(500m); // 0.2 * 50k * 0.05
        result.AvailableMargin.Should().BeGreaterThanOrEqualTo(500m);
    }

    [Fact]
    public void ValidateOrder_InsufficientMargin_IsRejected()
    {
        _engine.Deposit(101, 100m); // far too little

        var order = LimitBuy(101, 50_000m, 1m); // requires 2_500 IM
        var result = _engine.ValidateOrder(order, MarginMode.Cross);

        result.IsAccepted.Should().BeFalse();
        result.RejectReason.Should().Be(RiskRejectReason.InsufficientMargin);
        result.RequiredInitialMargin.Should().Be(2_500m);
    }

    [Fact]
    public void ValidateOrder_ExceedsMaxPositionSize_IsRejected()
    {
        _engine.Deposit(102, 1_000_000m);

        var order = LimitBuy(102, 50_000m, 150m); // > 100 max
        var result = _engine.ValidateOrder(order, MarginMode.Cross);

        result.IsAccepted.Should().BeFalse();
        result.RejectReason.Should().Be(RiskRejectReason.ExceedsMaxPositionSize);
    }

    [Fact]
    public void ValidateOrder_BelowMinNotional_IsRejected()
    {
        _engine.Deposit(103, 10_000m);

        var order = LimitBuy(103, 50_000m, 0.0001m); // notional = 5 < 10
        var result = _engine.ValidateOrder(order, MarginMode.Cross);

        result.IsAccepted.Should().BeFalse();
        result.RejectReason.Should().Be(RiskRejectReason.BelowMinNotional);
    }

    [Fact]
    public void ApplyTrade_UpdatesPositionAndRealizedPnl()
    {
        _engine.Deposit(200, 50_000m);
        _engine.Deposit(201, 50_000m);

        // Simulate a trade: account 200 buys 1 BTC @ 50_000 from 201
        var trade = new Trade(
            tradeId: 1,
            makerOrderId: 10,
            takerOrderId: 11,
            makerAccountId: 201,
            takerAccountId: 200,
            marketId: 1,
            takerSide: OrderSide.Buy,
            price: 50_000m,
            quantity: 1m,
            executedAtTicks: DateTime.UtcNow.Ticks,
            sequenceNumber: 1);

        _engine.ApplyTrade(trade, MarginMode.Cross, MarginMode.Cross);

        _engine.TryGetAccount(200, out var taker).Should().BeTrue();
        taker!.TryGetPosition(1, MarginMode.Cross, out var longPos).Should().BeTrue();
        longPos!.Side.Should().Be(PositionSide.Long);
        longPos.Quantity.Should().Be(1m);
        longPos.EntryPrice.Should().Be(50_000m);

        _engine.TryGetAccount(201, out var maker).Should().BeTrue();
        maker!.TryGetPosition(1, MarginMode.Cross, out var shortPos).Should().BeTrue();
        shortPos!.Side.Should().Be(PositionSide.Short);
        shortPos.Quantity.Should().Be(1m);
    }

    [Fact]
    public void ScanLiquidations_DetectsUnderMarginedPosition()
    {
        _engine.Deposit(300, 3_000m); // small equity

        // Open a large long that will be under-margined when price drops
        var openTrade = new Trade(1, 1, 2, 301, 300, 1, OrderSide.Buy, 50_000m, 1m, DateTime.UtcNow.Ticks, 1);
        _engine.Deposit(301, 50_000m);
        _engine.ApplyTrade(openTrade, MarginMode.Cross, MarginMode.Cross);

        // Price crashes → large unrealized loss
        _engine.UpdateMarkPrice(1, 40_000m); // -10k PnL, equity ≈ 3k - 10k = negative

        var candidates = _engine.ScanLiquidations();

        candidates.Should().NotBeEmpty();
        var c = candidates.First(x => x.AccountId == 300);
        c.Side.Should().Be(PositionSide.Long);
        c.MarginRatio.Should().BeLessThan(0.025m);
        c.LiquidationQuantity.Should().BeGreaterThan(0m);
    }

    [Fact]
    public void ExecuteLiquidation_ClosesPositionAndCreditsInsuranceFund()
    {
        _engine.Deposit(400, 2_500m);
        _engine.Deposit(401, 50_000m);

        var openTrade = new Trade(1, 1, 2, 401, 400, 1, OrderSide.Buy, 50_000m, 1m, DateTime.UtcNow.Ticks, 1);
        _engine.ApplyTrade(openTrade, MarginMode.Cross, MarginMode.Cross);

        _engine.UpdateMarkPrice(1, 45_000m); // -5k PnL → equity negative

        var candidates = _engine.ScanLiquidations();
        candidates.Should().Contain(c => c.AccountId == 400);

        var candidate = candidates.First(c => c.AccountId == 400);
        var fundBefore = _engine.InsuranceFundBalance;

        _engine.ExecuteLiquidation(candidate);

        // Position should be reduced / closed
        _engine.TryGetAccount(400, out var acc).Should().BeTrue();
        acc!.TryGetPosition(1, MarginMode.Cross, out var pos).Should().BeTrue();
        // After full or partial liquidation quantity decreases
        pos!.Quantity.Should().BeLessThan(1m);

        // Insurance fund received penalty
        _engine.InsuranceFundBalance.Should().BeGreaterThan(fundBefore);
    }

    [Fact]
    public void UnrealizedPnl_LongAndShort_AreCorrect()
    {
        var longPos = new Position(1, 1, MarginMode.Cross);
        longPos.ApplyFill(PositionSide.Long, 2m, 50_000m, 0m);
        longPos.UnrealizedPnl(52_000m).Should().Be(4_000m); // +2k * 2

        var shortPos = new Position(2, 1, MarginMode.Cross);
        shortPos.ApplyFill(PositionSide.Short, 2m, 50_000m, 0m);
        shortPos.UnrealizedPnl(48_000m).Should().Be(4_000m); // +2k * 2
    }

    [Fact]
    public void CrossMargin_AvailableAccountsForMultiplePositions()
    {
        _engine.Deposit(500, 20_000m);

        // First order consumes some margin
        var o1 = LimitBuy(500, 50_000m, 0.2m); // IM 500
        var r1 = _engine.ValidateOrder(o1, MarginMode.Cross);
        r1.IsAccepted.Should().BeTrue();

        // Simulate fill so position exists and IM is "used"
        var trade = new Trade(1, 1, 2, 501, 500, 1, OrderSide.Buy, 50_000m, 0.2m, DateTime.UtcNow.Ticks, 1);
        _engine.Deposit(501, 20_000m);
        _engine.ApplyTrade(trade, MarginMode.Cross, MarginMode.Cross);

        // Second order still has remaining equity
        var o2 = LimitBuy(500, 50_000m, 0.1m); // IM 250
        var r2 = _engine.ValidateOrder(o2, MarginMode.Cross);
        r2.IsAccepted.Should().BeTrue();
        r2.AvailableMargin.Should().BeLessThan(20_000m); // some margin already used
    }
}
