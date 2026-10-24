import { useQuery } from "@tanstack/react-query";
import { loadBook, loadKlines, loadTickers, loadTrades } from "@/lib/exchange/market.functions";
import { MARKETS, marketByBinance } from "@/lib/exchange/markets";
import type { Book, Ticker } from "@/lib/exchange/types";

export function useTickers() {
  return useQuery({
    queryKey: ["tickers"],
    queryFn: () => loadTickers(),
    refetchInterval: 4000,
  });
}

export function marksFrom(rows: Ticker[] | undefined): Record<string, number> {
  const marks: Record<string, number> = {};
  for (const t of rows ?? []) {
    const m = marketByBinance(t.symbol);
    if (m && t.last > 0) marks[m.symbol] = t.last;
  }
  return marks;
}

export function tickerFor(rows: Ticker[] | undefined, binance: string): Ticker | undefined {
  return rows?.find((t) => t.symbol === binance);
}

export function useBook(binance: string) {
  return useQuery({
    queryKey: ["book", binance],
    queryFn: () => loadBook({ data: { symbol: binance } }),
    refetchInterval: 1200,
  });
}

export function useTape(binance: string) {
  return useQuery({
    queryKey: ["tape", binance],
    queryFn: () => loadTrades({ data: { symbol: binance } }),
    refetchInterval: 1500,
  });
}

export function useCandles(binance: string, interval: string) {
  return useQuery({
    queryKey: ["klines", binance, interval],
    queryFn: () => loadKlines({ data: { symbol: binance, interval } }),
    refetchInterval: 8000,
  });
}

export const EMPTY_BOOK: Book = { bids: [], asks: [] };

export { MARKETS };
