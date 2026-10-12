import type { Candle, OpenOrder, Position, PriceLevel, TradeHistoryItem, TradeTick } from "./types";

/** Generate realistic-looking candles for the chart when live feed is offline */
export function generateCandles(
  basePrice = 95000,
  count = 200,
  intervalSec = 60
): Candle[] {
  const now = Math.floor(Date.now() / 1000);
  const candles: Candle[] = [];
  let price = basePrice;

  for (let i = count; i > 0; i--) {
    const open = price;
    const change = (Math.random() - 0.48) * basePrice * 0.004;
    const close = Math.max(100, open + change);
    const high = Math.max(open, close) * (1 + Math.random() * 0.0015);
    const low = Math.min(open, close) * (1 - Math.random() * 0.0015);
    const volume = 10 + Math.random() * 80;
    candles.push({
      time: now - i * intervalSec,
      open,
      high,
      low,
      close,
      volume,
    });
    price = close;
  }
  return candles;
}

export function generateOrderBook(
  mid = 95000,
  levels = 15
): { bids: PriceLevel[]; asks: PriceLevel[] } {
  const bids: PriceLevel[] = [];
  const asks: PriceLevel[] = [];
  let bidTotal = 0;
  let askTotal = 0;

  for (let i = 0; i < levels; i++) {
    const bidQty = 0.05 + Math.random() * 2.5;
    const askQty = 0.05 + Math.random() * 2.5;
    bidTotal += bidQty;
    askTotal += askQty;
    bids.push({
      price: mid - (i + 1) * (0.5 + Math.random()),
      quantity: bidQty,
      total: bidTotal,
    });
    asks.push({
      price: mid + (i + 1) * (0.5 + Math.random()),
      quantity: askQty,
      total: askTotal,
    });
  }
  return { bids, asks };
}

export function generateRecentTrades(mid = 95000, count = 30): TradeTick[] {
  const trades: TradeTick[] = [];
  const now = Date.now() * 10000;
  for (let i = 0; i < count; i++) {
    const isBuyerMaker = Math.random() > 0.5;
    trades.push({
      marketId: 1,
      tradeId: 100000 + i,
      price: mid + (Math.random() - 0.5) * 20,
      quantity: 0.001 + Math.random() * 0.5,
      isBuyerMaker,
      timestampUtcTicks: now - i * 1_000_0000,
    });
  }
  return trades;
}

export const DEMO_ORDERS: OpenOrder[] = [
  {
    orderId: 1001,
    clientOrderId: 1,
    marketId: 1,
    symbol: "BTC-USD",
    side: "Buy",
    type: "Limit",
    price: 94000,
    quantity: 0.25,
    filledQuantity: 0,
    remainingQuantity: 0.25,
    status: "New",
    createdAt: Date.now() - 120000,
  },
  {
    orderId: 1002,
    clientOrderId: 2,
    marketId: 1,
    symbol: "BTC-USD",
    side: "Sell",
    type: "Limit",
    price: 98000,
    quantity: 0.1,
    filledQuantity: 0.04,
    remainingQuantity: 0.06,
    status: "PartiallyFilled",
    createdAt: Date.now() - 300000,
  },
];

export const DEMO_POSITIONS: Position[] = [
  {
    marketId: 1,
    symbol: "BTC-USD",
    side: "Long",
    quantity: 0.5,
    entryPrice: 93200,
    markPrice: 95120,
    unrealizedPnl: 960,
    leverage: 10,
    marginMode: "Cross",
    liquidationPrice: 84500,
    margin: 4660,
  },
];

export const DEMO_HISTORY: TradeHistoryItem[] = [
  {
    tradeId: 9001,
    symbol: "BTC-USD",
    side: "Buy",
    price: 93200,
    quantity: 0.5,
    fee: 2.33,
    timestamp: Date.now() - 3600000,
  },
  {
    tradeId: 9002,
    symbol: "ETH-USD",
    side: "Sell",
    price: 3420,
    quantity: 2,
    fee: 1.71,
    timestamp: Date.now() - 7200000,
  },
];
