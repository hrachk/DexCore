namespace Dex.SettlementService.Models;

public sealed class SettlementTrade
{
    public long TradeId { get; init; }
    public string MakerAddress { get; init; } = string.Empty;
    public string TakerAddress { get; init; } = string.Empty;
    public int MarketId { get; init; }
    public decimal Quantity { get; init; }   // signed: + = taker long
    public decimal Price { get; init; }
}

public sealed class SettlementBatch
{
    public string BatchId { get; init; } = string.Empty;
    public IReadOnlyList<SettlementTrade> Trades { get; init; } = Array.Empty<SettlementTrade>();
    public DateTime CreatedAtUtc { get; init; }
}

public enum SettlementStatus
{
    Pending = 0,
    Submitted = 1,
    Confirmed = 2,
    Failed = 3,
    Replaced = 4
}

public sealed class SettlementReceipt
{
    public string BatchId { get; init; } = string.Empty;
    public string? TransactionHash { get; set; }
    public SettlementStatus Status { get; set; }
    public long? Nonce { get; set; }
    public string? Error { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class ChainEvent
{
    public string EventName { get; init; } = string.Empty;
    public string TransactionHash { get; init; } = string.Empty;
    public long BlockNumber { get; init; }
    public string? Account { get; init; }
    public decimal? Amount { get; init; }
    public int? MarketId { get; init; }
    public DateTime TimestampUtc { get; init; }
}
