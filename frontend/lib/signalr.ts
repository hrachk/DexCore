import * as signalR from "@microsoft/signalr";
import type {
  MarkPrice,
  OrderBookDelta,
  OrderBookSnapshot,
  Ticker,
  TradeTick,
} from "./types";

const HUB_URL =
  process.env.NEXT_PUBLIC_SIGNALR_URL ?? "http://localhost:5000/hubs/marketdata";

export type MarketDataHandlers = {
  onSnapshot?: (s: OrderBookSnapshot) => void;
  onDelta?: (d: OrderBookDelta) => void;
  onTrade?: (t: TradeTick) => void;
  onTicker?: (t: Ticker) => void;
  onMarkPrice?: (m: MarkPrice) => void;
  onError?: (msg: string) => void;
  onConnected?: () => void;
  onDisconnected?: () => void;
};

export function createMarketDataConnection(handlers: MarketDataHandlers) {
  const connection = new signalR.HubConnectionBuilder()
    .withUrl(HUB_URL)
    .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
    .configureLogging(signalR.LogLevel.Information)
    .build();

  connection.on("OrderBookSnapshot", (data: OrderBookSnapshot) => {
    handlers.onSnapshot?.(normalizeSnapshot(data));
  });
  connection.on("OrderBookDelta", (data: OrderBookDelta) => {
    handlers.onDelta?.(normalizeDelta(data));
  });
  connection.on("Trade", (data: TradeTick) => handlers.onTrade?.(data));
  connection.on("Ticker", (data: Ticker) => handlers.onTicker?.(data));
  connection.on("MarkPrice", (data: MarkPrice) => handlers.onMarkPrice?.(data));
  connection.on("Error", (msg: string) => handlers.onError?.(msg));
  connection.on("Subscribed", () => handlers.onConnected?.());

  connection.onreconnected(() => handlers.onConnected?.());
  connection.onclose(() => handlers.onDisconnected?.());

  return {
    connection,
    async start(marketIds: number[], depth = 15) {
      try {
        await connection.start();
        await connection.invoke(
          "SubscribeToMarketData",
          marketIds,
          true,
          true,
          true,
          depth
        );
        handlers.onConnected?.();
      } catch (err) {
        console.warn("[SignalR] connect failed, using mock data:", err);
        handlers.onError?.(
          "Live feed offline — showing simulated market data"
        );
        handlers.onDisconnected?.();
      }
    },
    async stop() {
      try {
        await connection.invoke("Unsubscribe");
        await connection.stop();
      } catch {
        /* ignore */
      }
    },
  };
}

function normalizeSnapshot(s: OrderBookSnapshot): OrderBookSnapshot {
  return {
    ...s,
    bids: (s.bids ?? []).map((b) => ({
      price: Number(b.price),
      quantity: Number(b.quantity),
    })),
    asks: (s.asks ?? []).map((a) => ({
      price: Number(a.price),
      quantity: Number(a.quantity),
    })),
  };
}

function normalizeDelta(d: OrderBookDelta): OrderBookDelta {
  return {
    ...d,
    updates: (d.updates ?? []).map((u) => ({
      isBid: u.isBid,
      price: Number(u.price),
      quantity: Number(u.quantity),
    })),
  };
}

export async function placeOrderRest(body: {
  accountId: number;
  clientOrderId: number;
  marketId: number;
  side: string;
  type: string;
  timeInForce: string;
  price: number;
  quantity: number;
  reduceOnly: boolean;
  postOnly: boolean;
  apiKey?: string;
}) {
  const base =
    process.env.NEXT_PUBLIC_API_URL ?? "http://localhost:5000";
  const res = await fetch(`${base}/api/v1/order`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
  const data = await res.json();
  if (!res.ok) throw new Error(data.error ?? "Order rejected");
  return data;
}
