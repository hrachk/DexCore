# DexCore terminal

Snapshot of the live perpetual desk (pushed without touching the .NET tree).

Layout:
- `exchange/` — matching, margin, liquidation, ledger
- `terminal/` — chart, book, order form, shell
- `routes/` — Trade, Markets, Portfolio, Vault

Behavior:
- 20 USDT markets, book, tape, and candles from data-api.binance.vision
- Limit and market, leverage, cross/isolated, post-only, reduce-only
- Fills walk the live book; margin checks reject under-collateralized orders
- Desk starts at 25,000 USDT (paper). Wallet connect reads an address only

These files import the hosted app shell (`@/` auth, router, Tailwind). They are not a standalone Next app.
