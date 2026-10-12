using Dex.RiskEngine.Math;
using FluentAssertions;
using Xunit;

namespace Dex.RiskEngine.Tests;

public class MarginCalculatorTests
{
    [Fact]
    public void Notional_IsQuantityTimesPrice()
    {
        MarginCalculator.Notional(10m, 2500m).Should().Be(25000m);
    }

    [Fact]
    public void InitialMargin_At5Percent_IsCorrect()
    {
        // 20x leverage → 5% IM
        var im = MarginCalculator.InitialMargin(100_000m, 0.05m);
        im.Should().Be(5_000m);
    }

    [Fact]
    public void MaintenanceMargin_At2_5Percent_IsCorrect()
    {
        var mm = MarginCalculator.MaintenanceMargin(100_000m, 0.025m);
        mm.Should().Be(2_500m);
    }

    [Fact]
    public void MaxQuantityByLeverage_RespectsEquityAndRate()
    {
        // equity 10_000, IM 5%, price 2000 → max notional 200_000 → max qty 100
        var qty = MarginCalculator.MaxQuantityByLeverage(10_000m, 2_000m, 0.05m);
        qty.Should().Be(100m);
    }

    [Fact]
    public void LiquidationPriceLong_WithBuffer_IsBelowEntry()
    {
        // entry 2000, qty 10, allocated 2000, MM 2.5%
        // notional 20_000, MM = 500, buffer = 1500 → liq = 2000 - 150
        var liq = MarginCalculator.LiquidationPriceLong(2000m, 10m, 2000m, 0.025m);
        liq.Should().Be(1850m);
    }

    [Fact]
    public void LiquidationPriceShort_WithBuffer_IsAboveEntry()
    {
        var liq = MarginCalculator.LiquidationPriceShort(2000m, 10m, 2000m, 0.025m);
        liq.Should().Be(2150m);
    }

    [Fact]
    public void MarginRatio_HealthyPosition()
    {
        // equity 5000, notional 100_000 → 5%
        var ratio = MarginCalculator.MarginRatio(5_000m, 100_000m);
        ratio.Should().Be(0.05m);
    }

    [Fact]
    public void MarginRatio_ZeroNotional_ReturnsMaxValue()
    {
        MarginCalculator.MarginRatio(1000m, 0m).Should().Be(decimal.MaxValue);
    }
}
