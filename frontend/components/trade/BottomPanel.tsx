"use client";

import { useState } from "react";
import { X } from "lucide-react";
import { formatPnl, formatPrice, formatQty } from "@/lib/markets";
import type { OpenOrder, Position, TradeHistoryItem } from "@/lib/types";

type Tab = "positions" | "orders" | "history";

export function BottomPanel({
  positions,
  orders,
  history,
  onCancelOrder,
  onClosePosition,
}: {
  positions: Position[];
  orders: OpenOrder[];
  history: TradeHistoryItem[];
  onCancelOrder: (id: number) => void;
  onClosePosition: (symbol: string) => void;
}) {
  const [tab, setTab] = useState<Tab>("positions");

  return (
    <div className="flex flex-col h-full border-t border-border bg-surface">
      <div className="flex items-center gap-1 px-3 pt-2 border-b border-border">
        {(
          [
            ["positions", `Positions (${positions.length})`],
            ["orders", `Open Orders (${orders.length})`],
            ["history", "Trade History"],
          ] as [Tab, string][]
        ).map(([id, label]) => (
          <button
            key={id}
            onClick={() => setTab(id)}
            className={`px-3 py-2 text-xs font-medium border-b-2 -mb-px transition ${
              tab === id
                ? "border-accent text-white"
                : "border-transparent text-muted hover:text-white"
            }`}
          >
            {label}
          </button>
        ))}
      </div>

      <div className="flex-1 overflow-auto">
        {tab === "positions" && (
          <table className="w-full text-xs font-mono">
            <thead className="text-muted text-[11px] uppercase sticky top-0 bg-surface">
              <tr>
                <th className="text-left px-3 py-2 font-medium">Symbol</th>
                <th className="text-left px-3 py-2 font-medium">Side</th>
                <th className="text-right px-3 py-2 font-medium">Size</th>
                <th className="text-right px-3 py-2 font-medium">Entry</th>
                <th className="text-right px-3 py-2 font-medium">Mark</th>
                <th className="text-right px-3 py-2 font-medium">uPnL</th>
                <th className="text-right px-3 py-2 font-medium">Liq.</th>
                <th className="text-right px-3 py-2 font-medium">Margin</th>
                <th className="text-right px-3 py-2 font-medium"></th>
              </tr>
            </thead>
            <tbody>
              {positions.length === 0 && (
                <tr>
                  <td colSpan={9} className="text-center text-muted py-8">
                    No open positions
                  </td>
                </tr>
              )}
              {positions.map((p) => (
                <tr
                  key={p.symbol}
                  className="border-t border-border/50 hover:bg-surface-50"
                >
                  <td className="px-3 py-2">{p.symbol}</td>
                  <td
                    className={`px-3 py-2 ${
                      p.side === "Long" ? "text-bid" : "text-ask"
                    }`}
                  >
                    {p.side} {p.leverage}x · {p.marginMode}
                  </td>
                  <td className="px-3 py-2 text-right">{formatQty(p.quantity)}</td>
                  <td className="px-3 py-2 text-right">
                    {formatPrice(p.entryPrice)}
                  </td>
                  <td className="px-3 py-2 text-right">
                    {formatPrice(p.markPrice)}
                  </td>
                  <td
                    className={`px-3 py-2 text-right font-semibold ${
                      p.unrealizedPnl >= 0 ? "text-bid" : "text-ask"
                    }`}
                  >
                    {formatPnl(p.unrealizedPnl)}
                  </td>
                  <td className="px-3 py-2 text-right text-muted">
                    {formatPrice(p.liquidationPrice)}
                  </td>
                  <td className="px-3 py-2 text-right">
                    {formatPrice(p.margin)}
                  </td>
                  <td className="px-3 py-2 text-right">
                    <button
                      onClick={() => onClosePosition(p.symbol)}
                      className="text-ask hover:underline"
                    >
                      Close
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}

        {tab === "orders" && (
          <table className="w-full text-xs font-mono">
            <thead className="text-muted text-[11px] uppercase sticky top-0 bg-surface">
              <tr>
                <th className="text-left px-3 py-2 font-medium">Symbol</th>
                <th className="text-left px-3 py-2 font-medium">Side</th>
                <th className="text-left px-3 py-2 font-medium">Type</th>
                <th className="text-right px-3 py-2 font-medium">Price</th>
                <th className="text-right px-3 py-2 font-medium">Qty</th>
                <th className="text-right px-3 py-2 font-medium">Filled</th>
                <th className="text-left px-3 py-2 font-medium">Status</th>
                <th className="text-right px-3 py-2 font-medium"></th>
              </tr>
            </thead>
            <tbody>
              {orders.length === 0 && (
                <tr>
                  <td colSpan={8} className="text-center text-muted py-8">
                    No open orders
                  </td>
                </tr>
              )}
              {orders.map((o) => (
                <tr
                  key={o.orderId}
                  className="border-t border-border/50 hover:bg-surface-50"
                >
                  <td className="px-3 py-2">{o.symbol}</td>
                  <td
                    className={`px-3 py-2 ${
                      o.side === "Buy" ? "text-bid" : "text-ask"
                    }`}
                  >
                    {o.side}
                  </td>
                  <td className="px-3 py-2">{o.type}</td>
                  <td className="px-3 py-2 text-right">
                    {formatPrice(o.price)}
                  </td>
                  <td className="px-3 py-2 text-right">
                    {formatQty(o.quantity)}
                  </td>
                  <td className="px-3 py-2 text-right">
                    {formatQty(o.filledQuantity)}
                  </td>
                  <td className="px-3 py-2 text-muted">{o.status}</td>
                  <td className="px-3 py-2 text-right">
                    <button
                      onClick={() => onCancelOrder(o.orderId)}
                      className="p-1 hover:bg-surface-200 rounded"
                      title="Cancel"
                    >
                      <X className="w-3.5 h-3.5 text-muted hover:text-ask" />
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}

        {tab === "history" && (
          <table className="w-full text-xs font-mono">
            <thead className="text-muted text-[11px] uppercase sticky top-0 bg-surface">
              <tr>
                <th className="text-left px-3 py-2 font-medium">Time</th>
                <th className="text-left px-3 py-2 font-medium">Symbol</th>
                <th className="text-left px-3 py-2 font-medium">Side</th>
                <th className="text-right px-3 py-2 font-medium">Price</th>
                <th className="text-right px-3 py-2 font-medium">Qty</th>
                <th className="text-right px-3 py-2 font-medium">Fee</th>
              </tr>
            </thead>
            <tbody>
              {history.map((h) => (
                <tr
                  key={h.tradeId}
                  className="border-t border-border/50 hover:bg-surface-50"
                >
                  <td className="px-3 py-2 text-muted">
                    {new Date(h.timestamp).toLocaleString()}
                  </td>
                  <td className="px-3 py-2">{h.symbol}</td>
                  <td
                    className={`px-3 py-2 ${
                      h.side === "Buy" ? "text-bid" : "text-ask"
                    }`}
                  >
                    {h.side}
                  </td>
                  <td className="px-3 py-2 text-right">
                    {formatPrice(h.price)}
                  </td>
                  <td className="px-3 py-2 text-right">
                    {formatQty(h.quantity)}
                  </td>
                  <td className="px-3 py-2 text-right text-muted">
                    {h.fee.toFixed(2)}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </div>
  );
}
