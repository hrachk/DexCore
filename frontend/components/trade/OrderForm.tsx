"use client";

import { useMemo, useState } from "react";
import { useAccount } from "wagmi";
import { placeOrderRest } from "@/lib/signalr";
import type { MarginMode, OrderSide, OrderType } from "@/lib/types";
import type { MarketInfo } from "@/lib/types";

export function OrderForm({
  market,
  midPrice,
  onOrderPlaced,
}: {
  market: MarketInfo;
  midPrice: number;
  onOrderPlaced?: (msg: string) => void;
}) {
  const { address, isConnected } = useAccount();
  const [side, setSide] = useState<OrderSide>("Buy");
  const [type, setType] = useState<OrderType>("Limit");
  const [marginMode, setMarginMode] = useState<MarginMode>("Cross");
  const [leverage, setLeverage] = useState(10);
  const [price, setPrice] = useState("");
  const [qty, setQty] = useState("");
  const [postOnly, setPostOnly] = useState(false);
  const [reduceOnly, setReduceOnly] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const notional = useMemo(() => {
    const p = type === "Market" ? midPrice : parseFloat(price) || 0;
    const q = parseFloat(qty) || 0;
    return p * q;
  }, [price, qty, type, midPrice]);

  const requiredMargin = notional / leverage;

  async function submit() {
    setError(null);
    const q = parseFloat(qty);
    if (!q || q <= 0) {
      setError("Enter quantity");
      return;
    }
    const p = type === "Market" ? midPrice : parseFloat(price);
    if (type !== "Market" && (!p || p <= 0)) {
      setError("Enter price");
      return;
    }

    setSubmitting(true);
    try {
      // Try live API first
      try {
        await placeOrderRest({
          accountId: 1,
          clientOrderId: Date.now(),
          marketId: market.marketId,
          side,
          type,
          timeInForce: "GTC",
          price: p,
          quantity: q,
          reduceOnly,
          postOnly,
          apiKey: address,
        });
        onOrderPlaced?.(`Order accepted: ${side} ${q} @ ${type === "Market" ? "MKT" : p}`);
      } catch {
        // Offline demo mode
        onOrderPlaced?.(
          `[Demo] ${side} ${type} ${q} ${market.base} @ ${
            type === "Market" ? "MARKET" : p
          } · ${marginMode} ${leverage}x`
        );
      }
      setQty("");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div className="flex flex-col h-full p-3 gap-3 text-sm">
      {/* Side toggle */}
      <div className="grid grid-cols-2 rounded-md overflow-hidden border border-border">
        <button
          onClick={() => setSide("Buy")}
          className={`py-2 font-semibold transition ${
            side === "Buy"
              ? "bg-bid text-black"
              : "bg-surface-100 text-muted hover:text-white"
          }`}
        >
          Buy / Long
        </button>
        <button
          onClick={() => setSide("Sell")}
          className={`py-2 font-semibold transition ${
            side === "Sell"
              ? "bg-ask text-white"
              : "bg-surface-100 text-muted hover:text-white"
          }`}
        >
          Sell / Short
        </button>
      </div>

      {/* Order type */}
      <div className="flex gap-1">
        {(["Limit", "Market", "StopLimit"] as OrderType[]).map((t) => (
          <button
            key={t}
            onClick={() => setType(t)}
            className={`px-2.5 py-1 rounded text-xs ${
              type === t
                ? "bg-surface-200 text-white"
                : "text-muted hover:text-white"
            }`}
          >
            {t === "StopLimit" ? "Stop" : t}
          </button>
        ))}
      </div>

      {/* Margin mode */}
      <div className="flex gap-2">
        {(["Cross", "Isolated"] as MarginMode[]).map((m) => (
          <button
            key={m}
            onClick={() => setMarginMode(m)}
            className={`flex-1 py-1.5 rounded border text-xs ${
              marginMode === m
                ? "border-accent text-accent bg-accent/10"
                : "border-border text-muted"
            }`}
          >
            {m}
          </button>
        ))}
      </div>

      {/* Leverage */}
      <div>
        <div className="flex justify-between text-xs text-muted mb-1">
          <span>Leverage</span>
          <span className="text-white font-mono">{leverage}x</span>
        </div>
        <input
          type="range"
          min={1}
          max={market.maxLeverage}
          value={leverage}
          onChange={(e) => setLeverage(Number(e.target.value))}
          className="w-full accent-accent"
        />
        <div className="flex justify-between text-[10px] text-muted">
          <span>1x</span>
          <span>{market.maxLeverage}x</span>
        </div>
      </div>

      {/* Price */}
      {type !== "Market" && (
        <label className="block">
          <span className="text-xs text-muted">Price (USD)</span>
          <input
            type="number"
            value={price}
            onChange={(e) => setPrice(e.target.value)}
            placeholder={midPrice.toFixed(2)}
            className="mt-1 w-full bg-surface-100 border border-border rounded-md px-3 py-2 font-mono text-sm focus:outline-none focus:border-accent"
          />
        </label>
      )}

      {/* Qty */}
      <label className="block">
        <span className="text-xs text-muted">Quantity ({market.base})</span>
        <input
          type="number"
          value={qty}
          onChange={(e) => setQty(e.target.value)}
          placeholder="0.00"
          step={market.lotSize}
          className="mt-1 w-full bg-surface-100 border border-border rounded-md px-3 py-2 font-mono text-sm focus:outline-none focus:border-accent"
        />
      </label>

      <div className="flex gap-3 text-xs">
        <label className="flex items-center gap-1.5 text-muted cursor-pointer">
          <input
            type="checkbox"
            checked={postOnly}
            onChange={(e) => setPostOnly(e.target.checked)}
            className="accent-accent"
          />
          Post-Only
        </label>
        <label className="flex items-center gap-1.5 text-muted cursor-pointer">
          <input
            type="checkbox"
            checked={reduceOnly}
            onChange={(e) => setReduceOnly(e.target.checked)}
            className="accent-accent"
          />
          Reduce-Only
        </label>
      </div>

      <div className="text-xs text-muted space-y-0.5">
        <div className="flex justify-between">
          <span>Notional</span>
          <span className="font-mono text-white/80">
            ${notional.toLocaleString(undefined, { maximumFractionDigits: 2 })}
          </span>
        </div>
        <div className="flex justify-between">
          <span>Required margin</span>
          <span className="font-mono text-white/80">
            ${requiredMargin.toLocaleString(undefined, { maximumFractionDigits: 2 })}
          </span>
        </div>
      </div>

      {error && <p className="text-ask text-xs">{error}</p>}

      <button
        onClick={submit}
        disabled={submitting}
        className={`mt-auto w-full py-2.5 rounded-md font-semibold transition disabled:opacity-50 ${
          side === "Buy"
            ? "bg-bid hover:brightness-110 text-black"
            : "bg-ask hover:brightness-110 text-white"
        }`}
      >
        {!isConnected
          ? `${side} ${market.base} (demo)`
          : submitting
          ? "Submitting…"
          : `${side} ${market.base}`}
      </button>
    </div>
  );
}
