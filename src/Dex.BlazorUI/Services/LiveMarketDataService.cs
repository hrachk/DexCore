using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Dex.BlazorUI.Models;

namespace Dex.BlazorUI.Services;

/// <summary>
/// Live market data from Binance public REST API (spot as price reference for perps UI).
/// Polls ticker, depth, trades, klines. Falls back to last-good cache on transient errors.
/// </summary>
public sealed class LiveMarketDataService : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
    private readonly HttpClient _http;
    private readonly ILogger<LiveMarketDataService> _logger;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private MarketInfo _market = MarketCatalog.All[0];
    private int _pollGen;

    public OrderBookState Book { get; } = new();
    public List<TradeTick> Trades { get; } = new(64);
    public List<Candle> Candles { get; } = new(300);
    public TickerState? Ticker { get; private set; }
    public MarkPriceState? Mark { get; private set; }
    public Dictionary<string, decimal> AllLastPrices { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, decimal> AllChangePct { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, TickerState> AllTickers { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool IsConnected { get; private set; }
    public string StatusMessage { get; private set; } = "Connecting…";
    public string DataSource { get; private set; } = "—";

    public event Action? UiRefreshRequested;

    public LiveMarketDataService(HttpClient http, ILogger<LiveMarketDataService> logger)
    {
        _http = http;
        _logger = logger;
        _http.Timeout = TimeSpan.FromSeconds(12);
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "DexCore-Terminal/1.0");
    }

    public async Task StartAsync(MarketInfo market)
    {
        _market = market;
        Book.MarketId = market.MarketId;
        Book.Symbol = market.Symbol;
        var gen = Interlocked.Increment(ref _pollGen);
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        StatusMessage = "Connecting to market data…";
        Notify();

        // Immediate first load
        await RefreshAllAsync(ct);
        if (gen != _pollGen) return;

        _loop = Task.Run(async () =>
        {
            var n = 0;
            while (!ct.IsCancellationRequested && gen == _pollGen)
            {
                try
                {
                    await Task.Delay(n == 0 ? 800 : 1500, ct);
                    await RefreshDepthAndTradesAsync(ct);
                    if (n % 3 == 0) await RefreshTickerAsync(ct);
                    if (n % 10 == 0) await RefreshKlinesAsync(ct);
                    if (n % 8 == 0) await RefreshAllTickersAsync(ct);
                    n++;
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "poll error");
                    StatusMessage = "Reconnecting…";
                    Notify();
                }
            }
        }, ct);
    }

    public Task StopAsync()
    {
        _cts?.Cancel();
        return Task.CompletedTask;
    }

    private async Task RefreshAllAsync(CancellationToken ct)
    {
        await Task.WhenAll(
            RefreshTickerAsync(ct),
            RefreshDepthAndTradesAsync(ct),
            RefreshKlinesAsync(ct),
            RefreshAllTickersAsync(ct));
    }

    private async Task RefreshTickerAsync(CancellationToken ct)
    {
        var sym = _market.BinanceSymbol;
        try
        {
            var url = $"https://api.binance.com/api/v3/ticker/24hr?symbol={sym}";
            var json = await _http.GetFromJsonAsync<JsonElement>(url, JsonOpts, ct);
            var last = Dec(json, "lastPrice");
            var bid = Dec(json, "bidPrice");
            var ask = Dec(json, "askPrice");
            Ticker = new TickerState
            {
                MarketId = _market.MarketId,
                Symbol = _market.Symbol,
                LastPrice = last,
                BestBid = bid,
                BestAsk = ask,
                Volume24h = Dec(json, "volume"),
                QuoteVolume24h = Dec(json, "quoteVolume"),
                High24h = Dec(json, "highPrice"),
                Low24h = Dec(json, "lowPrice"),
                PriceChange24h = Dec(json, "priceChange"),
                PriceChangePercent24h = Dec(json, "priceChangePercent")
            };
            Mark = new MarkPriceState
            {
                MarketId = _market.MarketId,
                MarkPrice = last,
                IndexPrice = last,
                FundingRate = 0.0001m,
                NextFundingTimeUtcTicks = DateTime.UtcNow.AddHours(1).Ticks
            };
            AllLastPrices[_market.Symbol] = last;
            AllChangePct[_market.Symbol] = Ticker.PriceChangePercent24h;
            IsConnected = true;
            DataSource = "Binance";
            StatusMessage = "Live · Binance";
            Notify();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ticker failed for {Sym}", sym);
            if (!IsConnected)
            {
                StatusMessage = "Market data unavailable (CORS/network). Retrying…";
                Notify();
            }
        }
    }

    private async Task RefreshDepthAndTradesAsync(CancellationToken ct)
    {
        var sym = _market.BinanceSymbol;
        try
        {
            var depthTask = _http.GetFromJsonAsync<JsonElement>(
                $"https://api.binance.com/api/v3/depth?symbol={sym}&limit=20", JsonOpts, ct);
            var tradesTask = _http.GetFromJsonAsync<JsonElement>(
                $"https://api.binance.com/api/v3/trades?symbol={sym}&limit=40", JsonOpts, ct);
            await Task.WhenAll(depthTask, tradesTask);
            var depth = depthTask.Result;
            var tradesEl = tradesTask.Result;

            Book.Bids = ParseLevels(depth, "bids", true);
            Book.Asks = ParseLevels(depth, "asks", false);
            Book.Sequence = DateTime.UtcNow.Ticks;
            Book.MarketId = _market.MarketId;
            Book.Symbol = _market.Symbol;

            var list = new List<TradeTick>();
            if (tradesEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var t in tradesEl.EnumerateArray().Reverse())
                {
                    list.Add(new TradeTick
                    {
                        MarketId = _market.MarketId,
                        TradeId = t.TryGetProperty("id", out var id) ? id.GetInt64() : 0,
                        Price = Dec(t, "price"),
                        Quantity = Dec(t, "qty"),
                        IsBuyerMaker = t.TryGetProperty("isBuyerMaker", out var ibm) && ibm.GetBoolean(),
                        TimestampUtcTicks = t.TryGetProperty("time", out var tm)
                            ? DateTimeOffset.FromUnixTimeMilliseconds(tm.GetInt64()).UtcTicks
                            : DateTime.UtcNow.Ticks
                    });
                }
            }
            lock (Trades)
            {
                Trades.Clear();
                Trades.AddRange(list);
            }
            IsConnected = true;
            DataSource = "Binance";
            StatusMessage = "Live · Binance";
            Notify();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "depth/trades failed");
        }
    }

    private async Task RefreshKlinesAsync(CancellationToken ct)
    {
        var sym = _market.BinanceSymbol;
        try
        {
            var url = $"https://api.binance.com/api/v3/klines?symbol={sym}&interval=1m&limit=240";
            var arr = await _http.GetFromJsonAsync<JsonElement>(url, JsonOpts, ct);
            if (arr.ValueKind != JsonValueKind.Array) return;
            var candles = new List<Candle>(240);
            foreach (var row in arr.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Array) continue;
                var a = row.EnumerateArray().ToArray();
                if (a.Length < 6) continue;
                candles.Add(new Candle
                {
                    Time = a[0].GetInt64() / 1000,
                    Open = DecNum(a[1]),
                    High = DecNum(a[2]),
                    Low = DecNum(a[3]),
                    Close = DecNum(a[4]),
                    Volume = DecNum(a[5])
                });
            }
            Candles.Clear();
            Candles.AddRange(candles);
            Notify();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "klines failed");
        }
    }

    private async Task RefreshAllTickersAsync(CancellationToken ct)
    {
        try
        {
            var url = "https://api.binance.com/api/v3/ticker/24hr";
            var arr = await _http.GetFromJsonAsync<JsonElement>(url, JsonOpts, ct);
            if (arr.ValueKind != JsonValueKind.Array) return;
            var wanted = MarketCatalog.All.ToDictionary(m => m.BinanceSymbol, m => m, StringComparer.OrdinalIgnoreCase);
            foreach (var row in arr.EnumerateArray())
            {
                if (!row.TryGetProperty("symbol", out var sEl)) continue;
                var bs = sEl.GetString() ?? "";
                if (!wanted.TryGetValue(bs, out var mkt)) continue;
                var uiSym = mkt.Symbol;
                var last = Dec(row, "lastPrice");
                var chg = Dec(row, "priceChangePercent");
                AllLastPrices[uiSym] = last;
                AllChangePct[uiSym] = chg;
                AllTickers[uiSym] = new TickerState
                {
                    MarketId = mkt.MarketId,
                    Symbol = uiSym,
                    LastPrice = last,
                    BestBid = Dec(row, "bidPrice"),
                    BestAsk = Dec(row, "askPrice"),
                    Volume24h = Dec(row, "volume"),
                    QuoteVolume24h = Dec(row, "quoteVolume"),
                    High24h = Dec(row, "highPrice"),
                    Low24h = Dec(row, "lowPrice"),
                    PriceChange24h = Dec(row, "priceChange"),
                    PriceChangePercent24h = chg
                };
            }
            Notify();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "all tickers failed");
        }
    }

    private static List<PriceLevel> ParseLevels(JsonElement root, string name, bool descending)
    {
        var list = new List<PriceLevel>();
        if (!root.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return list;
        decimal total = 0;
        foreach (var row in arr.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Array) continue;
            var parts = row.EnumerateArray().ToArray();
            if (parts.Length < 2) continue;
            var price = DecNum(parts[0]);
            var qty = DecNum(parts[1]);
            total += qty;
            list.Add(new PriceLevel { Price = price, Quantity = qty, Total = total });
        }
        list.Sort((a, b) => descending ? b.Price.CompareTo(a.Price) : a.Price.CompareTo(b.Price));
        total = 0;
        foreach (var l in list) { total += l.Quantity; l.Total = total; }
        return list;
    }

    private static decimal Dec(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p)) return 0m;
        return DecNum(p);
    }

    private static decimal DecNum(JsonElement p)
    {
        if (p.ValueKind == JsonValueKind.Number) return p.GetDecimal();
        if (p.ValueKind == JsonValueKind.String &&
            decimal.TryParse(p.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
            return d;
        return 0m;
    }

    private void Notify() => UiRefreshRequested?.Invoke();

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        if (_loop is not null)
        {
            try { await _loop; } catch { /* ignore */ }
        }
    }
}
