namespace Dex.ApiGateway.Models;

public readonly struct PriceLevelDto
{
    public decimal Price { get; }
    public decimal Quantity { get; }

    public PriceLevelDto(decimal price, decimal quantity)
    {
        Price = price;
        Quantity = quantity;
    }
}

public sealed class OrderBookSnapshotDto
{
    public int MarketId { get; init; }
    public string Symbol { get; init; } = string.Empty;
    public long Sequence { get; init; }
    public long TimestampUtcTicks { get; init; }
    public IReadOnlyList<PriceLevelDto> Bids { get; init; } = Array.Empty<PriceLevelDto>();
    public IReadOnlyList<PriceLevelDto> Asks { get; init; } = Array.Empty<PriceLevelDto>();
}

public sealed class OrderBookDeltaDto
{
    public int MarketId { get; init; }
    public long Sequence { get; init; }
    public long TimestampUtcTicks { get; init; }
    public IReadOnlyList<PriceLevelUpdateDto> Updates { get; init; } = Array.Empty<PriceLevelUpdateDto>();
}

public readonly struct PriceLevelUpdateDto
{
    public bool IsBid { get; }
    public decimal Price { get; }
    public decimal Quantity { get; } // 0 = remove

    public PriceLevelUpdateDto(bool isBid, decimal price, decimal quantity)
    {
        IsBid = isBid;
        Price = price;
        Quantity = quantity;
    }
}

public sealed class TradeDto
{
    public int MarketId { get; init; }
    public long TradeId { get; init; }
    public decimal Price { get; init; }
    public decimal Quantity { get; init; }
    public bool IsBuyerMaker { get; init; }
    public long TimestampUtcTicks { get; init; }
}

public sealed class TickerDto
{
    public int MarketId { get; init; }
    public string Symbol { get; init; } = string.Empty;
    public decimal LastPrice { get; init; }
    public decimal BestBid { get; init; }
    public decimal BestAsk { get; init; }
    public decimal Volume24h { get; init; }
    public decimal PriceChange24h { get; init; }
    public long TimestampUtcTicks { get; init; }
}

public sealed class MarkPriceDto
{
    public int MarketId { get; init; }
    public decimal MarkPrice { get; init; }
    public decimal IndexPrice { get; init; }
    public decimal FundingRate { get; init; }
    public long NextFundingTimeUtcTicks { get; init; }
    public long TimestampUtcTicks { get; init; }
}
