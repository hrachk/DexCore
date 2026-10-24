export type Side = "buy" | "sell";
export type OrdType = "limit" | "market";
export type MarginMode = "cross" | "isolated";
export type OrderStatus = "open" | "partial" | "filled" | "cancelled" | "rejected";

export type Level = { price: number; qty: number };

export type Book = { bids: Level[]; asks: Level[] };

export type Ticker = {
  symbol: string;
  last: number;
  bid: number;
  ask: number;
  changePct: number;
  high: number;
  low: number;
  quoteVol: number;
};

export type Candle = {
  time: number;
  open: number;
  high: number;
  low: number;
  close: number;
  volume: number;
};

export type TapeTrade = {
  id: number;
  price: number;
  qty: number;
  buyerMaker: boolean;
  time: number;
};

export type WorkingOrder = {
  id: string;
  symbol: string;
  side: Side;
  type: OrdType;
  price: number;
  qty: number;
  filled: number;
  leverage: number;
  marginMode: MarginMode;
  reduceOnly: boolean;
  postOnly: boolean;
  status: OrderStatus;
  marginLocked: number;
  created: number;
};

export type Position = {
  symbol: string;
  side: "long" | "short";
  qty: number;
  entry: number;
  leverage: number;
  marginMode: MarginMode;
  margin: number;
};

export type FillRow = {
  id: string;
  symbol: string;
  side: Side;
  price: number;
  qty: number;
  fee: number;
  pnl: number;
  time: number;
  note: string;
};

export type Ledger = {
  wallet: number;
  orders: WorkingOrder[];
  positions: Position[];
  fills: FillRow[];
};

export type PlaceInput = {
  symbol: string;
  side: Side;
  type: OrdType;
  price: number;
  qty: number;
  leverage: number;
  marginMode: MarginMode;
  reduceOnly: boolean;
  postOnly: boolean;
  mark: number;
};

export const TAKER_FEE = 0.0004;
export const STARTING_WALLET = 25_000;
