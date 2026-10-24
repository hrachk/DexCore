import { useMemo, useState } from "react";
import { liqPrice } from "@/lib/exchange/engine";
import { fmtPx, fmtUsd } from "@/lib/exchange/format";
import type { Market } from "@/lib/exchange/markets";
import { useDesk } from "@/lib/exchange/store";
import type { Book, MarginMode, OrdType, Side } from "@/lib/exchange/types";
import { TAKER_FEE } from "@/lib/exchange/types";

type Props = {
  market: Market;
  book: Book;
  mark: number;
  free: number;
  hasPos: boolean;
  posSide?: "long" | "short";
  posQty: number;
  onError: (m: string | null) => void;
  onDone: (m: string) => void;
};

export function OrderForm(props: Props) {
  const submit = useDesk((s) => s.submit);
  const marks = { [props.market.symbol]: props.mark };
  const [side, setSide] = useState<Side>("buy");
  const [type, setType] = useState<OrdType>("limit");
  const [mode, setMode] = useState<MarginMode>("cross");
  const [lev, setLev] = useState(5);
  const [price, setPrice] = useState("");
  const [qty, setQty] = useState("");
  const [reduceOnly, setReduceOnly] = useState(false);
  const [postOnly, setPostOnly] = useState(false);
  const [confirm, setConfirm] = useState(false);

  const px = type === "market" ? props.mark : Number(price);
  const q = Number(qty);
  const notional = Number.isFinite(px) && Number.isFinite(q) ? px * q : 0;
  const margin = lev > 0 ? notional / lev : notional;
  const fee = notional * TAKER_FEE;
  const liq = useMemo(() => {
    if (!(px > 0)) return 0;
    return liqPrice(px, side === "buy" ? "long" : "short", lev);
  }, [px, side, lev]);

  function setPct(p: number) {
    const ref = px > 0 ? px : props.mark;
    if (!(ref > 0) || props.free <= 0) return;
    const raw = ((props.free * p) / 100) * lev / ref;
    const stepped = Math.floor(raw / props.market.lot) * props.market.lot;
    setQty(stepped > 0 ? String(Number(stepped.toFixed(props.market.qtyDp))) : "");
  }

  function send() {
    const err = submit(
      {
        symbol: props.market.symbol,
        side,
        type,
        price: type === "market" ? props.mark : Number(price),
        qty: Number(qty),
        leverage: lev,
        marginMode: mode,
        reduceOnly,
        postOnly,
        mark: props.mark,
      },
      props.book,
      marks,
    );
    setConfirm(false);
    if (err) props.onError(err);
    else {
      props.onError(null);
      props.onDone(`${side === "buy" ? "Buy" : "Sell"} ${qty} ${props.market.base} accepted`);
      setQty("");
    }
  }

  return (
    <div className="flex flex-col gap-2 border-t border-line p-3">
      <div className="grid grid-cols-2 gap-1">
        <button type="button" onClick={() => setSide("buy")} className={"rounded-sm py-2 text-sm font-medium " + (side === "buy" ? "bg-up text-bg" : "bg-raised text-muted")}>
          Buy / Long
        </button>
        <button type="button" onClick={() => setSide("sell")} className={"rounded-sm py-2 text-sm font-medium " + (side === "sell" ? "bg-down text-bg" : "bg-raised text-muted")}>
          Sell / Short
        </button>
      </div>
      <div className="flex gap-1 text-xs">
        {(["limit", "market"] as const).map((t) => (
          <button key={t} type="button" onClick={() => setType(t)} className={"rounded-sm px-2 py-1 capitalize " + (type === t ? "bg-raised text-fg" : "text-muted")}>
            {t}
          </button>
        ))}
        <span className="ml-auto flex gap-1">
          {(["cross", "isolated"] as const).map((m) => (
            <button key={m} type="button" onClick={() => setMode(m)} className={"rounded-sm px-2 py-1 capitalize " + (mode === m ? "bg-raised text-fg" : "text-muted")}>
              {m}
            </button>
          ))}
        </span>
      </div>
      <label className="flex items-center justify-between text-xs text-muted">
        <span>Leverage</span>
        <span className="num text-fg">{lev}x</span>
      </label>
      <input type="range" min={1} max={props.market.maxLev} value={lev} onChange={(e) => setLev(Number(e.target.value))} className="accent-accent" />
      {type === "limit" ? (
        <label className="flex flex-col gap-1 text-xs text-muted">
          Price
          <input
            inputMode="decimal"
            value={price}
            placeholder={props.mark ? fmtPx(props.mark, props.market.pxDp) : "0"}
            onChange={(e) => setPrice(e.target.value)}
            className="num rounded-sm border border-line bg-bg px-2 py-2 text-sm text-fg"
          />
        </label>
      ) : null}
      <label className="flex flex-col gap-1 text-xs text-muted">
        Size ({props.market.base})
        <input
          inputMode="decimal"
          value={qty}
          placeholder="0.00"
          onChange={(e) => setQty(e.target.value)}
          className="num rounded-sm border border-line bg-bg px-2 py-2 text-sm text-fg"
        />
      </label>
      <div className="grid grid-cols-5 gap-1">
        {[10, 25, 50, 75, 100].map((p) => (
          <button key={p} type="button" onClick={() => setPct(p)} className="rounded-sm bg-raised py-1 text-xs text-muted">
            {p}%
          </button>
        ))}
      </div>
      <label className="flex items-center gap-2 text-xs text-muted">
        <input type="checkbox" checked={reduceOnly} onChange={(e) => setReduceOnly(e.target.checked)} />
        Reduce-only
      </label>
      <label className="flex items-center gap-2 text-xs text-muted">
        <input type="checkbox" checked={postOnly} onChange={(e) => setPostOnly(e.target.checked)} disabled={type === "market"} />
        Post-only
      </label>
      <dl className="grid grid-cols-2 gap-y-1 text-xs text-muted">
        <dt>Notional</dt>
        <dd className="num text-right text-fg">{fmtUsd(notional)}</dd>
        <dt>Margin</dt>
        <dd className="num text-right text-fg">{fmtUsd(margin)}</dd>
        <dt>Est. liq</dt>
        <dd className="num text-right text-down">{px > 0 ? fmtPx(liq, props.market.pxDp) : "—"}</dd>
        <dt>Fee 4 bps</dt>
        <dd className="num text-right text-fg">{fmtUsd(fee)}</dd>
        <dt>Available</dt>
        <dd className="num text-right text-fg">{fmtUsd(props.free)}</dd>
      </dl>
      {props.hasPos ? (
        <p className="text-xs text-muted">
          Open {props.posSide} {props.posQty} {props.market.base}
        </p>
      ) : null}
      <button
        type="button"
        onClick={() => setConfirm(true)}
        className={"rounded-sm py-2 text-sm font-semibold " + (side === "buy" ? "bg-up text-bg" : "bg-down text-bg")}
      >
        {side === "buy" ? "Buy" : "Sell"} {props.market.base}
      </button>
      {confirm ? (
        <div className="fixed inset-0 z-30 grid place-items-center bg-bg/80 p-4" role="dialog" aria-modal="true">
          <div className="w-full max-w-sm rounded-sm border border-line bg-surface p-4">
            <h2 className="text-sm font-semibold">
              Confirm {side === "buy" ? "long" : "short"} {props.market.symbol}
            </h2>
            <dl className="mt-3 grid grid-cols-2 gap-y-1 text-xs text-muted">
              <dt>Type</dt><dd className="text-right capitalize text-fg">{type}</dd>
              <dt>Price</dt><dd className="num text-right text-fg">{type === "market" ? "Market" : fmtPx(px, props.market.pxDp)}</dd>
              <dt>Size</dt><dd className="num text-right text-fg">{qty || "0"} {props.market.base}</dd>
              <dt>Leverage</dt><dd className="num text-right text-fg">{lev}x {mode}</dd>
              <dt>Margin</dt><dd className="num text-right text-fg">{fmtUsd(margin)}</dd>
              <dt>Liq</dt><dd className="num text-right text-down">{fmtPx(liq, props.market.pxDp)}</dd>
            </dl>
            <div className="mt-4 grid grid-cols-2 gap-2">
              <button type="button" onClick={() => setConfirm(false)} className="rounded-sm border border-line py-2 text-sm">
                Cancel
              </button>
              <button type="button" onClick={send} className={"rounded-sm py-2 text-sm font-semibold " + (side === "buy" ? "bg-up text-bg" : "bg-down text-bg")}>
                Submit
              </button>
            </div>
          </div>
        </div>
      ) : null}
    </div>
  );
}
