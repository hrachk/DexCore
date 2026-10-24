import { create } from "zustand";
import { persist } from "zustand/middleware";
import {
  available,
  cancelOrder,
  closePosition,
  deposit,
  emptyLedger,
  equity,
  onBook,
  placeOrder,
  usedMargin,
  withdraw,
} from "./engine";
import type { Book, Ledger, PlaceInput } from "./types";

type Marks = Record<string, number>;

type DeskState = {
  ledger: Ledger;
  symbol: string;
  hydrated: boolean;
  setHydrated: () => void;
  setSymbol: (symbol: string) => void;
  submit: (input: PlaceInput, book: Book, marks: Marks) => string | null;
  cancel: (id: string) => void;
  close: (symbol: string, book: Book, mark: number, marks: Marks) => string | null;
  tick: (symbol: string, book: Book, mark: number, marks: Marks) => void;
  fund: (amount: number) => string | null;
  cashOut: (amount: number, marks: Marks) => string | null;
  reset: () => void;
};

export const useDesk = create<DeskState>()(
  persist(
    (set, get) => ({
      ledger: emptyLedger(),
      symbol: "BTC-USDT",
      hydrated: false,
      setHydrated: () => set({ hydrated: true }),
      setSymbol: (symbol) => set({ symbol }),
      submit: (input, book, marks) => {
        const res = placeOrder(get().ledger, input, book, marks);
        if (res.error || !res.ledger) return res.error ?? "Rejected";
        set({ ledger: res.ledger });
        return null;
      },
      cancel: (id) => set({ ledger: cancelOrder(get().ledger, id) }),
      close: (symbol, book, mark, marks) => {
        const res = closePosition(get().ledger, symbol, book, mark, marks);
        if (res.error) return res.error;
        set({ ledger: res.ledger });
        return null;
      },
      tick: (symbol, book, mark, marks) => {
        const prev = get().ledger;
        const next = onBook(prev, symbol, book, mark, marks);
        const sig = (l: typeof prev) =>
          `${l.wallet}|${l.fills.length}|${l.positions.map((p) => `${p.symbol}:${p.qty}:${p.entry}`).join(",")}|${l.orders.map((o) => `${o.id}:${o.status}:${o.filled}:${o.marginLocked}`).join(",")}`;
        if (sig(next) !== sig(prev)) set({ ledger: next });
      },
      fund: (amount) => {
        const res = deposit(get().ledger, amount);
        if (res.error) return res.error;
        set({ ledger: res.ledger });
        return null;
      },
      cashOut: (amount, marks) => {
        const res = withdraw(get().ledger, amount, marks);
        if (res.error) return res.error;
        set({ ledger: res.ledger });
        return null;
      },
      reset: () => set({ ledger: emptyLedger() }),
    }),
    {
      name: "dexcore-ledger-v1",
      partialize: (s) => ({ ledger: s.ledger }),
      skipHydration: true,
    },
  ),
);

export function deskStats(ledger: Ledger, marks: Marks) {
  return {
    equity: equity(ledger, marks),
    available: available(ledger, marks),
    used: usedMargin(ledger),
  };
}
