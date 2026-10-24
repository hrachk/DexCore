import { createFileRoute, useNavigate } from "@tanstack/react-router";
import { useMemo, useState } from "react";
import { Shell } from "@/components/terminal/shell";
import { marksFrom, tickerFor, useTickers } from "@/components/terminal/use-market";
import { fmtPct, fmtPx, fmtVol } from "@/lib/exchange/format";
import { MARKETS } from "@/lib/exchange/markets";
import { useDesk } from "@/lib/exchange/store";

export const Route = createFileRoute("/markets")({
  component: MarketsPage,
});

function MarketsPage() {
  const tickers = useTickers();
  const marks = useMemo(() => marksFrom(tickers.data), [tickers.data]);
  const nav = useNavigate();
  const setSymbol = useDesk((s) => s.setSymbol);
  const [q, setQ] = useState("");
  const rows = MARKETS.filter((m) => m.base.toLowerCase().includes(q.trim().toLowerCase()));
  return (
    <div className="min-h-dvh bg-bg text-fg">
      <Shell marks={marks} />
      <main className="mx-auto max-w-5xl px-3 py-4">
        <h1 className="text-lg font-semibold">Markets</h1>
        <p className="mt-1 text-sm text-muted">USDT perpetuals. Prices are the live Binance spot print. Fills match against that book on the desk.</p>
        <input value={q} onChange={(e) => setQ(e.target.value)} placeholder="Filter" className="mt-3 w-full max-w-xs rounded-sm border border-line bg-surface px-3 py-2 text-sm" />
        <div className="mt-3 overflow-auto rounded-sm border border-line">
          <table className="w-full text-left text-sm">
            <thead className="text-xs text-muted">
              <tr>
                <th className="px-3 py-2 font-normal">Market</th>
                <th className="px-2 py-2 font-normal">Last</th>
                <th className="px-2 py-2 font-normal">24h</th>
                <th className="hidden px-2 py-2 font-normal sm:table-cell">Volume</th>
                <th className="hidden px-2 py-2 font-normal md:table-cell">Max lev</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((m) => {
                const t = tickerFor(tickers.data, m.binance);
                const up = (t?.changePct ?? 0) >= 0;
                return (
                  <tr key={m.symbol} className="cursor-pointer border-t border-line hover:bg-raised" onClick={() => { setSymbol(m.symbol); void nav({ to: "/" }); }}>
                    <td className="px-3 py-3 font-medium">{m.symbol}</td>
                    <td className="num px-2">{fmtPx(t?.last ?? 0, m.pxDp)}</td>
                    <td className={"num px-2 " + (up ? "text-up" : "text-down")}>{fmtPct(t?.changePct ?? 0)}</td>
                    <td className="num hidden px-2 sm:table-cell">{fmtVol(t?.quoteVol ?? 0)}</td>
                    <td className="hidden px-2 text-muted md:table-cell">{m.maxLev}x</td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      </main>
    </div>
  );
}
