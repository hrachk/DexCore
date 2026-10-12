using Dex.Domain.Risk;

namespace Dex.RiskEngine.Models;

public readonly struct LiquidationCandidate
{
    public long AccountId { get; }
    public int MarketId { get; }
    public MarginMode MarginMode { get; }
    public PositionSide Side { get; }
    public decimal Quantity { get; }
    public decimal EntryPrice { get; }
    public decimal MarkPrice { get; }
    public decimal UnrealizedPnl { get; }
    public decimal MarginRatio { get; }
    public decimal MaintenanceMargin { get; }
    public bool IsPartial { get; }
    public decimal LiquidationQuantity { get; }

    public LiquidationCandidate(
        long accountId,
        int marketId,
        MarginMode marginMode,
        PositionSide side,
        decimal quantity,
        decimal entryPrice,
        decimal markPrice,
        decimal unrealizedPnl,
        decimal marginRatio,
        decimal maintenanceMargin,
        bool isPartial,
        decimal liquidationQuantity)
    {
        AccountId = accountId;
        MarketId = marketId;
        MarginMode = marginMode;
        Side = side;
        Quantity = quantity;
        EntryPrice = entryPrice;
        MarkPrice = markPrice;
        UnrealizedPnl = unrealizedPnl;
        MarginRatio = marginRatio;
        MaintenanceMargin = maintenanceMargin;
        IsPartial = isPartial;
        LiquidationQuantity = liquidationQuantity;
    }
}
