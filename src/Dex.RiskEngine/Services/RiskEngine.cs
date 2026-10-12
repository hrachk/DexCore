using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Dex.Domain.Orders;
using Dex.Domain.Risk;
using Dex.RiskEngine.Math;
using Dex.RiskEngine.Models;
using Microsoft.Extensions.Logging;

namespace Dex.RiskEngine.Services;

/// <summary>
/// Production Risk Engine.
/// - Pre-trade synchronous validation (available margin, position limits, OI caps)
/// - Post-trade mark-to-market and liquidation candidate detection
/// - Cross-Margin and Isolated Margin support
/// All calculations use System.Decimal. Designed for low-latency single-threaded or partitioned access.
/// </summary>
public sealed class RiskEngine
{
    private readonly ConcurrentDictionary<long, AccountMarginState> _accounts = new();
    private readonly ConcurrentDictionary<int, MarketRiskParameters> _markets = new();
    private readonly ConcurrentDictionary<int, decimal> _markPrices = new();
    private readonly ConcurrentDictionary<int, decimal> _indexPrices = new();
    private readonly ConcurrentDictionary<int, decimal> _openInterest = new(); // absolute OI per market
    private readonly ILogger<RiskEngine> _logger;

    // Insurance fund (simplified single currency)
    private decimal _insuranceFundBalance;

    public RiskEngine(ILogger<RiskEngine> logger, decimal initialInsuranceFund = 0m)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _insuranceFundBalance = initialInsuranceFund;
    }

    public decimal InsuranceFundBalance => _insuranceFundBalance;

    // ───────────────────────── Market & Price Management ─────────────────────────

    public void RegisterMarket(MarketRiskParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (!_markets.TryAdd(parameters.MarketId, parameters))
            throw new InvalidOperationException($"Market {parameters.MarketId} is already registered.");
        _openInterest.TryAdd(parameters.MarketId, 0m);
        _logger.LogInformation("Registered market {MarketId} IM={Im:P2} MM={Mm:P2} MaxPos={MaxPos}",
            parameters.MarketId, parameters.InitialMarginRate, parameters.MaintenanceMarginRate, parameters.MaxPositionSize);
    }

    public void UpdateMarkPrice(int marketId, decimal markPrice)
    {
        if (markPrice <= 0m) throw new ArgumentOutOfRangeException(nameof(markPrice));
        _markPrices[marketId] = markPrice;
    }

    public void UpdateIndexPrice(int marketId, decimal indexPrice)
    {
        if (indexPrice <= 0m) throw new ArgumentOutOfRangeException(nameof(indexPrice));
        _indexPrices[marketId] = indexPrice;
    }

    public bool TryGetMarkPrice(int marketId, out decimal price) => _markPrices.TryGetValue(marketId, out price);

    // ───────────────────────── Account Management ─────────────────────────

    public AccountMarginState GetOrCreateAccount(long accountId, decimal initialBalance = 0m)
        => _accounts.GetOrAdd(accountId, id => new AccountMarginState(id, initialBalance));

    public bool TryGetAccount(long accountId, out AccountMarginState? state)
        => _accounts.TryGetValue(accountId, out state);

    public void Deposit(long accountId, decimal amount)
    {
        var account = GetOrCreateAccount(accountId);
        account.Deposit(amount);
        _logger.LogDebug("Account {AccountId} deposited {Amount}", accountId, amount);
    }

    // ───────────────────────── Pre-Trade Risk Check ─────────────────────────

    /// <summary>
    /// Synchronous pre-trade validation. Must be called before sending the order to MatchingEngine.
    /// Returns Accept or Reject with reason. Zero-allocation on the happy path as much as possible.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public RiskCheckResult ValidateOrder(Order order, MarginMode marginMode, decimal? leverageOverride = null)
    {
        if (order is null)
            return RiskCheckResult.Reject(RiskRejectReason.InvalidParameters, "Order is null.");

        if (!_markets.TryGetValue(order.MarketId, out var market))
            return RiskCheckResult.Reject(RiskRejectReason.MarketNotFound, $"Market {order.MarketId} not registered.");

        if (!_accounts.TryGetValue(order.AccountId, out var account))
            return RiskCheckResult.Reject(RiskRejectReason.AccountNotFound, $"Account {order.AccountId} not found.");

        // Price for notional: limit price or current mark for market orders
        decimal refPrice;
        if (order.Type == OrderType.Market)
        {
            if (!_markPrices.TryGetValue(order.MarketId, out refPrice) || refPrice <= 0m)
                return RiskCheckResult.Reject(RiskRejectReason.InvalidParameters, "Mark price unavailable for market order.");
        }
        else
        {
            refPrice = order.Price;
            if (refPrice <= 0m)
                return RiskCheckResult.Reject(RiskRejectReason.InvalidParameters, "Invalid limit price.");
        }

        var notional = MarginCalculator.Notional(order.Quantity, refPrice);
        if (notional < market.MinNotional)
            return RiskCheckResult.Reject(RiskRejectReason.BelowMinNotional,
                $"Notional {notional} below minimum {market.MinNotional}.", 0m, 0m);

        // Effective IM rate (leverage override can only increase rate, never decrease below market)
        var imRate = market.InitialMarginRate;
        if (leverageOverride.HasValue && leverageOverride.Value > 0m)
        {
            var requestedRate = 1m / leverageOverride.Value;
            if (requestedRate > imRate) imRate = requestedRate; // more conservative
        }

        var requiredIm = MarginCalculator.InitialMargin(notional, imRate);

        // Position size check
        account.TryGetPosition(order.MarketId, marginMode, out var existingPos);
        var currentQty = existingPos?.Quantity ?? 0m;
        var projectedQty = currentQty + order.Quantity; // simplified; real system nets by side
        if (projectedQty > market.MaxPositionSize)
            return RiskCheckResult.Reject(RiskRejectReason.ExceedsMaxPositionSize,
                $"Projected size {projectedQty} exceeds max {market.MaxPositionSize}.", requiredIm, 0m);

        // Open Interest cap (simplified absolute)
        var currentOi = _openInterest.GetValueOrDefault(order.MarketId, 0m);
        if (currentOi + order.Quantity > market.OpenInterestCap)
            return RiskCheckResult.Reject(RiskRejectReason.ExceedsOpenInterestCap,
                $"OI would exceed cap {market.OpenInterestCap}.", requiredIm, 0m);

        // Available margin
        var equity = account.Equity(_markPrices);
        decimal available;

        if (marginMode == MarginMode.Cross)
        {
            // Cross: equity minus already required IM of all positions
            var usedIm = CalculateTotalInitialMargin(account);
            available = equity - usedIm;
        }
        else
        {
            // Isolated: only the free wallet balance can be used for new isolated margin
            available = account.WalletBalance; // simplified; real system tracks free collateral
        }

        if (available < requiredIm)
            return RiskCheckResult.Reject(RiskRejectReason.InsufficientMargin,
                $"Available {available} < required IM {requiredIm}.", requiredIm, available);

        return RiskCheckResult.Accept(requiredIm, available);
    }

    // ───────────────────────── Post-Trade / Mark-to-Market ─────────────────────────

    /// <summary>
    /// Applies a trade fill to the account position and updates realized PnL / OI.
    /// </summary>
    public void ApplyTrade(Trade trade, MarginMode takerMarginMode, MarginMode makerMarginMode)
    {
        // Update OI (simplified: increase by trade qty; real system nets long/short OI)
        _openInterest.AddOrUpdate(trade.MarketId, trade.Quantity, (_, old) => old + trade.Quantity);

        // Taker
        if (_accounts.TryGetValue(trade.TakerAccountId, out var takerAcc))
        {
            var pos = takerAcc.GetOrCreatePosition(trade.MarketId, takerMarginMode);
            var side = trade.TakerSide == OrderSide.Buy ? PositionSide.Long : PositionSide.Short;
            var realized = pos.ApplyFill(side, trade.Quantity, trade.Price, 0m);
            if (realized != 0m) takerAcc.ApplyRealizedPnl(realized);
        }

        // Maker
        if (_accounts.TryGetValue(trade.MakerAccountId, out var makerAcc))
        {
            var pos = makerAcc.GetOrCreatePosition(trade.MarketId, makerMarginMode);
            var makerSide = trade.TakerSide == OrderSide.Buy ? PositionSide.Short : PositionSide.Long;
            var realized = pos.ApplyFill(makerSide, trade.Quantity, trade.Price, 0m);
            if (realized != 0m) makerAcc.ApplyRealizedPnl(realized);
        }
    }

    /// <summary>
    /// Scans all accounts for positions that breach Maintenance Margin and returns liquidation candidates.
    /// Partial liquidation: reduce size so that remaining position satisfies a safety buffer above MM.
    /// </summary>
    public IReadOnlyList<LiquidationCandidate> ScanLiquidations(decimal safetyBufferMultiplier = 1.1m)
    {
        var candidates = new List<LiquidationCandidate>();

        foreach (var account in _accounts.Values)
        {
            foreach (var kv in account.Positions)
            {
                var pos = kv.Value;
                if (!pos.IsOpen) continue;

                if (!_markPrices.TryGetValue(pos.MarketId, out var mark) || mark <= 0m) continue;
                if (!_markets.TryGetValue(pos.MarketId, out var market)) continue;

                var notional = MarginCalculator.Notional(pos.Quantity, mark);
                var upnl = pos.UnrealizedPnl(mark);
                var mm = MarginCalculator.MaintenanceMargin(notional, market.MaintenanceMarginRate);

                decimal equityOrAllocated;
                if (pos.MarginMode == MarginMode.Isolated)
                {
                    equityOrAllocated = pos.IsolatedMargin + upnl;
                }
                else
                {
                    // Cross: use account equity (conservative; real systems allocate)
                    equityOrAllocated = account.Equity(_markPrices);
                }

                var ratio = MarginCalculator.MarginRatio(equityOrAllocated, notional);

                if (ratio >= market.MaintenanceMarginRate)
                    continue; // healthy

                // Breach → compute liquidation size
                // Target: bring margin ratio back to MM * safetyBufferMultiplier
                var targetRatio = market.MaintenanceMarginRate * safetyBufferMultiplier;
                var targetNotional = equityOrAllocated / targetRatio;
                var targetQty = targetNotional / mark;
                var liqQty = pos.Quantity - targetQty;
                if (liqQty <= 0m) liqQty = pos.Quantity; // full

                var isPartial = liqQty < pos.Quantity;

                candidates.Add(new LiquidationCandidate(
                    accountId: account.AccountId,
                    marketId: pos.MarketId,
                    marginMode: pos.MarginMode,
                    side: pos.Side,
                    quantity: pos.Quantity,
                    entryPrice: pos.EntryPrice,
                    markPrice: mark,
                    unrealizedPnl: upnl,
                    marginRatio: ratio,
                    maintenanceMargin: mm,
                    isPartial: isPartial,
                    liquidationQuantity: liqQty));
            }
        }

        return candidates;
    }

    /// <summary>
    /// Executes a liquidation (skeleton). In production this would send IOC reduce-only orders
    /// to the MatchingEngine and settle residual against the Insurance Fund.
    /// </summary>
    public void ExecuteLiquidation(LiquidationCandidate candidate)
    {
        if (!_accounts.TryGetValue(candidate.AccountId, out var account)) return;
        if (!account.TryGetPosition(candidate.MarketId, candidate.MarginMode, out var pos) || pos is null || !pos.IsOpen)
            return;

        var liqQty = System.Math.Min(candidate.LiquidationQuantity, pos.Quantity);
        if (liqQty <= 0m) return;

        // Close part of the position at mark (simplified; real system uses actual fill price)
        var closeSide = pos.Side == PositionSide.Long ? PositionSide.Short : PositionSide.Long;
        var realized = pos.ApplyFill(closeSide, liqQty, candidate.MarkPrice, 0m);
        account.ApplyRealizedPnl(realized);

        // Penalty to insurance fund
        if (_markets.TryGetValue(candidate.MarketId, out var market))
        {
            var penalty = MarginCalculator.Notional(liqQty, candidate.MarkPrice) * market.LiquidationPenaltyRate;
            _insuranceFundBalance += penalty;
            // In a full implementation the remaining shortfall would be covered by insurance fund
            // and ADL if necessary.
        }

        _logger.LogWarning(
            "Liquidated Account={AccountId} Market={MarketId} Qty={Qty} Partial={Partial} PnL={Pnl}",
            candidate.AccountId, candidate.MarketId, liqQty, candidate.IsPartial, realized);
    }

    // ───────────────────────── Helpers ─────────────────────────

    private decimal CalculateTotalInitialMargin(AccountMarginState account)
    {
        decimal total = 0m;
        foreach (var kv in account.Positions)
        {
            var pos = kv.Value;
            if (!pos.IsOpen) continue;
            if (!_markPrices.TryGetValue(pos.MarketId, out var mark)) continue;
            if (!_markets.TryGetValue(pos.MarketId, out var market)) continue;

            var notional = MarginCalculator.Notional(pos.Quantity, mark);
            total += MarginCalculator.InitialMargin(notional, market.InitialMarginRate);
        }
        return total;
    }
}
