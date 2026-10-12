# DexCore Web UI

Institutional trading terminal for the DexCore hybrid DEX.

**Stack:** Next.js 14 · React 18 · TypeScript · Tailwind CSS · Lucide · Lightweight Charts · RainbowKit / wagmi · SignalR

## Features

- TradingView **Lightweight Charts** candlestick terminal
- Live **Order Book** (bids/asks depth bars) via SignalR → `Dex.ApiGateway`
- **Recent trades** tape
- Order ticket: Limit / Market / Stop, Cross/Isolated, leverage slider, post-only / reduce-only
- Positions · Open orders · Trade history with uPnL
- **Wallet connect** (MetaMask, WalletConnect, Coinbase via RainbowKit)
- Automatic **mock market data** when the API is offline so the UI is always usable

## Quick start

```bash
cd frontend
cp .env.example .env.local   # optional
npm install
npm run dev
```

Open **http://localhost:3000** → redirects to `/trade/BTC-USD`.

### With live backend

```bash
# terminal 1 — API (from DexCore root, after dotnet build)
dotnet run --project src/Dex.ApiGateway

# terminal 2 — UI
cd frontend && npm run dev
```

Set in `.env.local`:

```
NEXT_PUBLIC_SIGNALR_URL=http://localhost:5000/hubs/marketdata
NEXT_PUBLIC_API_URL=http://localhost:5000
NEXT_PUBLIC_WALLETCONNECT_PROJECT_ID=<from cloud.walletconnect.com>
```

## Routes

| Path | Description |
|------|-------------|
| `/` | Redirect → `/trade/BTC-USD` |
| `/trade/BTC-USD` | Main terminal |
| `/trade/ETH-USD` | ETH market |
| `/trade/SOL-USD` | SOL market |

## Project structure

```
frontend/
├── app/
│   ├── layout.tsx          # RainbowKit + wagmi providers
│   ├── page.tsx
│   ├── globals.css
│   └── trade/[symbol]/page.tsx
├── components/
│   ├── layout/Header.tsx
│   ├── trade/
│   │   ├── TradingTerminal.tsx
│   │   ├── CandleChart.tsx
│   │   ├── OrderBook.tsx
│   │   ├── OrderForm.tsx
│   │   ├── TradesTape.tsx
│   │   └── BottomPanel.tsx
│   └── wallet/Providers.tsx
├── hooks/useOrderBook.ts
└── lib/
    ├── types.ts
    ├── markets.ts
    ├── mockData.ts
    ├── signalr.ts
    └── wagmi.ts
```
