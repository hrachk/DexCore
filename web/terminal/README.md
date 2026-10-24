# DexCore terminal

Live perpetual desk used by the Grok preview build.

- 20 USDT markets, book, tape, and candles from `data-api.binance.vision`
- Limit and market orders, leverage, cross/isolated, post-only, reduce-only
- Fills walk the live book; margin, liquidation, cancel, and close are enforced in `src/lib/exchange/engine.ts`
- Desk starts at 25,000 USDT (paper). Wallet connect reads an address only; it does not move chain funds

These files are the terminal surface. They import the app shell (`@/lib/auth`, router, Tailwind tokens) from the hosted app, not from the .NET solution.
