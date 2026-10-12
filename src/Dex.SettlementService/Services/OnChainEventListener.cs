using System.Threading.Channels;
using Dex.SettlementService.Models;
using Microsoft.Extensions.Logging;

namespace Dex.SettlementService.Services;

/// <summary>
/// Listens for on-chain events (Deposit, WithdrawalExecuted, LiquidationSettled, BatchSettled)
/// and pushes them into an internal channel for RiskEngine / balance synchronisation.
/// Designed to be driven by Nethereum WebSocket subscriptions in production.
/// </summary>
public sealed class OnChainEventListener : IAsyncDisposable
{
    private readonly Channel<ChainEvent> _events;
    private readonly ILogger<OnChainEventListener> _logger;
    private readonly CancellationTokenSource _cts = new();
    private long _lastProcessedBlock;
    private bool _running;

    public ChannelReader<ChainEvent> Events => _events.Reader;
    public long LastProcessedBlock => Interlocked.Read(ref _lastProcessedBlock);

    public OnChainEventListener(ILogger<OnChainEventListener> logger, long startBlock = 0)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _lastProcessedBlock = startBlock;
        _events = Channel.CreateUnbounded<ChainEvent>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = true
        });
    }

    /// <summary>
    /// Injects an event (used by unit tests and by the Nethereum subscription callback).
    /// Handles basic re-org protection by ignoring blocks older than last processed - reorgDepth.
    /// </summary>
    public void PublishEvent(ChainEvent evt, int reorgDepth = 12)
    {
        if (evt.BlockNumber + reorgDepth < LastProcessedBlock)
        {
            _logger.LogWarning(
                "Ignoring potential re-org event {Event} at block {Block} (last={Last})",
                evt.EventName, evt.BlockNumber, LastProcessedBlock);
            return;
        }

        if (evt.BlockNumber > LastProcessedBlock)
            Interlocked.Exchange(ref _lastProcessedBlock, evt.BlockNumber);

        if (!_events.Writer.TryWrite(evt))
            _logger.LogError("Failed to enqueue chain event {Event}", evt.EventName);
        else
            _logger.LogDebug("Enqueued {Event} block={Block} account={Account}",
                evt.EventName, evt.BlockNumber, evt.Account);
    }

    /// <summary>
    /// Marks a block range as confirmed (after re-org depth). Idempotent.
    /// </summary>
    public void ConfirmBlock(long blockNumber)
    {
        if (blockNumber > LastProcessedBlock)
            Interlocked.Exchange(ref _lastProcessedBlock, blockNumber);
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _events.Writer.TryComplete();
        _cts.Dispose();
        await Task.CompletedTask;
    }
}
