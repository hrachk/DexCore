export type OrderSide = "Buy" | "Sell";
export type OrderType = "Limit" | "Market" | "StopLimit" | "StopMarket";
export type TimeInForce = "GTC" | "IOC" | "FOK";
export type MarginMode = "Cross" | "Isolated";
export type OrderStatus =
  | "PendingNew"
  | "New"
  | "PartiallyFilled"
  | "Filled"
  | "PendingCancel"
  | "Cancelled"
  | "Rejected";

export interface PriceLevel {
  price: number;
  quantity: number;
  total?: number;
}

export interface OrderBookSnapshot {
  marketId: number;
  symbol: string;
  sequence: number;
  timestampUtcTicks: number;
  bids: PriceLevel[];
  asks: PriceLevel[];
}

export interface OrderBookDelta {
  marketId: number;
  sequence: number;
  timestampUtcTicks: number;
  updates: {
    isBid: boolean;
    price: number;
    quantity: number;
  }[];
}

export interface TradeTick {
  marketId: number;
  tradeId: number;
  price: number;
  quantity: number;
  isBuyerMaker: boolean;
  timestampUtcTicks: number;
}

export interface Ticker {
  marketId: number;
  symbol: string;
  lastPrice: number;
  bestBid: number;
  bestAsk: number;
  volume24h: number;
  priceChange24h: number;
  timestampUtcTicks: number;
}

export interface MarkPrice {
  marketId: number;
  markPrice: number;
  indexPrice: number;
  fundingRate: number;
  nextFundingTimeUtcTicks: number;
  timestampUtcTicks: number;
}

export interface OpenOrder {
  orderId: number;
  clientOrderId: number;
  marketId: number;
  symbol: string;
  side: OrderSide;
  type: OrderType;
  price: number;
  quantity: number;
  filledQuantity: number;
  remainingQuantity: number;
  status: OrderStatus;
  createdAt: number;
}

export interface Position {
  marketId: number;
  symbol: string;
  side: "Long" | "Short" | "Flat";
  quantity: number;
  entryPrice: number;
  markPrice: number;
  unrealizedPnl: number;
  leverage: number;
  marginMode: MarginMode;
  liquidationPrice: number;
  margin: number;
}

export interface TradeHistoryItem {
  tradeId: number;
  symbol: string;
  side: OrderSide;
  price: number;
  quantity: number;
  fee: number;
  timestamp: number;
}

export interface Candle {
  time: number;
  open: number;
  high: number;
  low: number;
  close: number;
  volume: number;
}

export interface MarketInfo {
  marketId: number;
  symbol: string;
  base: string;
  quote: string;
  tickSize: number;
  lotSize: number;
  maxLeverage: number;
}
