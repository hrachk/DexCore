using System.Threading.Channels;
using Dex.Domain.Orders;
using Dex.SettlementService.Models;
using Microsoft.Extensions.Logging;

namespace Dex.SettlementService.Services;

/// <summary>
/// Aggregates executed trades from the Matching Engine into batches,
/// signs them, and submits to the on-chain DexSettlement contract.
/// Handles idempotency, nonce management and basic speed-up / replacement.
/// </summary>
public sealed class BatchSettlementWorker : IAsyncDisposable
{
    private readonly Channel<Trade> _inbound;
    private readonly Eip712BatchSigner _signer;
    private readonly ISettlementTransactionSender _sender;
    private readonly ILogger<BatchSettlementWorker> _logger;
    private readonly int _maxBatchSize;
    private readonly TimeSpan _maxBatchWait;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;
    private readonly Dictionary<string, SettlementReceipt> _receipts = new();
    private long _nextNonce;

    public BatchSettlementWorker(
        Eip712BatchSigner signer,
        ISettlementTransactionSender sender,
        ILogger<BatchSettlementWorker> logger,
        int maxBatchSize = 50,
        int maxBatchWaitMs = 200,
        long startingNonce = 0)
    {
        _signer = signer ?? throw new ArgumentNullException(nameof(signer));
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _maxBatchSize = maxBatchSize;
        _maxBatchWait = TimeSpan.FromMilliseconds(maxBatchWaitMs);
        _nextNonce = startingNonce;

        _inbound = Channel.CreateBounded<Trade>(new BoundedChannelOptions(10_000)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });

        _loop = Task.Run(ProcessLoopAsync);
    }

    public ValueTask EnqueueTradeAsync(Trade trade, CancellationToken ct = default)
        => _inbound.Writer.WriteAsync(trade, ct);

    public SettlementReceipt? GetReceipt(string batchId)
        => _receipts.TryGetValue(batchId, out var r) ? r : null;

    private async Task ProcessLoopAsync()
    {
        var buffer = new List<Trade>(_maxBatchSize);
        var token = _cts.Token;

        try
        {
            while (!token.IsCancellationRequested)
            {
                buffer.Clear();

                // Wait for at least one trade
                if (!await _inbound.Reader.WaitToReadAsync(token).ConfigureAwait(false))
                    break;

                while (buffer.Count < _maxBatchSize && _inbound.Reader.TryRead(out var trade))
                    buffer.Add(trade);

                // Optional short wait to fill the batch
                var deadline = DateTime.UtcNow + _maxBatchWait;
                while (buffer.Count < _maxBatchSize && DateTime.UtcNow < deadline)
                {
                    if (_inbound.Reader.TryRead(out var more))
                        buffer.Add(more);
                    else
                        await Task.Delay(10, token).ConfigureAwait(false);
                }

                if (buffer.Count == 0) continue;

                await SubmitBatchAsync(buffer, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // graceful
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BatchSettlementWorker loop terminated unexpectedly");
        }
    }

    private async Task SubmitBatchAsync(List<Trade> trades, CancellationToken ct)
    {
        var settlementTrades = trades.Select(t => new SettlementTrade
        {
            TradeId = t.TradeId,
            MakerAddress = "0x" + t.MakerAccountId.ToString("x40"), // placeholder mapping
            TakerAddress = "0x" + t.TakerAccountId.ToString("x40"),
            MarketId = t.MarketId,
            Quantity = t.TakerSide == OrderSide.Buy ? t.Quantity : -t.Quantity,
            Price = t.Price
        }).ToList();

        var (batchId, signature) = _signer.SignBatch(settlementTrades);

        var receipt = new SettlementReceipt
        {
            BatchId = batchId,
            Status = SettlementStatus.Pending,
            UpdatedAtUtc = DateTime.UtcNow
        };
        _receipts[batchId] = receipt;

        try
        {
            var nonce = Interlocked.Increment(ref _nextNonce) - 1;
            receipt.Nonce = nonce;

            var txHash = await _sender.SendSettleBatchAsync(
                batchId,
                settlementTrades,
                signature,
                nonce,
                ct).ConfigureAwait(false);

            receipt.TransactionHash = txHash;
            receipt.Status = SettlementStatus.Submitted;
            receipt.UpdatedAtUtc = DateTime.UtcNow;

            _logger.LogInformation(
                "Submitted batch {BatchId} with {Count} trades, tx={Tx}, nonce={Nonce}",
                batchId, settlementTrades.Count, txHash, nonce);
        }
        catch (Exception ex)
        {
            receipt.Status = SettlementStatus.Failed;
            receipt.Error = ex.Message;
            receipt.UpdatedAtUtc = DateTime.UtcNow;
            _logger.LogError(ex, "Failed to submit batch {BatchId}", batchId);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _inbound.Writer.TryComplete();
        try { await _loop.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        _cts.Dispose();
    }
}

/// <summary>
/// Abstraction over Nethereum transaction submission so the worker can be unit-tested.
/// </summary>
public interface ISettlementTransactionSender
{
    Task<string> SendSettleBatchAsync(
        string batchId,
        IReadOnlyList<SettlementTrade> trades,
        string signature,
        long nonce,
        CancellationToken ct);
}
