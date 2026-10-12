"use client";

import { formatPrice, formatQty } from "@/lib/markets";
import type { TradeTick } from "@/lib/types";

export function TradesTape({ trades }: { trades: TradeTick[] }) {
  return (
    <div className="flex flex-col h-full text-xs font-mono">
      <div className="grid grid-cols-3 gap-1 px-3 py-2 text-muted border-b border-border text-[11px] uppercase tracking-wide">
        <span>Price</span>
        <span className="text-right">Size</span>
        <span className="text-right">Time</span>
      </div>
      <div className="flex-1 overflow-y-auto">
        {trades.map((t) => {
          const buy = !t.isBuyerMaker;
          const d = new Date(Number(t.timestampUtcTicks) / 10000);
          const time = isNaN(d.getTime())
            ? "--:--:--"
            : d.toLocaleTimeString("en-US", { hour12: false });
          return (
            <div
              key={`${t.tradeId}-${t.timestampUtcTicks}`}
              className="grid grid-cols-3 gap-1 px-3 py-[3px] hover:bg-surface-100"
            >
              <span className={buy ? "text-bid" : "text-ask"}>
                {formatPrice(t.price)}
              </span>
              <span className="text-right">{formatQty(t.quantity)}</span>
              <span className="text-right text-muted">{time}</span>
            </div>
          );
        })}
      </div>
    </div>
  );
}
