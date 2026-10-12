using Dex.BlazorUI.Models;

namespace Dex.BlazorUI.Services;

/// <summary>
/// Client-side trading ledger: balances, positions, open orders, fills.
/// Market orders fill at live bid/ask; limits rest and fill when price crosses.
/// This is a functional trading simulator on live prices (paper trading) until
/// on-chain settlement contracts are deployed and wired.
/// </summary>
public sealed class TradingPortfolioService
{
    private readonly object _gate = new();
    private long _nextOrderId = 1_000_000;
    private long _nextTradeId = 1;

    public decimal UsdtBalance { get; private set; } = 100_000m;
    public decimal RealizedPnl { get; private set; }
    public List<OpenOrder> OpenOrders { get; } = new();
    public List<PositionRow> Positions { get; } = new();
    public List<TradeHistoryRow> History { get; } = new();

    public event Action? Changed;

    public decimal Equity(decimal markPricesSumUpnl)
        => UsdtBalance + markPricesSumUpnl;

    public decimal AvailableMargin(decimal upnl)
    {
        var used = Positions.Sum(p => p.Margin);
        return Math.Max(0, UsdtBalance + upnl - used);
    }

    public (bool Ok, string Message, OpenOrder? Order) PlaceOrder(
        MarketInfo market,
        OrderSide side,
        OrderType type,
        decimal price,
        decimal qty,
        int leverage,
        MarginMode marginMode,
        bool reduceOnly,
        decimal? takeProfit,
        decimal? stopLoss,
        decimal bestBid,
        decimal bestAsk,
        decimal lastPrice)
    {
        if (qty <= 0) return (false, "Invalid quantity", null);
        if (leverage < 1) leverage = 1;
        if (leverage > market.MaxLeverage) leverage = market.MaxLeverage;

        var refPx = type == OrderType.Market
            ? (side == OrderSide.Buy
                ? (bestAsk > 0 ? bestAsk : lastPrice)
                : (bestBid > 0 ? bestBid : lastPrice))
            : price;

        if (type != OrderType.Market && price <= 0)
            return (false, "Limit price required", null);
        if (refPx <= 0) return (false, "No market price", null);

        var notional = refPx * qty;
        var marginNeeded = notional / leverage;
        var upnl = Positions.Sum(p => p.UnrealizedPnl);
        var available = AvailableMargin(upnl);

        lock (_gate)
        {
            if (!reduceOnly && marginNeeded > available + 0.01m)
                return (false, $"Insufficient margin. Need {marginNeeded:N2} USDT, available {available:N2}", null);

            var order = new OpenOrder
            {
                OrderId = Interlocked.Increment(ref _nextOrderId),
                ClientOrderId = DateTime.UtcNow.Ticks,
                MarketId = market.MarketId,
                Symbol = market.Symbol,
                Side = side,
                Type = type,
                Price = type == OrderType.Market ? refPx : price,
                Quantity = qty,
                RemainingQuantity = qty,
                FilledQuantity = 0,
                Status = OrderStatus.New,
                CreatedAtTicks = DateTime.UtcNow.Ticks,
                TakeProfit = takeProfit,
                StopLoss = stopLoss
            };

            if (type == OrderType.Market)
            {
                FillOrder(order, refPx, qty, market, leverage, marginMode, reduceOnly);
                order.Status = OrderStatus.Filled;
                order.RemainingQuantity = 0;
                order.FilledQuantity = qty;
            }
            else
            {
                // Immediate match if already marketable
                var cross = side == OrderSide.Buy ? bestAsk > 0 && price >= bestAsk : bestBid > 0 && price <= bestBid;
                if (cross)
                {
                    var fillPx = side == OrderSide.Buy ? bestAsk : bestBid;
                    FillOrder(order, fillPx, qty, market, leverage, marginMode, reduceOnly);
                    order.Status = OrderStatus.Filled;
                    order.RemainingQuantity = 0;
                    order.FilledQuantity = qty;
                }
                else
                {
                    OpenOrders.Insert(0, order);
                }
            }

            Changed?.Invoke();
            return (true, order.Status == OrderStatus.Filled ? "Filled" : "Order accepted", order);
        }
    }

    public bool CancelOrder(long orderId)
    {
        lock (_gate)
        {
            var o = OpenOrders.FirstOrDefault(x => x.OrderId == orderId);
            if (o is null) return false;
            o.Status = OrderStatus.Cancelled;
            OpenOrders.Remove(o);
            Changed?.Invoke();
            return true;
        }
    }

    public void ClosePosition(string symbol, decimal lastPrice, decimal bestBid, decimal bestAsk)
    {
        lock (_gate)
        {
            var pos = Positions.FirstOrDefault(p => p.Symbol == symbol);
            if (pos is null) return;
            var side = pos.Side == "Long" ? OrderSide.Sell : OrderSide.Buy;
            var px = side == OrderSide.Sell
                ? (bestBid > 0 ? bestBid : lastPrice)
                : (bestAsk > 0 ? bestAsk : lastPrice);
            var m = MarketCatalog.Get(symbol);
            FillOrder(new OpenOrder
            {
                OrderId = Interlocked.Increment(ref _nextOrderId),
                Symbol = symbol,
                MarketId = m.MarketId,
                Side = side,
                Type = OrderType.Market,
                Price = px,
                Quantity = pos.Quantity,
                RemainingQuantity = pos.Quantity
            }, px, pos.Quantity, m, pos.Leverage, pos.MarginMode, reduceOnly: true);
            Changed?.Invoke();
        }
    }

    /// <summary>Call on each price tick to fill resting limits and TP/SL.</summary>
    public void OnPriceTick(string symbol, decimal last, decimal bid, decimal ask)
    {
        lock (_gate)
        {
            var changed = false;
            // Resting limits
            foreach (var o in OpenOrders.Where(x => x.Symbol == symbol && x.Status == OrderStatus.New).ToList())
            {
                var fillable = o.Side == OrderSide.Buy
                    ? ask > 0 && o.Price >= ask
                    : bid > 0 && o.Price <= bid;
                if (!fillable) continue;
                var px = o.Side == OrderSide.Buy ? ask : bid;
                var m = MarketCatalog.Get(symbol);
                var lev = 10;
                var existing = Positions.FirstOrDefault(p => p.Symbol == symbol);
                if (existing is not null) lev = existing.Leverage;
                FillOrder(o, px, o.RemainingQuantity, m, lev, MarginMode.Cross, false);
                o.Status = OrderStatus.Filled;
                o.FilledQuantity = o.Quantity;
                o.RemainingQuantity = 0;
                OpenOrders.Remove(o);
                changed = true;
            }

            // TP / SL on positions
            var pos = Positions.FirstOrDefault(p => p.Symbol == symbol);
            if (pos is not null)
            {
                pos.MarkPrice = last;
                pos.UnrealizedPnl = pos.Side == "Long"
                    ? (last - pos.EntryPrice) * pos.Quantity
                    : (pos.EntryPrice - last) * pos.Quantity;

                var hitTp = pos.TakeProfit is { } tp && (
                    (pos.Side == "Long" && last >= tp) || (pos.Side == "Short" && last <= tp));
                var hitSl = pos.StopLoss is { } sl && (
                    (pos.Side == "Long" && last <= sl) || (pos.Side == "Short" && last >= sl));

                if (hitTp || hitSl)
                {
                    var side = pos.Side == "Long" ? OrderSide.Sell : OrderSide.Buy;
                    var m = MarketCatalog.Get(symbol);
                    FillOrder(new OpenOrder
                    {
                        OrderId = Interlocked.Increment(ref _nextOrderId),
                        Symbol = symbol,
                        MarketId = m.MarketId,
                        Side = side,
                        Quantity = pos.Quantity,
                        Price = last
                    }, last, pos.Quantity, m, pos.Leverage, pos.MarginMode, true);
                    changed = true;
                }
            }
            else
            {
                // still update marks
                foreach (var p in Positions.Where(x => x.Symbol == symbol))
                {
                    p.MarkPrice = last;
                    p.UnrealizedPnl = p.Side == "Long"
                        ? (last - p.EntryPrice) * p.Quantity
                        : (p.EntryPrice - last) * p.Quantity;
                }
            }

            if (changed) Changed?.Invoke();
            else Changed?.Invoke(); // mark updates
        }
    }

    private void FillOrder(OpenOrder order, decimal fillPrice, decimal qty, MarketInfo market, int leverage, MarginMode mode, bool reduceOnly)
    {
        var fee = fillPrice * qty * 0.0004m; // 4 bps
        var pos = Positions.FirstOrDefault(p => p.Symbol == order.Symbol);
        var signedQty = order.Side == OrderSide.Buy ? qty : -qty;

        if (pos is null || pos.Quantity == 0)
        {
            if (reduceOnly) return;
            var margin = fillPrice * qty / Math.Max(1, leverage);
            if (margin > UsdtBalance) margin = UsdtBalance;
            UsdtBalance -= margin;
            Positions.Insert(0, new PositionRow
            {
                MarketId = market.MarketId,
                Symbol = market.Symbol,
                Side = order.Side == OrderSide.Buy ? "Long" : "Short",
                Quantity = qty,
                EntryPrice = fillPrice,
                MarkPrice = fillPrice,
                Leverage = leverage,
                MarginMode = mode,
                Margin = margin,
                LiquidationPrice = order.Side == OrderSide.Buy
                    ? fillPrice * (1m - 0.9m / leverage)
                    : fillPrice * (1m + 0.9m / leverage),
                TakeProfit = order.TakeProfit,
                StopLoss = order.StopLoss,
                UnrealizedPnl = 0
            });
        }
        else
        {
            var posSign = pos.Side == "Long" ? 1m : -1m;
            var newSigned = posSign * pos.Quantity + signedQty;

            // Same direction — add
            if (Math.Sign(newSigned) == Math.Sign(posSign) || newSigned == 0)
            {
                if (Math.Sign(signedQty) == Math.Sign(posSign))
                {
                    var totalQty = pos.Quantity + qty;
                    pos.EntryPrice = (pos.EntryPrice * pos.Quantity + fillPrice * qty) / totalQty;
                    pos.Quantity = totalQty;
                    var extraMargin = fillPrice * qty / Math.Max(1, leverage);
                    pos.Margin += extraMargin;
                    UsdtBalance -= extraMargin;
                }
                else
                {
                    // Reduce / close
                    var closeQty = Math.Min(pos.Quantity, qty);
                    var pnl = pos.Side == "Long"
                        ? (fillPrice - pos.EntryPrice) * closeQty
                        : (pos.EntryPrice - fillPrice) * closeQty;
                    pnl -= fee;
                    RealizedPnl += pnl;
                    UsdtBalance += pos.Margin * (closeQty / pos.Quantity) + pnl;
                    pos.Margin -= pos.Margin * (closeQty / pos.Quantity);
                    pos.Quantity -= closeQty;
                    if (pos.Quantity <= 0.0000001m)
                        Positions.Remove(pos);
                }
            }
            else
            {
                // Flip
                var closeQty = pos.Quantity;
                var pnl = pos.Side == "Long"
                    ? (fillPrice - pos.EntryPrice) * closeQty
                    : (pos.EntryPrice - fillPrice) * closeQty;
                pnl -= fee;
                RealizedPnl += pnl;
                UsdtBalance += pos.Margin + pnl;
                Positions.Remove(pos);
                var remain = Math.Abs(newSigned);
                if (remain > 0 && !reduceOnly)
                {
                    var margin = fillPrice * remain / Math.Max(1, leverage);
                    UsdtBalance -= margin;
                    Positions.Insert(0, new PositionRow
                    {
                        MarketId = market.MarketId,
                        Symbol = market.Symbol,
                        Side = newSigned > 0 ? "Long" : "Short",
                        Quantity = remain,
                        EntryPrice = fillPrice,
                        MarkPrice = fillPrice,
                        Leverage = leverage,
                        MarginMode = mode,
                        Margin = margin,
                        LiquidationPrice = newSigned > 0
                            ? fillPrice * (1m - 0.9m / leverage)
                            : fillPrice * (1m + 0.9m / leverage),
                        TakeProfit = order.TakeProfit,
                        StopLoss = order.StopLoss
                    });
                }
            }
        }

        History.Insert(0, new TradeHistoryRow
        {
            TradeId = Interlocked.Increment(ref _nextTradeId),
            Symbol = order.Symbol,
            Side = order.Side,
            Price = fillPrice,
            Quantity = qty,
            Fee = fee,
            TimestampTicks = DateTime.UtcNow.Ticks,
            Note = order.Type.ToString()
        });
        if (History.Count > 200) History.RemoveRange(200, History.Count - 200);
    }

    public void Deposit(decimal amount)
    {
        if (amount <= 0) return;
        UsdtBalance += amount;
        Changed?.Invoke();
    }

    public bool Withdraw(decimal amount)
    {
        if (amount <= 0 || amount > UsdtBalance) return false;
        UsdtBalance -= amount;
        Changed?.Invoke();
        return true;
    }
}
