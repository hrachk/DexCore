using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Dex.BlazorUI.Models;
using Microsoft.AspNetCore.SignalR.Client;

namespace Dex.BlazorUI.Services;

/// <summary>
/// SignalR market-data client with automatic reconnect and offline mock feed.
/// UI updates are throttled via <see cref="UiRefreshRequested"/> to avoid layout thrashing.
/// </summary>
public sealed class MarketDataStreamService : IAsyncDisposable
{
    private readonly IConfiguration _config;
    private readonly ILogger<MarketDataStreamService> _logger;
    private HubConnection? _hub;
    private CancellationTokenSource? _mockCts;
    private Timer? _throttleTimer;
    private int _pendingUi;
    private bool _useMock;

    public OrderBookState Book { get; } = new();
    public List<TradeTick> Trades { get; } = new(64);
    public TickerState? Ticker { get; private set; }
    public MarkPriceState? Mark { get; private set; }
    public bool IsConnected { get; private set; }
    public string StatusMessage { get; private set; } = "Disconnected";

    /// <summary>Raised at most every ~100ms when market data changes.</summary>
    public event Action? UiRefreshRequested;

    public MarketDataStreamService(IConfiguration config, ILogger<MarketDataStreamService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task StartAsync(int marketId, string symbol, decimal seedMid = 95000m)
    {
        await StopAsync();
        Book.MarketId = marketId;
        Book.Symbol = symbol;

        var hubUrl = _config["SignalR:HubUrl"] ?? "http://localhost:5000/hubs/marketdata";

        _hub = new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .WithAutomaticReconnect([TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)])
            .Build();

        _hub.On<JsonElement>("OrderBookSnapshot", OnSnapshot);
        _hub.On<JsonElement>("OrderBookDelta", OnDelta);
        _hub.On<JsonElement>("Trade", OnTrade);
        _hub.On<JsonElement>("Ticker", OnTicker);
        _hub.On<JsonElement>("MarkPrice", OnMark);
        _hub.On<string>("Error", msg =>
        {
            StatusMessage = msg;
            RequestUi();
        });

        _hub.Reconnected += async _ =>
        {
            IsConnected = true;
            StatusMessage = "Live";
            _useMock = false;
            StopMock();
            try { await _hub.InvokeAsync("SubscribeToMarketData", new[] { marketId }, true, true, true, 15); }
            catch { /* ignore */ }
            RequestUi();
        };
        _hub.Closed += _ =>
        {
            IsConnected = false;
            StatusMessage = "Disconnected";
            StartMock(marketId, seedMid);
            RequestUi();
            return Task.CompletedTask;
        };

        try
        {
            await _hub.StartAsync();
            await _hub.InvokeAsync("SubscribeToMarketData", new[] { marketId }, true, true, true, 15);
            IsConnected = true;
            StatusMessage = "Live";
            _useMock = false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SignalR unavailable — starting mock feed");
            StatusMessage = "Simulated feed (API offline)";
            IsConnected = false;
            StartMock(marketId, seedMid);
        }

        _throttleTimer = new Timer(_ =>
        {
            if (Interlocked.Exchange(ref _pendingUi, 0) == 1)
                UiRefreshRequested?.Invoke();
        }, null, 100, 100);

        RequestUi();
    }

    public async Task StopAsync()
    {
        StopMock();
        if (_throttleTimer is not null)
        {
            await _throttleTimer.DisposeAsync();
            _throttleTimer = null;
        }
        if (_hub is not null)
        {
            try
            {
                await _hub.InvokeAsync("Unsubscribe");
                await _hub.StopAsync();
                await _hub.DisposeAsync();
            }
            catch { /* ignore */ }
            _hub = null;
        }
    }

    private void OnSnapshot(JsonElement el)
    {
        _useMock = false;
        StopMock();
        Book.Bids = ParseLevels(el, "bids", true);
        Book.Asks = ParseLevels(el, "asks", false);
        if (el.TryGetProperty("sequence", out var seq))
            Book.Sequence = seq.GetInt64();
        IsConnected = true;
        StatusMessage = "Live";
        RequestUi();
    }

    private void OnDelta(JsonElement el)
    {
        if (!el.TryGetProperty("updates", out var updates)) return;
        var bidMap = Book.Bids.ToDictionary(x => x.Price, x => x.Quantity);
        var askMap = Book.Asks.ToDictionary(x => x.Price, x => x.Quantity);

        foreach (var u in updates.EnumerateArray())
        {
            var isBid = u.TryGetProperty("isBid", out var b) && b.GetBoolean();
            var price = GetDecimal(u, "price");
            var qty = GetDecimal(u, "quantity");
            var map = isBid ? bidMap : askMap;
            if (qty <= 0) map.Remove(price);
            else map[price] = qty;
        }

        Book.Bids = ToLevels(bidMap, descending: true);
        Book.Asks = ToLevels(askMap, descending: false);
        RequestUi();
    }

    private void OnTrade(JsonElement el)
    {
        var tick = new TradeTick
        {
            MarketId = el.TryGetProperty("marketId", out var m) ? m.GetInt32() : Book.MarketId,
            TradeId = el.TryGetProperty("tradeId", out var t) ? t.GetInt64() : DateTime.UtcNow.Ticks,
            Price = GetDecimal(el, "price"),
            Quantity = GetDecimal(el, "quantity"),
            IsBuyerMaker = el.TryGetProperty("isBuyerMaker", out var ibm) && ibm.GetBoolean(),
            TimestampUtcTicks = el.TryGetProperty("timestampUtcTicks", out var ts) ? ts.GetInt64() : DateTime.UtcNow.Ticks
        };
        lock (Trades)
        {
            Trades.Insert(0, tick);
            if (Trades.Count > 50) Trades.RemoveRange(50, Trades.Count - 50);
        }
        RequestUi();
    }

    private void OnTicker(JsonElement el)
    {
        Ticker = new TickerState
        {
            MarketId = el.TryGetProperty("marketId", out var m) ? m.GetInt32() : 0,
            Symbol = el.TryGetProperty("symbol", out var s) ? s.GetString() ?? "" : "",
            LastPrice = GetDecimal(el, "lastPrice"),
            BestBid = GetDecimal(el, "bestBid"),
            BestAsk = GetDecimal(el, "bestAsk"),
            Volume24h = GetDecimal(el, "volume24h"),
            PriceChange24h = GetDecimal(el, "priceChange24h")
        };
        RequestUi();
    }

    private void OnMark(JsonElement el)
    {
        Mark = new MarkPriceState
        {
            MarketId = el.TryGetProperty("marketId", out var m) ? m.GetInt32() : 0,
            MarkPrice = GetDecimal(el, "markPrice"),
            IndexPrice = GetDecimal(el, "indexPrice"),
            FundingRate = GetDecimal(el, "fundingRate"),
            NextFundingTimeUtcTicks = el.TryGetProperty("nextFundingTimeUtcTicks", out var n) ? n.GetInt64() : 0
        };
        RequestUi();
    }

    private void StartMock(int marketId, decimal mid)
    {
        if (_useMock) return;
        _useMock = true;
        SeedBook(mid);
        SeedTrades(mid);
        Ticker = new TickerState
        {
            MarketId = marketId,
            Symbol = Book.Symbol,
            LastPrice = mid,
            BestBid = Book.Bids.FirstOrDefault()?.Price ?? mid,
            BestAsk = Book.Asks.FirstOrDefault()?.Price ?? mid,
            Volume24h = 12500m,
            PriceChange24h = 2.34m
        };
        Mark = new MarkPriceState
        {
            MarketId = marketId,
            MarkPrice = mid,
            IndexPrice = mid * 1.0001m,
            FundingRate = 0.0001m,
            NextFundingTimeUtcTicks = DateTime.UtcNow.AddHours(1).Ticks
        };

        _mockCts = new CancellationTokenSource();
        _ = RunMockLoopAsync(mid, _mockCts.Token);
    }

    private void StopMock()
    {
        _mockCts?.Cancel();
        _mockCts?.Dispose();
        _mockCts = null;
        _useMock = false;
    }

    private async Task RunMockLoopAsync(decimal mid, CancellationToken ct)
    {
        var rng = new Random(42);
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(1200, ct); }
            catch (OperationCanceledException) { break; }

            foreach (var l in Book.Bids)
                l.Quantity = Math.Max(0.01m, l.Quantity + (decimal)(rng.NextDouble() - 0.5) * 0.3m);
            foreach (var l in Book.Asks)
                l.Quantity = Math.Max(0.01m, l.Quantity + (decimal)(rng.NextDouble() - 0.5) * 0.3m);
            RecalcTotals(Book.Bids);
            RecalcTotals(Book.Asks);

            var price = mid + (decimal)(rng.NextDouble() - 0.5) * 30m;
            var tick = new TradeTick
            {
                MarketId = Book.MarketId,
                TradeId = DateTime.UtcNow.Ticks,
                Price = price,
                Quantity = 0.01m + (decimal)rng.NextDouble() * 0.4m,
                IsBuyerMaker = rng.NextDouble() > 0.5,
                TimestampUtcTicks = DateTime.UtcNow.Ticks
            };
            lock (Trades)
            {
                Trades.Insert(0, tick);
                if (Trades.Count > 50) Trades.RemoveRange(50, Trades.Count - 50);
            }

            if (Ticker is not null)
            {
                Ticker.LastPrice = mid + (decimal)(rng.NextDouble() - 0.5) * 20m;
                Ticker.BestBid = Book.Bids.FirstOrDefault()?.Price ?? mid;
                Ticker.BestAsk = Book.Asks.FirstOrDefault()?.Price ?? mid;
            }
            RequestUi();
        }
    }

    private void SeedBook(decimal mid)
    {
        Book.Bids.Clear();
        Book.Asks.Clear();
        decimal bidT = 0, askT = 0;
        var rng = new Random(7);
        for (int i = 0; i < 15; i++)
        {
            var bq = 0.05m + (decimal)rng.NextDouble() * 2.5m;
            var aq = 0.05m + (decimal)rng.NextDouble() * 2.5m;
            bidT += bq; askT += aq;
            Book.Bids.Add(new PriceLevel { Price = mid - (i + 1) * (0.5m + (decimal)rng.NextDouble()), Quantity = bq, Total = bidT });
            Book.Asks.Add(new PriceLevel { Price = mid + (i + 1) * (0.5m + (decimal)rng.NextDouble()), Quantity = aq, Total = askT });
        }
    }

    private void SeedTrades(decimal mid)
    {
        Trades.Clear();
        var rng = new Random(11);
        for (int i = 0; i < 30; i++)
        {
            Trades.Add(new TradeTick
            {
                MarketId = Book.MarketId,
                TradeId = 100000 + i,
                Price = mid + (decimal)(rng.NextDouble() - 0.5) * 20m,
                Quantity = 0.001m + (decimal)rng.NextDouble() * 0.5m,
                IsBuyerMaker = rng.NextDouble() > 0.5,
                TimestampUtcTicks = DateTime.UtcNow.AddSeconds(-i * 3).Ticks
            });
        }
    }

    private static List<PriceLevel> ParseLevels(JsonElement root, string name, bool descending)
    {
        var list = new List<PriceLevel>();
        if (!root.TryGetProperty(name, out var arr)) return list;
        decimal total = 0;
        foreach (var x in arr.EnumerateArray())
        {
            var p = GetDecimal(x, "price");
            var q = GetDecimal(x, "quantity");
            total += q;
            list.Add(new PriceLevel { Price = p, Quantity = q, Total = total });
        }
        list.Sort((a, b) => descending ? b.Price.CompareTo(a.Price) : a.Price.CompareTo(b.Price));
        RecalcTotals(list);
        return list;
    }

    private static List<PriceLevel> ToLevels(Dictionary<decimal, decimal> map, bool descending)
    {
        var list = map.Select(kv => new PriceLevel { Price = kv.Key, Quantity = kv.Value }).ToList();
        list.Sort((a, b) => descending ? b.Price.CompareTo(a.Price) : a.Price.CompareTo(b.Price));
        RecalcTotals(list);
        return list;
    }

    private static void RecalcTotals(List<PriceLevel> levels)
    {
        decimal t = 0;
        foreach (var l in levels)
        {
            t += l.Quantity;
            l.Total = t;
        }
    }

    private static decimal GetDecimal(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p)) return 0m;
        if (p.ValueKind == JsonValueKind.Number) return p.GetDecimal();
        if (p.ValueKind == JsonValueKind.String &&
            decimal.TryParse(p.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
            return d;
        return 0m;
    }

    private void RequestUi() => Interlocked.Exchange(ref _pendingUi, 1);

    public async ValueTask DisposeAsync() => await StopAsync();
}
