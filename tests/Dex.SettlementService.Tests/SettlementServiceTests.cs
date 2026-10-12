using Dex.Domain.Orders;
using Dex.SettlementService.Models;
using Dex.SettlementService.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Dex.SettlementService.Tests;

public class SettlementServiceTests
{
    private static byte[] DummyKey()
    {
        var key = new byte[32];
        for (int i = 0; i < 32; i++) key[i] = (byte)(i + 1);
        return key;
    }

    [Fact]
    public void Eip712BatchSigner_ProducesStableBatchId()
    {
        var signer = new Eip712BatchSigner(DummyKey(), NullLogger<Eip712BatchSigner>.Instance);

        var trades = new List<SettlementTrade>
        {
            new() { TradeId = 1, MakerAddress = "0xabc", TakerAddress = "0xdef", MarketId = 1, Quantity = 1m, Price = 50000m },
            new() { TradeId = 2, MakerAddress = "0xabc", TakerAddress = "0xdef", MarketId = 1, Quantity = -0.5m, Price = 50100m }
        };

        var (batchId1, sig1) = signer.SignBatch(trades);
        var (batchId2, sig2) = signer.SignBatch(trades);

        batchId1.Should().Be(batchId2);
        batchId1.Should().StartWith("0x");
        sig1.Should().StartWith("0x");
        sig1.Length.Should().BeGreaterThan(100);
    }

    [Fact]
    public async Task BatchSettlementWorker_SubmitsBatchAndRecordsReceipt()
    {
        var signer = new Eip712BatchSigner(DummyKey(), NullLogger<Eip712BatchSigner>.Instance);
        var sender = new FakeSender();
        await using var worker = new BatchSettlementWorker(
            signer, sender, NullLogger<BatchSettlementWorker>.Instance,
            maxBatchSize: 10, maxBatchWaitMs: 50, startingNonce: 0);

        var trade = new Trade(
            tradeId: 42,
            makerOrderId: 1,
            takerOrderId: 2,
            makerAccountId: 100,
            takerAccountId: 200,
            marketId: 1,
            takerSide: OrderSide.Buy,
            price: 50_000m,
            quantity: 1m,
            executedAtTicks: DateTime.UtcNow.Ticks,
            sequenceNumber: 1);

        await worker.EnqueueTradeAsync(trade);
        await Task.Delay(200); // allow batch flush

        sender.SubmittedCount.Should().BeGreaterThanOrEqualTo(1);
        sender.LastBatchId.Should().NotBeNullOrEmpty();

        var receipt = worker.GetReceipt(sender.LastBatchId!);
        receipt.Should().NotBeNull();
        receipt!.Status.Should().BeOneOf(SettlementStatus.Submitted, SettlementStatus.Pending);
        receipt.TransactionHash.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void OnChainEventListener_IgnoresDeepReorgEvents()
    {
        var listener = new OnChainEventListener(NullLogger<OnChainEventListener>.Instance, startBlock: 1000);

        listener.PublishEvent(new ChainEvent
        {
            EventName = "Deposit",
            TransactionHash = "0x1",
            BlockNumber = 1005,
            Account = "0xabc",
            Amount = 10m,
            TimestampUtc = DateTime.UtcNow
        });

        listener.LastProcessedBlock.Should().Be(1005);

        // Deep re-org event (far behind)
        listener.PublishEvent(new ChainEvent
        {
            EventName = "Deposit",
            TransactionHash = "0x2",
            BlockNumber = 980,
            Account = "0xabc",
            Amount = 5m,
            TimestampUtc = DateTime.UtcNow
        }, reorgDepth: 12);

        // Should still be at 1005
        listener.LastProcessedBlock.Should().Be(1005);
    }

    [Fact]
    public async Task OnChainEventListener_DeliversEventsToChannel()
    {
        var listener = new OnChainEventListener(NullLogger<OnChainEventListener>.Instance);

        listener.PublishEvent(new ChainEvent
        {
            EventName = "WithdrawalExecuted",
            TransactionHash = "0xabc",
            BlockNumber = 42,
            Account = "0xuser",
            Amount = 3.5m,
            TimestampUtc = DateTime.UtcNow
        });

        var read = await listener.Events.ReadAsync();
        read.EventName.Should().Be("WithdrawalExecuted");
        read.Amount.Should().Be(3.5m);
        read.BlockNumber.Should().Be(42);
    }

    private sealed class FakeSender : ISettlementTransactionSender
    {
        public int SubmittedCount;
        public string? LastBatchId;

        public Task<string> SendSettleBatchAsync(
            string batchId,
            IReadOnlyList<SettlementTrade> trades,
            string signature,
            long nonce,
            CancellationToken ct)
        {
            Interlocked.Increment(ref SubmittedCount);
            LastBatchId = batchId;
            return Task.FromResult("0x" + Guid.NewGuid().ToString("N"));
        }
    }
}
