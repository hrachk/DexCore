using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Dex.Domain.Risk;

/// <summary>
/// Account-level margin state. Holds balances and all positions.
/// Equity = WalletBalance + UnrealizedPnl (sum across positions)
/// Available = Equity - InitialMarginRequirement (or more precise variants)
/// </summary>
public sealed class AccountMarginState
{
    public long AccountId { get; }
    public decimal WalletBalance { get; private set; }   // settled USDC / collateral
    public decimal LockedMargin { get; private set; }    // margin locked for open orders (optional extension)

    private readonly ConcurrentDictionary<(int MarketId, MarginMode Mode), Position> _positions = new();

    public AccountMarginState(long accountId, decimal initialWalletBalance = 0m)
    {
        if (accountId <= 0) throw new ArgumentOutOfRangeException(nameof(accountId));
        if (initialWalletBalance < 0m) throw new ArgumentOutOfRangeException(nameof(initialWalletBalance));

        AccountId = accountId;
        WalletBalance = initialWalletBalance;
        LockedMargin = 0m;
    }

    public IReadOnlyDictionary<(int MarketId, MarginMode Mode), Position> Positions => _positions;

    public void Deposit(decimal amount)
    {
        if (amount <= 0m) throw new ArgumentOutOfRangeException(nameof(amount));
        WalletBalance += amount;
    }

    public void Withdraw(decimal amount)
    {
        if (amount <= 0m) throw new ArgumentOutOfRangeException(nameof(amount));
        if (amount > WalletBalance) throw new InvalidOperationException("Insufficient wallet balance.");
        WalletBalance -= amount;
    }

    public Position GetOrCreatePosition(int marketId, MarginMode mode)
    {
        return _positions.GetOrAdd((marketId, mode), key => new Position(AccountId, key.MarketId, key.Mode));
    }

    public bool TryGetPosition(int marketId, MarginMode mode, out Position? position)
        => _positions.TryGetValue((marketId, mode), out position);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public decimal TotalUnrealizedPnl(IReadOnlyDictionary<int, decimal> markPrices)
    {
        decimal total = 0m;
        foreach (var kv in _positions)
        {
            if (markPrices.TryGetValue(kv.Key.MarketId, out var mark))
                total += kv.Value.UnrealizedPnl(mark);
        }
        return total;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public decimal Equity(IReadOnlyDictionary<int, decimal> markPrices)
        => WalletBalance + TotalUnrealizedPnl(markPrices);

    public void ApplyRealizedPnl(decimal pnl)
    {
        WalletBalance += pnl;
    }
}
