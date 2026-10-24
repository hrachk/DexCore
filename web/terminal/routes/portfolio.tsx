import { createFileRoute } from "@tanstack/react-router";
import { useMemo } from "react";
import { Shell } from "@/components/terminal/shell";
import { marksFrom, useTickers } from "@/components/terminal/use-market";
import { liqPrice, upnl } from "@/lib/exchange/engine";
import { fmtPx, fmtQty, fmtUsd } from "@/lib/exchange/format";
import { marketBySymbol } from "@/lib/exchange/markets";
import { deskStats, useDesk } from "@/lib/exchange/store";

export const Route = createFileRoute("/portfolio")({
  component: PortfolioPage,
});

function PortfolioPage() {
  const tickers = useTickers();
  const marks = useMemo(() => marksFrom(tickers.data), [tickers.data]);
  const ledger = useDesk((s) => s.ledger);
  const close = useDesk((s) => s.close);
  const stats = deskStats(ledger, marks);
  const u = stats.equity - ledger.wallet;
  return (
    <div className="min-h-dvh bg-bg text-fg">
      <Shell marks={marks} />
      <main className="mx-auto max-w-5xl px-3 py-4">
        <h1 className="text-lg font-semibold">Portfolio</h1>
        <div className="mt-3 grid grid-cols-2 gap-2 sm:grid-cols-4">
          <Stat label="Equity" value={fmtUsd(stats.equity)} />
          <Stat label="Wallet" value={fmtUsd(ledger.wallet)} />
          <Stat label="Unrealized" value={fmtUsd(u)} tone={u >= 0 ? "up" : "down"} />
          <Stat label="Margin used" value={fmtUsd(stats.used)} />
        </div>
        <h2 className="mt-6 text-sm font-medium">Positions</h2>
        {ledger.positions.length === 0 ? (
          <p className="mt-2 text-sm text-muted">Flat. Open a position from Trade.</p>
        ) : (
          <ul className="mt-2 divide-y divide-line rounded-sm border border-line">
            {ledger.positions.map((p) => {
              const m = marketBySymbol(p.symbol);
              const mark = marks[p.symbol] ?? p.entry;
              const pnl = upnl(p, mark);
              return (
                <li key={p.symbol} className="flex flex-wrap items-center gap-3 px-3 py-3 text-sm">
                  <span className="font-medium">{p.symbol}</span>
                  <span className={p.side === "long" ? "text-up" : "text-down"}>{p.side} {p.leverage}x</span>
                  <span className="num text-muted">{fmtQty(p.qty, m.qtyDp)} @ {fmtPx(p.entry, m.pxDp)}</span>
                  <span className={"num " + (pnl >= 0 ? "text-up" : "text-down")}>{fmtUsd(pnl)}</span>
                  <span className="num text-xs text-down">liq {fmtPx(liqPrice(p.entry, p.side, p.leverage), m.pxDp)}</span>
                  <button
                    type="button"
                    className="ml-auto rounded-sm border border-line px-3 py-2 text-xs"
                    onClick={() => close(p.symbol, { bids: mark ? [{ price: mark, qty: p.qty }] : [], asks: mark ? [{ price: mark, qty: p.qty }] : [] }, mark, marks)}
                  >
                    Close at mark
                  </button>
                </li>
              );
            })}
          </ul>
        )}
      </main>
    </div>
  );
}

function Stat({ label, value, tone }: { label: string; value: string; tone?: "up" | "down" }) {
  return (
    <div className="rounded-sm border border-line bg-surface px-3 py-3">
      <div className="text-xs text-muted">{label}</div>
      <div className={"num mt-1 text-base " + (tone === "up" ? "text-up" : tone === "down" ? "text-down" : "text-fg")}>{value}</div>
    </div>
  );
}
