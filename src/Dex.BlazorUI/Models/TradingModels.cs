namespace Dex.BlazorUI.Models;

public enum OrderSide : byte { Buy = 0, Sell = 1 }
public enum OrderType : byte { Limit = 0, Market = 1, StopLimit = 2, StopMarket = 3 }
public enum TimeInForce : byte { Gtc = 0, Ioc = 1, Fok = 2 }
public enum MarginMode : byte { Cross = 0, Isolated = 1 }
public enum OrderStatus : byte
{
    PendingNew = 0, New = 1, PartiallyFilled = 2, Filled = 3,
    PendingCancel = 4, Cancelled = 5, Rejected = 6
}

public sealed class PriceLevel
{
    public decimal Price { get; set; }
    public decimal Quantity { get; set; }
    public decimal Total { get; set; }
}

public sealed class OrderBookState
{
    public int MarketId { get; set; }
    public string Symbol { get; set; } = "";
    public long Sequence { get; set; }
    public List<PriceLevel> Bids { get; set; } = new();
    public List<PriceLevel> Asks { get; set; } = new();
    public decimal Spread => Bids.Count > 0 && Asks.Count > 0 ? Asks[0].Price - Bids[0].Price : 0m;
    public decimal Mid => Bids.Count > 0 && Asks.Count > 0 ? (Asks[0].Price + Bids[0].Price) / 2m : 0m;
}

public sealed class TradeTick
{
    public int MarketId { get; set; }
    public long TradeId { get; set; }
    public decimal Price { get; set; }
    public decimal Quantity { get; set; }
    public bool IsBuyerMaker { get; set; }
    public long TimestampUtcTicks { get; set; }
}

public sealed class TickerState
{
    public int MarketId { get; set; }
    public string Symbol { get; set; } = "";
    public decimal LastPrice { get; set; }
    public decimal BestBid { get; set; }
    public decimal BestAsk { get; set; }
    public decimal Volume24h { get; set; }
    public decimal QuoteVolume24h { get; set; }
    public decimal High24h { get; set; }
    public decimal Low24h { get; set; }
    public decimal PriceChange24h { get; set; }
    public decimal PriceChangePercent24h { get; set; }
}

public sealed class MarkPriceState
{
    public int MarketId { get; set; }
    public decimal MarkPrice { get; set; }
    public decimal IndexPrice { get; set; }
    public decimal FundingRate { get; set; }
    public long NextFundingTimeUtcTicks { get; set; }
}

public sealed class Candle
{
    public long Time { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public decimal Volume { get; set; }
}

public sealed class OpenOrder
{
    public long OrderId { get; set; }
    public long ClientOrderId { get; set; }
    public int MarketId { get; set; }
    public string Symbol { get; set; } = "";
    public OrderSide Side { get; set; }
    public OrderType Type { get; set; }
    public decimal Price { get; set; }
    public decimal StopPrice { get; set; }
    public decimal Quantity { get; set; }
    public decimal FilledQuantity { get; set; }
    public decimal RemainingQuantity { get; set; }
    public OrderStatus Status { get; set; }
    public long CreatedAtTicks { get; set; }
    public decimal? TakeProfit { get; set; }
    public decimal? StopLoss { get; set; }
}

public sealed class PositionRow
{
    public int MarketId { get; set; }
    public string Symbol { get; set; } = "";
    public string Side { get; set; } = "Flat";
    public decimal Quantity { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal MarkPrice { get; set; }
    public decimal UnrealizedPnl { get; set; }
    public decimal RealizedPnl { get; set; }
    public int Leverage { get; set; }
    public MarginMode MarginMode { get; set; }
    public decimal LiquidationPrice { get; set; }
    public decimal Margin { get; set; }
    public decimal? TakeProfit { get; set; }
    public decimal? StopLoss { get; set; }
}

public sealed class TradeHistoryRow
{
    public long TradeId { get; set; }
    public string Symbol { get; set; } = "";
    public OrderSide Side { get; set; }
    public decimal Price { get; set; }
    public decimal Quantity { get; set; }
    public decimal Fee { get; set; }
    public long TimestampTicks { get; set; }
    public string? Note { get; set; }
}

public sealed class MarketInfo
{
    public int MarketId { get; init; }
    public string Symbol { get; init; } = "";
    public string BinanceSymbol { get; init; } = "";
    public string Base { get; init; } = "";
    public string Quote { get; init; } = "";
    public decimal TickSize { get; init; }
    public decimal LotSize { get; init; }
    public int MaxLeverage { get; init; }
    public int PriceDecimals { get; init; } = 2;
    public int QtyDecimals { get; init; } = 4;
}

public static class MarketCatalog
{
    public static readonly MarketInfo[] All =
    [
        new() { MarketId = 1,  Symbol = "BTC-USDT",  BinanceSymbol = "BTCUSDT",  Base = "BTC",  Quote = "USDT", TickSize = 0.1m,    LotSize = 0.0001m, MaxLeverage = 50, PriceDecimals = 2, QtyDecimals = 4 },
        new() { MarketId = 2,  Symbol = "ETH-USDT",  BinanceSymbol = "ETHUSDT",  Base = "ETH",  Quote = "USDT", TickSize = 0.01m,   LotSize = 0.001m,  MaxLeverage = 50, PriceDecimals = 2, QtyDecimals = 3 },
        new() { MarketId = 3,  Symbol = "SOL-USDT",  BinanceSymbol = "SOLUSDT",  Base = "SOL",  Quote = "USDT", TickSize = 0.01m,   LotSize = 0.01m,   MaxLeverage = 25, PriceDecimals = 2, QtyDecimals = 2 },
        new() { MarketId = 4,  Symbol = "BNB-USDT",  BinanceSymbol = "BNBUSDT",  Base = "BNB",  Quote = "USDT", TickSize = 0.01m,   LotSize = 0.001m,  MaxLeverage = 25, PriceDecimals = 2, QtyDecimals = 3 },
        new() { MarketId = 5,  Symbol = "XRP-USDT",  BinanceSymbol = "XRPUSDT",  Base = "XRP",  Quote = "USDT", TickSize = 0.0001m, LotSize = 0.1m,    MaxLeverage = 20, PriceDecimals = 4, QtyDecimals = 1 },
        new() { MarketId = 6,  Symbol = "DOGE-USDT", BinanceSymbol = "DOGEUSDT", Base = "DOGE", Quote = "USDT", TickSize = 0.00001m,LotSize = 1m,      MaxLeverage = 20, PriceDecimals = 5, QtyDecimals = 0 },
        new() { MarketId = 7,  Symbol = "ADA-USDT",  BinanceSymbol = "ADAUSDT",  Base = "ADA",  Quote = "USDT", TickSize = 0.0001m, LotSize = 0.1m,    MaxLeverage = 20, PriceDecimals = 4, QtyDecimals = 1 },
        new() { MarketId = 8,  Symbol = "AVAX-USDT", BinanceSymbol = "AVAXUSDT", Base = "AVAX", Quote = "USDT", TickSize = 0.01m,   LotSize = 0.01m,   MaxLeverage = 25, PriceDecimals = 2, QtyDecimals = 2 },
        new() { MarketId = 9,  Symbol = "LINK-USDT", BinanceSymbol = "LINKUSDT", Base = "LINK", Quote = "USDT", TickSize = 0.001m,  LotSize = 0.01m,   MaxLeverage = 20, PriceDecimals = 3, QtyDecimals = 2 },
        new() { MarketId = 10, Symbol = "DOT-USDT",  BinanceSymbol = "DOTUSDT",  Base = "DOT",  Quote = "USDT", TickSize = 0.001m,  LotSize = 0.01m,   MaxLeverage = 20, PriceDecimals = 3, QtyDecimals = 2 },
        new() { MarketId = 11, Symbol = "MATIC-USDT",BinanceSymbol = "MATICUSDT",Base = "MATIC",Quote = "USDT", TickSize = 0.0001m, LotSize = 0.1m,    MaxLeverage = 20, PriceDecimals = 4, QtyDecimals = 1 },
        new() { MarketId = 12, Symbol = "LTC-USDT",  BinanceSymbol = "LTCUSDT",  Base = "LTC",  Quote = "USDT", TickSize = 0.01m,   LotSize = 0.001m,  MaxLeverage = 20, PriceDecimals = 2, QtyDecimals = 3 },
        new() { MarketId = 13, Symbol = "ATOM-USDT", BinanceSymbol = "ATOMUSDT", Base = "ATOM", Quote = "USDT", TickSize = 0.001m,  LotSize = 0.01m,   MaxLeverage = 20, PriceDecimals = 3, QtyDecimals = 2 },
        new() { MarketId = 14, Symbol = "UNI-USDT",  BinanceSymbol = "UNIUSDT",  Base = "UNI",  Quote = "USDT", TickSize = 0.001m,  LotSize = 0.01m,   MaxLeverage = 20, PriceDecimals = 3, QtyDecimals = 2 },
        new() { MarketId = 15, Symbol = "APT-USDT",  BinanceSymbol = "APTUSDT",  Base = "APT",  Quote = "USDT", TickSize = 0.001m,  LotSize = 0.01m,   MaxLeverage = 20, PriceDecimals = 3, QtyDecimals = 2 },
        new() { MarketId = 16, Symbol = "ARB-USDT",  BinanceSymbol = "ARBUSDT",  Base = "ARB",  Quote = "USDT", TickSize = 0.0001m, LotSize = 0.1m,    MaxLeverage = 20, PriceDecimals = 4, QtyDecimals = 1 },
        new() { MarketId = 17, Symbol = "OP-USDT",   BinanceSymbol = "OPUSDT",   Base = "OP",   Quote = "USDT", TickSize = 0.0001m, LotSize = 0.1m,    MaxLeverage = 20, PriceDecimals = 4, QtyDecimals = 1 },
        new() { MarketId = 18, Symbol = "SUI-USDT",  BinanceSymbol = "SUIUSDT",  Base = "SUI",  Quote = "USDT", TickSize = 0.0001m, LotSize = 0.1m,    MaxLeverage = 20, PriceDecimals = 4, QtyDecimals = 1 },
        new() { MarketId = 19, Symbol = "NEAR-USDT", BinanceSymbol = "NEARUSDT", Base = "NEAR", Quote = "USDT", TickSize = 0.001m,  LotSize = 0.01m,   MaxLeverage = 20, PriceDecimals = 3, QtyDecimals = 2 },
        new() { MarketId = 20, Symbol = "FIL-USDT",  BinanceSymbol = "FILUSDT",  Base = "FIL",  Quote = "USDT", TickSize = 0.001m,  LotSize = 0.01m,   MaxLeverage = 15, PriceDecimals = 3, QtyDecimals = 2 },
        new() { MarketId = 21, Symbol = "AAVE-USDT", BinanceSymbol = "AAVEUSDT", Base = "AAVE", Quote = "USDT", TickSize = 0.01m,   LotSize = 0.001m,  MaxLeverage = 15, PriceDecimals = 2, QtyDecimals = 3 },
        new() { MarketId = 22, Symbol = "PEPE-USDT", BinanceSymbol = "PEPEUSDT", Base = "PEPE", Quote = "USDT", TickSize = 0.00000001m, LotSize = 1000m, MaxLeverage = 10, PriceDecimals = 8, QtyDecimals = 0 },
        new() { MarketId = 23, Symbol = "WIF-USDT",  BinanceSymbol = "WIFUSDT",  Base = "WIF",  Quote = "USDT", TickSize = 0.0001m, LotSize = 0.1m,    MaxLeverage = 10, PriceDecimals = 4, QtyDecimals = 1 },
        new() { MarketId = 24, Symbol = "INJ-USDT",  BinanceSymbol = "INJUSDT",  Base = "INJ",  Quote = "USDT", TickSize = 0.001m,  LotSize = 0.01m,   MaxLeverage = 20, PriceDecimals = 3, QtyDecimals = 2 },
    ];

    public static MarketInfo Get(string symbol)
    {
        var s = symbol.Replace("/", "-").Replace("_", "-").ToUpperInvariant();
        if (!s.Contains('-') && s.EndsWith("USDT"))
            s = s[..^4] + "-USDT";
        return All.FirstOrDefault(m =>
                   m.Symbol.Equals(s, StringComparison.OrdinalIgnoreCase)
                || m.BinanceSymbol.Equals(symbol, StringComparison.OrdinalIgnoreCase)
                || m.Symbol.Replace("-", "").Equals(s.Replace("-", ""), StringComparison.OrdinalIgnoreCase))
               ?? All[0];
    }

    public static MarketInfo? ById(int id) => All.FirstOrDefault(m => m.MarketId == id);

    /// <summary>Alias for Get — used by Trade.razor route binding.</summary>
    public static MarketInfo Find(string? symbol) => Get(string.IsNullOrWhiteSpace(symbol) ? "BTC-USDT" : symbol);
}

public sealed class OrderConfirmRequest
{
    public OrderSide Side { get; set; }
    public OrderType Type { get; set; }
    public decimal Price { get; set; }
    public decimal Qty { get; set; }
    public int Leverage { get; set; }
    public MarginMode MarginMode { get; set; }
    public bool ReduceOnly { get; set; }
    public decimal Margin { get; set; }
    public decimal Liq { get; set; }
    public decimal Fee { get; set; }
    public decimal? Tp { get; set; }
    public decimal? Sl { get; set; }
}
