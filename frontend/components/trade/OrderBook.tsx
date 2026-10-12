"use client";

import { formatPrice, formatQty } from "@/lib/markets";
import type { PriceLevel } from "@/lib/types";

export function OrderBookPanel({
  bids,
  asks,
  spread,
  mid,
}: {
  bids: PriceLevel[];
  asks: PriceLevel[];
  spread: number;
  mid: number;
}) {
  const maxBidTotal = bids[bids.length - 1]?.total ?? 1;
  const maxAskTotal = asks[asks.length - 1]?.total ?? 1;
  const maxTotal = Math.max(maxBidTotal, maxAskTotal, 1);

  // Asks shown reversed (lowest near mid)
  const asksView = [...asks].slice(0, 12).reverse();
  const bidsView = bids.slice(0, 12);

  return (
    <div className="flex flex-col h-full text-xs font-mono">
      <div className="grid grid-cols-3 gap-1 px-3 py-2 text-muted border-b border-border text-[11px] uppercase tracking-wide">
        <span>Price</span>
        <span className="text-right">Size</span>
        <span className="text-right">Total</span>
      </div>

      <div className="flex-1 overflow-hidden flex flex-col">
        {/* Asks */}
        <div className="flex-1 overflow-hidden flex flex-col justify-end">
          {asksView.map((l) => (
            <DepthRow
              key={`a-${l.price}`}
              level={l}
              side="ask"
              maxTotal={maxTotal}
            />
          ))}
        </div>

        {/* Spread */}
        <div className="px-3 py-1.5 border-y border-border bg-surface-50 flex items-center justify-between">
          <span className="text-sm font-semibold text-white">
            {formatPrice(mid)}
          </span>
          <span className="text-muted text-[11px]">
            Spread {formatPrice(spread, 2)}
          </span>
        </div>

        {/* Bids */}
        <div className="flex-1 overflow-hidden">
          {bidsView.map((l) => (
            <DepthRow
              key={`b-${l.price}`}
              level={l}
              side="bid"
              maxTotal={maxTotal}
            />
          ))}
        </div>
      </div>
    </div>
  );
}

function DepthRow({
  level,
  side,
  maxTotal,
}: {
  level: PriceLevel;
  side: "bid" | "ask";
  maxTotal: number;
}) {
  const pct = Math.min(100, ((level.total ?? level.quantity) / maxTotal) * 100);
  const priceColor = side === "bid" ? "text-bid" : "text-ask";
  const barClass = side === "bid" ? "depth-bar-bid" : "depth-bar-ask";

  return (
    <div className="relative grid grid-cols-3 gap-1 px-3 py-[3px] hover:bg-surface-100 cursor-default">
      <div
        className={`absolute inset-y-0 right-0 ${barClass}`}
        style={{ width: `${pct}%` }}
      />
      <span className={`relative ${priceColor}`}>{formatPrice(level.price)}</span>
      <span className="relative text-right text-white/90">
        {formatQty(level.quantity)}
      </span>
      <span className="relative text-right text-muted">
        {formatQty(level.total ?? level.quantity)}
      </span>
    </div>
  );
}
