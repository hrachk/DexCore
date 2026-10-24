import { createServerFn } from "@tanstack/react-start";
import { ALLOWED_BINANCE } from "./markets";
import type { Candle, Level, TapeTrade, Ticker } from "./types";

const BINANCE = "https://data-api.binance.vision";

function assertSymbol(symbol: string): string {
  const s = symbol.toUpperCase();
  if (!ALLOWED_BINANCE.has(s)) throw new Error("Unknown market");
  return s;
}

async function binance<T>(path: string): Promise<T> {
  const res = await fetch(`${BINANCE}${path}`, {
    headers: { accept: "application/json" },
  });
  if (!res.ok) throw new Error(`Market data ${res.status}`);
  return (await res.json()) as T;
}

export const loadTickers = createServerFn({ method: "GET" }).handler(async () => {
  const rows = await binance<
    {
      symbol: string;
      lastPrice: string;
      bidPrice: string;
      askPrice: string;
      priceChangePercent: string;
      highPrice: string;
      lowPrice: string;
      quoteVolume: string;
    }[]
  >("/api/v3/ticker/24hr");
  const out: Ticker[] = [];
  for (const r of rows) {
    if (!ALLOWED_BINANCE.has(r.symbol)) continue;
    out.push({
      symbol: r.symbol,
      last: Number(r.lastPrice),
      bid: Number(r.bidPrice),
      ask: Number(r.askPrice),
      changePct: Number(r.priceChangePercent),
      high: Number(r.highPrice),
      low: Number(r.lowPrice),
      quoteVol: Number(r.quoteVolume),
    });
  }
  return out;
});

export const loadBook = createServerFn({ method: "GET" })
  .inputValidator((d: { symbol: string }) => ({ symbol: assertSymbol(d.symbol) }))
  .handler(async ({ data }) => {
    const j = await binance<{ bids: [string, string][]; asks: [string, string][] }>(
      `/api/v3/depth?symbol=${data.symbol}&limit=20`,
    );
    const map = (rows: [string, string][]): Level[] =>
      rows.slice(0, 14).map(([p, q]) => ({ price: Number(p), qty: Number(q) }));
    return { bids: map(j.bids), asks: map(j.asks) };
  });

export const loadTrades = createServerFn({ method: "GET" })
  .inputValidator((d: { symbol: string }) => ({ symbol: assertSymbol(d.symbol) }))
  .handler(async ({ data }) => {
    const rows = await binance<
      { id: number; price: string; qty: string; time: number; isBuyerMaker: boolean }[]
    >(`/api/v3/trades?symbol=${data.symbol}&limit=30`);
    const out: TapeTrade[] = rows
      .map((r) => ({
        id: r.id,
        price: Number(r.price),
        qty: Number(r.qty),
        buyerMaker: r.isBuyerMaker,
        time: r.time,
      }))
      .reverse();
    return out;
  });

const INTERVALS = new Set(["1m", "5m", "15m", "1h", "4h", "1d"]);

export const loadKlines = createServerFn({ method: "GET" })
  .inputValidator((d: { symbol: string; interval: string }) => {
    const symbol = assertSymbol(d.symbol);
    const interval = INTERVALS.has(d.interval) ? d.interval : "1m";
    return { symbol, interval };
  })
  .handler(async ({ data }) => {
    const rows = await binance<(number | string)[][]>(
      `/api/v3/klines?symbol=${data.symbol}&interval=${data.interval}&limit=240`,
    );
    const candles: Candle[] = rows.map((r) => ({
      time: Math.floor(Number(r[0]) / 1000),
      open: Number(r[1]),
      high: Number(r[2]),
      low: Number(r[3]),
      close: Number(r[4]),
      volume: Number(r[5]),
    }));
    return candles;
  });
