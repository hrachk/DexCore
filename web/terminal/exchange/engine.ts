import { marketBySymbol, roundTo } from "./markets";
import type {
  Book,
  FillRow,
  Ledger,
  Level,
  PlaceInput,
  Position,
  Side,
  WorkingOrder,
} from "./types";
import { STARTING_WALLET, TAKER_FEE } from "./types";

export function emptyLedger(): Ledger {
  return { wallet: STARTING_WALLET, orders: [], positions: [], fills: [] };
}

export function liqPrice(entry: number, side: "long" | "short", leverage: number): number {
  const buf = 0.5 / Math.max(1, leverage);
  return side === "long" ? entry * (1 - buf) : entry * (1 + buf);
}

export function upnl(p: Position, mark: number): number {
  if (mark <= 0 || p.qty <= 0) return 0;
  const dir = p.side === "long" ? 1 : -1;
  return (mark - p.entry) * p.qty * dir;
}

export function usedMargin(l: Ledger): number {
  const pos = l.positions.reduce((s, p) => s + p.margin, 0);
  const ord = l.orders
    .filter((o) => o.status === "open" || o.status === "partial")
    .reduce((s, o) => s + o.marginLocked, 0);
  return pos + ord;
}

export function equity(l: Ledger, marks: Record<string, number>): number {
  const u = l.positions.reduce((s, p) => s + upnl(p, marks[p.symbol] ?? p.entry), 0);
  return l.wallet + u;
}

export function available(l: Ledger, marks: Record<string, number>): number {
  return equity(l, marks) - usedMargin(l);
}

function uid(prefix: string): string {
  return `${prefix}-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 7)}`;
}

function clone(l: Ledger): Ledger {
  return {
    wallet: l.wallet,
    orders: l.orders.map((o) => ({ ...o })),
    positions: l.positions.map((p) => ({ ...p })),
    fills: l.fills.slice(),
  };
}

function pushFill(l: Ledger, row: Omit<FillRow, "id" | "time"> & { time?: number }) {
  l.fills.unshift({
    id: uid("f"),
    time: row.time ?? Date.now(),
    ...row,
  });
  if (l.fills.length > 200) l.fills.length = 200;
}

/** Apply a signed qty at price. Positive qty = buy. Returns realized pnl. */
function applyFill(l: Ledger, symbol: string, side: Side, qty: number, price: number, leverage: number, marginMode: Position["marginMode"]): number {
  const mkt = marketBySymbol(symbol);
  const q = roundTo(qty, mkt.lot);
  if (q <= 0) return 0;
  const fee = price * q * TAKER_FEE;
  l.wallet -= fee;
  let realized = 0;
  const pos = l.positions.find((p) => p.symbol === symbol);
  const openingLong = side === "buy";

  if (!pos) {
    const margin = (price * q) / leverage;
    l.positions.push({
      symbol,
      side: openingLong ? "long" : "short",
      qty: q,
      entry: price,
      leverage,
      marginMode,
      margin,
    });
    pushFill(l, { symbol, side, price, qty: q, fee, pnl: 0, note: "open" });
    return 0;
  }

  const same =
    (pos.side === "long" && side === "buy") || (pos.side === "short" && side === "sell");

  if (same) {
    const notional = pos.entry * pos.qty + price * q;
    pos.qty = roundTo(pos.qty + q, mkt.lot);
    pos.entry = pos.qty > 0 ? notional / pos.qty : price;
    pos.leverage = leverage;
    pos.marginMode = marginMode;
    pos.margin += (price * q) / leverage;
    pushFill(l, { symbol, side, price, qty: q, fee, pnl: 0, note: "add" });
    return 0;
  }

  const closeQty = Math.min(pos.qty, q);
  const dir = pos.side === "long" ? 1 : -1;
  realized = (price - pos.entry) * closeQty * dir;
  l.wallet += realized;
  const frac = pos.qty > 0 ? closeQty / pos.qty : 1;
  pos.margin *= 1 - frac;
  pos.qty = roundTo(pos.qty - closeQty, mkt.lot);
  pushFill(l, { symbol, side, price, qty: closeQty, fee, pnl: realized, note: pos.qty <= 0 ? "close" : "reduce" });

  if (pos.qty <= mkt.lot / 2) {
    l.positions = l.positions.filter((p) => p.symbol !== symbol);
  }

  const leftover = roundTo(q - closeQty, mkt.lot);
  if (leftover > 0) {
    l.positions.push({
      symbol,
      side: side === "buy" ? "long" : "short",
      qty: leftover,
      entry: price,
      leverage,
      marginMode,
      margin: (price * leftover) / leverage,
    });
    pushFill(l, { symbol, side, price, qty: leftover, fee: 0, pnl: 0, note: "flip" });
  }
  return realized;
}

function walk(levels: Level[], qty: number, limit: number | null, side: Side): { fills: { price: number; qty: number }[]; left: number } {
  const fills: { price: number; qty: number }[] = [];
  let left = qty;
  for (const lv of levels) {
    if (left <= 0) break;
    if (limit !== null) {
      if (side === "buy" && lv.price > limit) break;
      if (side === "sell" && lv.price < limit) break;
    }
    const take = Math.min(left, lv.qty);
    if (take <= 0) continue;
    fills.push({ price: lv.price, qty: take });
    left -= take;
  }
  return { fills, left };
}

function marginFor(price: number, qty: number, leverage: number): number {
  return (price * qty) / Math.max(1, leverage);
}

export type PlaceResult = { ledger: Ledger; error?: string; orderId?: string };

export function placeOrder(ledger: Ledger, input: PlaceInput, book: Book, marks: Record<string, number>): PlaceResult {
  const l = clone(ledger);
  const mkt = marketBySymbol(input.symbol);
  const qty = roundTo(input.qty, mkt.lot);
  if (qty <= 0) return { ledger, error: "Enter a size above the lot." };
  const leverage = Math.min(mkt.maxLev, Math.max(1, Math.floor(input.leverage)));
  const mark = input.mark > 0 ? input.mark : marks[input.symbol] ?? 0;
  const px =
    input.type === "market"
      ? mark
      : roundTo(input.price, mkt.tick);
  if (px <= 0) return { ledger, error: "Enter a price." };

  const pos = l.positions.find((p) => p.symbol === input.symbol);
  if (input.reduceOnly) {
    if (!pos) return { ledger, error: "Reduce-only needs an open position." };
    const closing =
      (pos.side === "long" && input.side === "sell") || (pos.side === "short" && input.side === "buy");
    if (!closing) return { ledger, error: "Reduce-only must close the position." };
    if (qty > pos.qty + 1e-12) return { ledger, error: "Size is larger than the position." };
  }

  const opp = input.side === "buy" ? book.asks : book.bids;
  const bestOpp = opp[0]?.price ?? 0;
  const wouldCross =
    input.type === "market" ||
    (bestOpp > 0 && (input.side === "buy" ? px >= bestOpp : px <= bestOpp));

  if (input.postOnly && wouldCross) return { ledger, error: "Post-only would take liquidity." };

  const refPx = input.type === "market" ? (bestOpp || mark) : px;
  const need = input.reduceOnly ? 0 : marginFor(refPx, qty, leverage);
  const free = available(l, { ...marks, [input.symbol]: mark || refPx });
  if (need > free + 1e-6) {
    return { ledger, error: `Need ${need.toFixed(2)} USDT margin, ${Math.max(0, free).toFixed(2)} free.` };
  }

  const order: WorkingOrder = {
    id: uid("o"),
    symbol: input.symbol,
    side: input.side,
    type: input.type,
    price: px,
    qty,
    filled: 0,
    leverage,
    marginMode: input.marginMode,
    reduceOnly: input.reduceOnly,
    postOnly: input.postOnly,
    status: "open",
    marginLocked: 0,
    created: Date.now(),
  };

  if (wouldCross || input.type === "market") {
    const limit = input.type === "market" ? null : px;
    const { fills, left } = walk(opp, qty, limit, input.side);
    if (fills.length === 0) {
      if (input.type === "market") return { ledger, error: "No liquidity in the book." };
    }
    for (const f of fills) {
      applyFill(l, input.symbol, input.side, f.qty, f.price, leverage, input.marginMode);
      order.filled = roundTo(order.filled + f.qty, mkt.lot);
    }
    const residual = roundTo(Math.max(0, left), mkt.lot);
    if (residual <= mkt.lot / 2 || input.type === "market") {
      order.status = order.filled > 0 ? "filled" : "rejected";
      order.marginLocked = 0;
      if (order.status === "rejected") return { ledger, error: "Nothing filled." };
      l.orders.unshift(order);
      return { ledger: l, orderId: order.id };
    }
    order.qty = qty;
    order.status = "partial";
    order.marginLocked = input.reduceOnly ? 0 : marginFor(px, residual, leverage);
    l.orders.unshift(order);
    return { ledger: l, orderId: order.id };
  }

  order.marginLocked = need;
  order.status = "open";
  l.orders.unshift(order);
  return { ledger: l, orderId: order.id };
}

export function cancelOrder(ledger: Ledger, id: string): Ledger {
  const l = clone(ledger);
  const o = l.orders.find((x) => x.id === id);
  if (!o || (o.status !== "open" && o.status !== "partial")) return ledger;
  o.status = "cancelled";
  o.marginLocked = 0;
  return l;
}

export function closePosition(ledger: Ledger, symbol: string, book: Book, mark: number, marks: Record<string, number>): PlaceResult {
  const pos = ledger.positions.find((p) => p.symbol === symbol);
  if (!pos) return { ledger, error: "No position." };
  return placeOrder(
    ledger,
    {
      symbol,
      side: pos.side === "long" ? "sell" : "buy",
      type: "market",
      price: mark,
      qty: pos.qty,
      leverage: pos.leverage,
      marginMode: pos.marginMode,
      reduceOnly: true,
      postOnly: false,
      mark,
    },
    book,
    marks,
  );
}

/** Resting limits cross the live book; positions past liq are flattened. */
export function onBook(ledger: Ledger, symbol: string, book: Book, mark: number, marks: Record<string, number>): Ledger {
  let l = clone(ledger);
  const mkt = marketBySymbol(symbol);
  const working = l.orders.filter(
    (o) => o.symbol === symbol && (o.status === "open" || o.status === "partial") && o.type === "limit",
  );
  for (const o of working) {
    const opp = o.side === "buy" ? book.asks : book.bids;
    const best = opp[0]?.price ?? 0;
    if (best <= 0) continue;
    const cross = o.side === "buy" ? best <= o.price : best >= o.price;
    if (!cross) continue;
    const residual = roundTo(o.qty - o.filled, mkt.lot);
    if (residual <= 0) {
      o.status = "filled";
      o.marginLocked = 0;
      continue;
    }
    const { fills, left } = walk(opp, residual, o.price, o.side);
    for (const f of fills) {
      applyFill(l, symbol, o.side, f.qty, f.price, o.leverage, o.marginMode);
      o.filled = roundTo(o.filled + f.qty, mkt.lot);
    }
    const rem = roundTo(Math.max(0, left), mkt.lot);
    if (rem <= mkt.lot / 2) {
      o.status = "filled";
      o.marginLocked = 0;
    } else {
      o.status = "partial";
      o.marginLocked = o.reduceOnly ? 0 : marginFor(o.price, rem, o.leverage);
    }
  }

  const pos = l.positions.find((p) => p.symbol === symbol);
  if (pos && mark > 0) {
    const liq = liqPrice(pos.entry, pos.side, pos.leverage);
    const hit = pos.side === "long" ? mark <= liq : mark >= liq;
    if (hit) {
      const side: Side = pos.side === "long" ? "sell" : "buy";
      const q = pos.qty;
      applyFill(l, symbol, side, q, mark, pos.leverage, pos.marginMode);
      const last = l.fills[0];
      if (last) last.note = "liquidation";
    }
  }

  if (l.wallet < 0) l.wallet = 0;
  return l;
}

export function deposit(ledger: Ledger, amount: number): PlaceResult {
  if (!(amount > 0)) return { ledger, error: "Amount must be positive." };
  const l = clone(ledger);
  l.wallet += amount;
  pushFill(l, { symbol: "USDT", side: "buy", price: 1, qty: amount, fee: 0, pnl: 0, note: "deposit" });
  return { ledger: l };
}

export function withdraw(ledger: Ledger, amount: number, marks: Record<string, number>): PlaceResult {
  if (!(amount > 0)) return { ledger, error: "Amount must be positive." };
  const free = Math.max(0, ledger.wallet - usedMargin(ledger));
  // withdrawals come from cash not locked in margin; ignore negative upnl beyond wallet
  const cashFree = Math.min(free, Math.max(0, available(ledger, marks)));
  if (amount > cashFree + 1e-6) return { ledger, error: `Max withdraw ${cashFree.toFixed(2)} USDT.` };
  const l = clone(ledger);
  l.wallet -= amount;
  pushFill(l, { symbol: "USDT", side: "sell", price: 1, qty: amount, fee: 0, pnl: 0, note: "withdraw" });
  return { ledger: l };
}
