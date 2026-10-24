export type Market = {
  id: number;
  symbol: string;
  binance: string;
  base: string;
  quote: "USDT";
  tick: number;
  lot: number;
  maxLev: number;
  pxDp: number;
  qtyDp: number;
};

export const MARKETS: Market[] = [
  { id: 1, symbol: "BTC-USDT", binance: "BTCUSDT", base: "BTC", quote: "USDT", tick: 0.1, lot: 0.0001, maxLev: 50, pxDp: 1, qtyDp: 4 },
  { id: 2, symbol: "ETH-USDT", binance: "ETHUSDT", base: "ETH", quote: "USDT", tick: 0.01, lot: 0.001, maxLev: 50, pxDp: 2, qtyDp: 3 },
  { id: 3, symbol: "SOL-USDT", binance: "SOLUSDT", base: "SOL", quote: "USDT", tick: 0.01, lot: 0.01, maxLev: 25, pxDp: 2, qtyDp: 2 },
  { id: 4, symbol: "BNB-USDT", binance: "BNBUSDT", base: "BNB", quote: "USDT", tick: 0.01, lot: 0.001, maxLev: 25, pxDp: 2, qtyDp: 3 },
  { id: 5, symbol: "XRP-USDT", binance: "XRPUSDT", base: "XRP", quote: "USDT", tick: 0.0001, lot: 0.1, maxLev: 20, pxDp: 4, qtyDp: 1 },
  { id: 6, symbol: "DOGE-USDT", binance: "DOGEUSDT", base: "DOGE", quote: "USDT", tick: 0.00001, lot: 1, maxLev: 20, pxDp: 5, qtyDp: 0 },
  { id: 7, symbol: "ADA-USDT", binance: "ADAUSDT", base: "ADA", quote: "USDT", tick: 0.0001, lot: 1, maxLev: 20, pxDp: 4, qtyDp: 0 },
  { id: 8, symbol: "AVAX-USDT", binance: "AVAXUSDT", base: "AVAX", quote: "USDT", tick: 0.01, lot: 0.1, maxLev: 25, pxDp: 2, qtyDp: 1 },
  { id: 9, symbol: "LINK-USDT", binance: "LINKUSDT", base: "LINK", quote: "USDT", tick: 0.001, lot: 0.1, maxLev: 20, pxDp: 3, qtyDp: 1 },
  { id: 10, symbol: "DOT-USDT", binance: "DOTUSDT", base: "DOT", quote: "USDT", tick: 0.001, lot: 0.1, maxLev: 20, pxDp: 3, qtyDp: 1 },
  { id: 11, symbol: "LTC-USDT", binance: "LTCUSDT", base: "LTC", quote: "USDT", tick: 0.01, lot: 0.01, maxLev: 20, pxDp: 2, qtyDp: 2 },
  { id: 12, symbol: "ATOM-USDT", binance: "ATOMUSDT", base: "ATOM", quote: "USDT", tick: 0.001, lot: 0.1, maxLev: 20, pxDp: 3, qtyDp: 1 },
  { id: 13, symbol: "UNI-USDT", binance: "UNIUSDT", base: "UNI", quote: "USDT", tick: 0.001, lot: 0.1, maxLev: 20, pxDp: 3, qtyDp: 1 },
  { id: 14, symbol: "APT-USDT", binance: "APTUSDT", base: "APT", quote: "USDT", tick: 0.001, lot: 0.1, maxLev: 20, pxDp: 3, qtyDp: 1 },
  { id: 15, symbol: "ARB-USDT", binance: "ARBUSDT", base: "ARB", quote: "USDT", tick: 0.0001, lot: 0.1, maxLev: 20, pxDp: 4, qtyDp: 1 },
  { id: 16, symbol: "OP-USDT", binance: "OPUSDT", base: "OP", quote: "USDT", tick: 0.0001, lot: 0.1, maxLev: 20, pxDp: 4, qtyDp: 1 },
  { id: 17, symbol: "SUI-USDT", binance: "SUIUSDT", base: "SUI", quote: "USDT", tick: 0.0001, lot: 0.1, maxLev: 20, pxDp: 4, qtyDp: 1 },
  { id: 18, symbol: "NEAR-USDT", binance: "NEARUSDT", base: "NEAR", quote: "USDT", tick: 0.001, lot: 0.1, maxLev: 20, pxDp: 3, qtyDp: 1 },
  { id: 19, symbol: "AAVE-USDT", binance: "AAVEUSDT", base: "AAVE", quote: "USDT", tick: 0.01, lot: 0.01, maxLev: 15, pxDp: 2, qtyDp: 2 },
  { id: 20, symbol: "INJ-USDT", binance: "INJUSDT", base: "INJ", quote: "USDT", tick: 0.001, lot: 0.1, maxLev: 20, pxDp: 3, qtyDp: 1 },
];

const bySym = new Map(MARKETS.map((m) => [m.symbol, m]));
const byBin = new Map(MARKETS.map((m) => [m.binance, m]));

export function marketBySymbol(symbol: string): Market {
  return bySym.get(symbol) ?? MARKETS[0]!;
}

export function marketByBinance(binance: string): Market | undefined {
  return byBin.get(binance);
}

export const ALLOWED_BINANCE = new Set(MARKETS.map((m) => m.binance));

export function roundTo(n: number, step: number): number {
  if (step <= 0) return n;
  const k = Math.round(n / step);
  const dp = Math.max(0, Math.ceil(-Math.log10(step)));
  return Number((k * step).toFixed(Math.min(dp, 8)));
}
