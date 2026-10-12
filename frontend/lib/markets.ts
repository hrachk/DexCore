import type { MarketInfo } from "./types";

export const MARKETS: MarketInfo[] = [
  {
    marketId: 1,
    symbol: "BTC-USD",
    base: "BTC",
    quote: "USD",
    tickSize: 0.1,
    lotSize: 0.001,
    maxLeverage: 50,
  },
  {
    marketId: 2,
    symbol: "ETH-USD",
    base: "ETH",
    quote: "USD",
    tickSize: 0.01,
    lotSize: 0.01,
    maxLeverage: 50,
  },
  {
    marketId: 3,
    symbol: "SOL-USD",
    base: "SOL",
    quote: "USD",
    tickSize: 0.001,
    lotSize: 0.1,
    maxLeverage: 25,
  },
];

export function getMarket(symbol: string): MarketInfo {
  return (
    MARKETS.find((m) => m.symbol.toUpperCase() === symbol.toUpperCase()) ??
    MARKETS[0]
  );
}

export function formatPrice(n: number, decimals = 2): string {
  return n.toLocaleString("en-US", {
    minimumFractionDigits: decimals,
    maximumFractionDigits: decimals,
  });
}

export function formatQty(n: number, decimals = 4): string {
  return n.toLocaleString("en-US", {
    minimumFractionDigits: 0,
    maximumFractionDigits: decimals,
  });
}

export function formatPnl(n: number): string {
  const sign = n >= 0 ? "+" : "";
  return `${sign}${n.toLocaleString("en-US", {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  })}`;
}
