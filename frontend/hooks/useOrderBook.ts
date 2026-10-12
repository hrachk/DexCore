"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { createMarketDataConnection } from "@/lib/signalr";
import { generateOrderBook, generateRecentTrades } from "@/lib/mockData";
import type {
  MarkPrice,
  OrderBookSnapshot,
  PriceLevel,
  Ticker,
  TradeTick,
} from "@/lib/types";

function applyDelta(
  levels: PriceLevel[],
  updates: { price: number; quantity: number }[],
  descending: boolean
): PriceLevel[] {
  const map = new Map(levels.map((l) => [l.price, l.quantity]));
  for (const u of updates) {
    if (u.quantity <= 0) map.delete(u.price);
    else map.set(u.price, u.quantity);
  }
  const arr = Array.from(map.entries()).map(([price, quantity]) => ({
    price,
    quantity,
  }));
  arr.sort((a, b) => (descending ? b.price - a.price : a.price - b.price));
  let total = 0;
  return arr.map((l) => {
    total += l.quantity;
    return { ...l, total };
  });
}

export function useOrderBook(marketId: number, midPrice = 95000) {
  const [bids, setBids] = useState<PriceLevel[]>([]);
  const [asks, setAsks] = useState<PriceLevel[]>([]);
  const [trades, setTrades] = useState<TradeTick[]>([]);
  const [ticker, setTicker] = useState<Ticker | null>(null);
  const [mark, setMark] = useState<MarkPrice | null>(null);
  const [connected, setConnected] = useState(false);
  const [statusMsg, setStatusMsg] = useState("Connecting…");
  const mockTimer = useRef<ReturnType<typeof setInterval> | null>(null);

  const startMock = useCallback(() => {
    if (mockTimer.current) return;
    const seed = generateOrderBook(midPrice);
    setBids(seed.bids);
    setAsks(seed.asks);
    setTrades(generateRecentTrades(midPrice));
    setTicker({
      marketId,
      symbol: "BTC-USD",
      lastPrice: midPrice,
      bestBid: seed.bids[0]?.price ?? midPrice,
      bestAsk: seed.asks[0]?.price ?? midPrice,
      volume24h: 12500,
      priceChange24h: 2.34,
      timestampUtcTicks: Date.now(),
    });
    setMark({
      marketId,
      markPrice: midPrice,
      indexPrice: midPrice * 1.0001,
      fundingRate: 0.0001,
      nextFundingTimeUtcTicks: Date.now() + 3600_000,
      timestampUtcTicks: Date.now(),
    });
    setStatusMsg("Simulated feed (API offline)");
    setConnected(false);

    mockTimer.current = setInterval(() => {
      setBids((prev) => {
        const next = prev.map((l) => ({
          ...l,
          quantity: Math.max(0.01, l.quantity + (Math.random() - 0.5) * 0.3),
        }));
        let t = 0;
        return next.map((l) => {
          t += l.quantity;
          return { ...l, total: t };
        });
      });
      setAsks((prev) => {
        const next = prev.map((l) => ({
          ...l,
          quantity: Math.max(0.01, l.quantity + (Math.random() - 0.5) * 0.3),
        }));
        let t = 0;
        return next.map((l) => {
          t += l.quantity;
          return { ...l, total: t };
        });
      });
      setTrades((prev) => {
        const price =
          midPrice + (Math.random() - 0.5) * 30;
        const tick: TradeTick = {
          marketId,
          tradeId: Date.now(),
          price,
          quantity: 0.01 + Math.random() * 0.4,
          isBuyerMaker: Math.random() > 0.5,
          timestampUtcTicks: Date.now() * 10000,
        };
        return [tick, ...prev].slice(0, 40);
      });
      setTicker((t) =>
        t
          ? {
              ...t,
              lastPrice: midPrice + (Math.random() - 0.5) * 20,
              priceChange24h: t.priceChange24h + (Math.random() - 0.5) * 0.01,
            }
          : t
      );
    }, 1200);
  }, [marketId, midPrice]);

  useEffect(() => {
    const conn = createMarketDataConnection({
      onSnapshot: (s: OrderBookSnapshot) => {
        let bt = 0;
        let at = 0;
        setBids(
          s.bids.map((b) => {
            bt += b.quantity;
            return { ...b, total: bt };
          })
        );
        setAsks(
          s.asks.map((a) => {
            at += a.quantity;
            return { ...a, total: at };
          })
        );
        if (mockTimer.current) {
          clearInterval(mockTimer.current);
          mockTimer.current = null;
        }
        setConnected(true);
        setStatusMsg("Live");
      },
      onDelta: (d) => {
        const bidUpdates = d.updates.filter((u) => u.isBid);
        const askUpdates = d.updates.filter((u) => !u.isBid);
        setBids((prev) => applyDelta(prev, bidUpdates, true));
        setAsks((prev) => applyDelta(prev, askUpdates, false));
      },
      onTrade: (t) => setTrades((prev) => [t, ...prev].slice(0, 50)),
      onTicker: setTicker,
      onMarkPrice: setMark,
      onConnected: () => {
        setConnected(true);
        setStatusMsg("Live");
      },
      onDisconnected: () => {
        setConnected(false);
        startMock();
      },
      onError: (msg) => {
        setStatusMsg(msg);
        startMock();
      },
    });

    conn.start([marketId], 15);
    // If hub doesn't respond quickly, fall back to mock
    const fallback = setTimeout(() => {
      if (!mockTimer.current) startMock();
    }, 2500);

    return () => {
      clearTimeout(fallback);
      if (mockTimer.current) clearInterval(mockTimer.current);
      conn.stop();
    };
  }, [marketId, startMock]);

  const spread =
    asks[0] && bids[0] ? asks[0].price - bids[0].price : 0;
  const mid =
    asks[0] && bids[0]
      ? (asks[0].price + bids[0].price) / 2
      : midPrice;

  return {
    bids,
    asks,
    trades,
    ticker,
    mark,
    connected,
    statusMsg,
    spread,
    mid,
  };
}
