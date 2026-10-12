namespace Dex.Domain.Risk;

/// <summary>
/// Static risk parameters for a perpetual market.
/// All ratios are decimal fractions (e.g. 0.05m = 5%).
/// </summary>
public sealed class MarketRiskParameters
{
    public int MarketId { get; }
    public decimal InitialMarginRate { get; }       // e.g. 0.05 = 5% → max leverage 20x
    public decimal MaintenanceMarginRate { get; }   // e.g. 0.025 = 2.5%
    public decimal MaxLeverage { get; }             // derived or explicit
    public decimal MaxPositionSize { get; }         // absolute quantity cap per account
    public decimal OpenInterestCap { get; }         // total OI cap for the market
    public decimal LiquidationPenaltyRate { get; }  // penalty taken on liquidation
    public decimal MinNotional { get; }

    public MarketRiskParameters(
        int marketId,
        decimal initialMarginRate,
        decimal maintenanceMarginRate,
        decimal maxPositionSize,
        decimal openInterestCap,
        decimal liquidationPenaltyRate = 0.005m,
        decimal minNotional = 1m)
    {
        if (marketId <= 0) throw new ArgumentOutOfRangeException(nameof(marketId));
        if (initialMarginRate <= 0m || initialMarginRate >= 1m)
            throw new ArgumentOutOfRangeException(nameof(initialMarginRate));
        if (maintenanceMarginRate <= 0m || maintenanceMarginRate >= initialMarginRate)
            throw new ArgumentOutOfRangeException(nameof(maintenanceMarginRate), "MM rate must be positive and strictly less than IM rate.");
        if (maxPositionSize <= 0m) throw new ArgumentOutOfRangeException(nameof(maxPositionSize));
        if (openInterestCap <= 0m) throw new ArgumentOutOfRangeException(nameof(openInterestCap));

        MarketId = marketId;
        InitialMarginRate = initialMarginRate;
        MaintenanceMarginRate = maintenanceMarginRate;
        MaxLeverage = 1m / initialMarginRate;
        MaxPositionSize = maxPositionSize;
        OpenInterestCap = openInterestCap;
        LiquidationPenaltyRate = liquidationPenaltyRate;
        MinNotional = minNotional;
    }
}
