using Dex.ApiGateway.Services;
using Dex.ComplianceService.Models;
using Dex.ComplianceService.Services;
using Dex.Domain.Orders;
using Dex.Domain.Risk;
using Dex.MatchingEngine;
using Dex.MatchingEngine.OrderBook;
using Dex.RiskEngine.Services;
using RiskEngineType = Dex.RiskEngine.Services.RiskEngine;
using OrderBookType = Dex.MatchingEngine.OrderBook.OrderBook;
using MatchingEngineType = Dex.MatchingEngine.MatchingEngine;
using Dex.SettlementService.Models;
using Dex.SettlementService.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Dex.IntegrationTests;

/// <summary>
/// End-to-end in-process integration: Compliance → Risk → Matching → Settlement → Market Data.
/// Simulates the full institutional trade lifecycle without external nodes.
/// </summary>
public class EndToEndTradingFlowTests
{
    private readonly AmlScreeningService _aml;
    private readonly ZkProofGeneratorAdapter _zk;
    private readonly ComplianceRuleEngine _compliance;
    private readonly RiskEngineType _risk;
    private readonly MatchingEngineType _matching;
    private readonly OrderBookType _book;
    private readonly DeltaCompressionEngine _delta;
    private readonly MarketDataBroadcaster _broadcaster;
    private readonly Eip712BatchSigner _signer;
    private readonly BatchSettlementWorker _settlement;

    public EndToEndTradingFlowTests()
    {
        _aml = new AmlScreeningService(NullLogger<AmlScreeningService>.Instance);
        _zk = new ZkProofGeneratorAdapter(NullLogger<ZkProofGeneratorAdapter>.Instance);
        _compliance = new ComplianceRuleEngine(
            _aml, _zk, NullLogger<ComplianceRuleEngine>.Instance,
            new ComplianceRule
            {
                Name = "e2e",
                MaxNotionalWithoutKyc = 100_000m,
                RequireZkForAll = false,
                MaxSingleWithdrawal = 1_000_000m
            });

        _risk = new RiskEngineType(NullLogger<RiskEngineType>.Instance, initialInsuranceFund: 10_000_000m);
        _risk.RegisterMarket(new MarketRiskParameters(
            marketId: 1,
            initialMarginRate: 0.05m,
            maintenanceMarginRate: 0.025m,
            maxPositionSize: 100m,
            openInterestCap: 10_000m));
        _risk.UpdateMarkPrice(1, 50_000m);

        _matching = new MatchingEngineType(1, NullLogger<MatchingEngineType>.Instance);
        _book = new OrderBookType(1);

        _delta = new DeltaCompressionEngine();
        _broadcaster = new MarketDataBroadcaster(_delta, NullLogger<MarketDataBroadcaster>.Instance);
        _broadcaster.RegisterMarket(1, "BTC-USD", _book);

        var key = new byte[32];
        for (int i = 0; i < 32; i++) key[i] = (byte)(i + 1);
        _signer = new Eip712BatchSigner(key, NullLogger<Eip712BatchSigner>.Instance);
        _settlement = new BatchSettlementWorker(
            _signer,
            new FakeSettlementSender(),
            NullLogger<BatchSettlementWorker>.Instance,
            maxBatchSize: 10,
            maxBatchWaitMs: 50);
    }

    [Fact]
    public async Task FullTradeLifecycle_DepositComplianceMatchSettle_Succeeds()
    {
        const long alice = 1001;
        const long bob = 1002;
        const string aliceAddr = "0xalice00000000000000000000000000000000001";
        const string bobAddr = "0xbob0000000000000000000000000000000000002";

        // 1. Compliance screening
        var aliceDecision = _compliance.EvaluateDepositOrTrade(aliceAddr, notional: 50_000m, attestation: null);
        aliceDecision.Action.Should().Be(ComplianceAction.Allow);

        var bobDecision = _compliance.EvaluateDepositOrTrade(bobAddr, notional: 50_000m, attestation: null);
        bobDecision.Action.Should().Be(ComplianceAction.Allow);

        // 2. Fund accounts in Risk Engine (simulates Vault deposit sync)
        _risk.Deposit(alice, 100_000m);
        _risk.Deposit(bob, 100_000m);

        // 3. Pre-trade risk check + place resting bid
        var bid = new Order(1, 1, alice, 1, OrderSide.Buy, OrderType.Limit, TimeInForce.GTC,
            50_000m, 1m, DateTime.UtcNow.Ticks);
        var riskBid = _risk.ValidateOrder(bid, MarginMode.Cross);
        riskBid.IsAccepted.Should().BeTrue();

        var bidTrades = _book.PlaceLimitOrder(bid);
        bidTrades.Should().BeEmpty(); // resting
        bid.Status.Should().Be(OrderStatus.New);

        // 4. Market data snapshot available
        var (subId, reader) = _broadcaster.Subscribe(new[] { 1 });
        var snapMsg = await reader.ReadAsync();
        snapMsg.Should().BeOfType<ApiGateway.Models.OrderBookSnapshotDto>();

        // 5. Crossing ask → trade
        var ask = new Order(2, 2, bob, 1, OrderSide.Sell, OrderType.Limit, TimeInForce.GTC,
            50_000m, 1m, DateTime.UtcNow.Ticks);
        var riskAsk = _risk.ValidateOrder(ask, MarginMode.Cross);
        riskAsk.IsAccepted.Should().BeTrue();

        var trades = _book.PlaceLimitOrder(ask);
        trades.Should().HaveCount(1);
        trades[0].Price.Should().Be(50_000m);
        trades[0].Quantity.Should().Be(1m);

        // 6. Apply trade to risk positions
        _risk.ApplyTrade(trades[0], MarginMode.Cross, MarginMode.Cross);

        _risk.TryGetAccount(alice, out var aliceAcc).Should().BeTrue();
        aliceAcc!.TryGetPosition(1, MarginMode.Cross, out var longPos).Should().BeTrue();
        longPos!.Side.Should().Be(PositionSide.Long);
        longPos.Quantity.Should().Be(1m);

        // 7. Publish book update → delta to subscribers
        _broadcaster.PublishBookUpdate(1);
        _broadcaster.PublishTrade(new ApiGateway.Models.TradeDto
        {
            MarketId = 1,
            TradeId = trades[0].TradeId,
            Price = trades[0].Price,
            Quantity = trades[0].Quantity,
            IsBuyerMaker = true,
            TimestampUtcTicks = DateTime.UtcNow.Ticks
        });

        // 8. Settlement batch
        await _settlement.EnqueueTradeAsync(trades[0]);
        await Task.Delay(150);

        // Cleanup
        _broadcaster.Unsubscribe(subId);
        await _settlement.DisposeAsync();
        await _matching.DisposeAsync();
    }

    [Fact]
    public void InsufficientMargin_OrderRejectedByRisk()
    {
        _risk.Deposit(2001, 10m); // tiny balance
        var order = new Order(10, 10, 2001, 1, OrderSide.Buy, OrderType.Limit, TimeInForce.GTC,
            50_000m, 1m, DateTime.UtcNow.Ticks); // requires 2500 IM

        var result = _risk.ValidateOrder(order, MarginMode.Cross);
        result.IsAccepted.Should().BeFalse();
        result.RejectReason.Should().Be(RiskEngine.Models.RiskRejectReason.InsufficientMargin);
    }

    [Fact]
    public void SanctionedAddress_ComplianceDenies()
    {
        _aml.AddToBlocklist("0xsanctioned");
        var decision = _compliance.EvaluateDepositOrTrade("0xsanctioned", 100m, null);
        decision.Action.Should().Be(ComplianceAction.Deny);
    }

    [Fact]
    public void MarkPriceCrash_TriggersLiquidationScan()
    {
        _risk.Deposit(3001, 3_000m);
        _risk.Deposit(3002, 100_000m);

        var trade = new Trade(1, 1, 2, 3002, 3001, 1, OrderSide.Buy, 50_000m, 1m,
            DateTime.UtcNow.Ticks, 1);
        _risk.ApplyTrade(trade, MarginMode.Cross, MarginMode.Cross);

        _risk.UpdateMarkPrice(1, 40_000m); // -10k UPNL

        var candidates = _risk.ScanLiquidations();
        candidates.Should().Contain(c => c.AccountId == 3001);
    }

    [Fact]
    public void CancelRestingOrder_RemovesFromBook()
    {
        var order = new Order(50, 50, 4001, 1, OrderSide.Buy, OrderType.Limit, TimeInForce.GTC,
            49_000m, 2m, DateTime.UtcNow.Ticks);
        _book.PlaceLimitOrder(order);
        _book.BestBid.Should().Be(49_000m);

        var cancelled = _book.CancelOrder(50);
        cancelled.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.Cancelled);
        _book.BestBid.Should().Be(0m);
    }

    private sealed class FakeSettlementSender : ISettlementTransactionSender
    {
        public Task<string> SendSettleBatchAsync(
            string batchId,
            IReadOnlyList<SettlementTrade> trades,
            string signature,
            long nonce,
            CancellationToken ct)
            => Task.FromResult("0x" + Guid.NewGuid().ToString("N"));
    }
}
