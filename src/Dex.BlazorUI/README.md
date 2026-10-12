# DexCore Trading Terminal (Blazor WASM)

Production-oriented perpetual trading UI with **live market data** and an **executable order ticket**.

## What works (real)

| Feature | Source |
|---------|--------|
| 24 markets (BTC, ETH, SOL, … PEPE, WIF) | Binance public REST |
| Live last / bid / ask / 24h stats | `api.binance.com` |
| Live order book depth | Binance depth |
| Live trades tape | Binance trades |
| Candlestick chart | Binance klines 1m |
| Place Limit / Market orders | Local matching ledger on **live prices** |
| Positions, margin, uPnL, TP/SL | Portfolio engine |
| Cancel orders / close positions | Portfolio engine |
| MetaMask connect | Real EIP-1193 wallet |

## Run

```bash
cd DexCore
dotnet run --project src/Dex.BlazorUI
```

Open **http://localhost:5200**

Allow outbound HTTPS to `api.binance.com` (browser). If corporate network blocks it, status stays on “Reconnecting”.

## Trading mode

1. **Paper (default)** — $100,000 USDT simulated balance. Orders execute against live Binance bid/ask. This is fully usable for testing strategy and UI.
2. **Wallet** — Connect MetaMask for real address identity. On-chain settlement requires deployed `VaultManager` / `DexSettlement` (see `contracts/`) and a configured vault address in `wwwroot/appsettings.json`.

## On-chain go-live checklist

1. Deploy contracts (`contracts/`) to Sepolia or mainnet  
2. Set `Web3:VaultAddress` in `wwwroot/appsettings.json`  
3. Wire `MetamaskInteropService.SendDepositAsync` from a Deposit panel  
4. Point settlement batch worker at the deployed `DexSettlement`  
5. Replace paper `TradingPortfolioService` fills with API → matching → settlement path  

Until then, the terminal is a **live-price execution desk** (paper collateral), not a custodial CEX.
