using System.Runtime.CompilerServices;
using Dex.Domain.Risk;

namespace Dex.RiskEngine.Math;

/// <summary>
/// Pure functions for Initial / Maintenance Margin calculations.
/// All inputs and outputs are System.Decimal. No floating-point.
/// </summary>
public static class MarginCalculator
{
    /// <summary>
    /// Notional value of a position or order.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static decimal Notional(decimal quantity, decimal price)
        => quantity * price;

    /// <summary>
    /// Initial Margin required for a given notional at the market IM rate.
    /// IM = Notional * InitialMarginRate
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static decimal InitialMargin(decimal notional, decimal initialMarginRate)
    {
        if (notional < 0m) throw new ArgumentOutOfRangeException(nameof(notional));
        if (initialMarginRate <= 0m || initialMarginRate >= 1m)
            throw new ArgumentOutOfRangeException(nameof(initialMarginRate));
        return notional * initialMarginRate;
    }

    /// <summary>
    /// Maintenance Margin required.
    /// MM = Notional * MaintenanceMarginRate
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static decimal MaintenanceMargin(decimal notional, decimal maintenanceMarginRate)
    {
        if (notional < 0m) throw new ArgumentOutOfRangeException(nameof(notional));
        if (maintenanceMarginRate <= 0m)
            throw new ArgumentOutOfRangeException(nameof(maintenanceMarginRate));
        return notional * maintenanceMarginRate;
    }

    /// <summary>
    /// Maximum position size allowed by leverage / IM rate for a given equity.
    /// maxQty = (equity / IMRate) / price
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static decimal MaxQuantityByLeverage(decimal equity, decimal price, decimal initialMarginRate)
    {
        if (equity <= 0m || price <= 0m || initialMarginRate <= 0m) return 0m;
        var maxNotional = equity / initialMarginRate;
        return maxNotional / price;
    }

    /// <summary>
    /// Liquidation price for a long position (Isolated or approximate Cross).
    /// For Long: liqPrice = entry * (1 - (marginRatio - MMRate) / (1 - MMRate)) roughly,
    /// simplified conservative form used by many perps:
    /// Long Liq ≈ Entry * (1 - 1/Leverage + MMRate)
    /// More precise with allocated margin:
    /// Long: liqPrice = entry - (allocatedMargin - MM) / qty
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static decimal LiquidationPriceLong(
        decimal entryPrice,
        decimal quantity,
        decimal allocatedMargin,
        decimal maintenanceMarginRate)
    {
        if (quantity <= 0m || entryPrice <= 0m) return 0m;
        var mm = MaintenanceMargin(Notional(quantity, entryPrice), maintenanceMarginRate);
        var buffer = allocatedMargin - mm;
        if (buffer <= 0m) return entryPrice; // already under-margined
        return entryPrice - (buffer / quantity);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static decimal LiquidationPriceShort(
        decimal entryPrice,
        decimal quantity,
        decimal allocatedMargin,
        decimal maintenanceMarginRate)
    {
        if (quantity <= 0m || entryPrice <= 0m) return 0m;
        var mm = MaintenanceMargin(Notional(quantity, entryPrice), maintenanceMarginRate);
        var buffer = allocatedMargin - mm;
        if (buffer <= 0m) return entryPrice;
        return entryPrice + (buffer / quantity);
    }

    /// <summary>
    /// Margin ratio = Equity / Notional (or PositionMargin / Notional for Isolated).
    /// Values below MaintenanceMarginRate trigger liquidation.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static decimal MarginRatio(decimal equityOrAllocatedMargin, decimal notional)
    {
        if (notional <= 0m) return decimal.MaxValue;
        return equityOrAllocatedMargin / notional;
    }
}
