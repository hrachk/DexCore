import { useEffect, useMemo, useState } from "react";
import { ChartPane } from "@/components/terminal/chart-pane";
import { OrderForm } from "@/components/terminal/order-form";
import { Shell } from "@/components/terminal/shell";
import { EMPTY_BOOK, marksFrom, tickerFor, useBook, useCandles, useTape, useTickers } from "@/components/terminal/use-market";
import { liqPrice, upnl } from "@/lib/exchange/engine";
import { fmtPct, fmtPx, fmtQty, fmtTime, fmtUsd, fmtVol } from "@/lib/exchange/format";
import { MARKETS, marketBySymbol, type Market } from "@/lib/exchange/markets";
import { deskStats, useDesk } from "@/lib/exchange/store";

const TFS = ["1m", "5m", "15m", "1h", "4h", "1d"] as const;

export function TradeScreen() {
  const tickers = useTickers();
  const marks = useMemo(() => marksFrom(tickers.data), [tickers.data]);
  const ledger = useDesk((s) => s.ledger);
  const symbol = useDesk((s) => s.symbol);
  const setSymbol = useDesk((s) => s.setSymbol);
  const [tf, setTf] = useState<(typeof TFS)[number]>("1m");
  const [q, setQ] = useState("");
  const [tab, setTab] = useState<"pos" | "ord" | "hist">("pos");
  const [banner, setBanner] = useState<string | null>(null);
  const [pane, setPane] = useState<"chart" | "book" | "order">("chart");

  const market = marketBySymbol(symbol);
  const bookQ = useBook(market.binance);
  const tapeQ = useTape(market.binance);
  const candles = useCandles(market.binance, tf);
  const tick = tickerFor(tickers.data, market.binance);
  const book = bookQ.data ?? EMPTY_BOOK;
  const mark = tick?.last ?? marks[symbol] ?? 0;

  const tickBook = useDesk((s) => s.tick);
  const cancel = useDesk((s) => s.cancel);
  const close = useDesk((s) => s.close);
  const stats = deskStats(ledger, marks);
  const pos = ledger.positions.find((p) => p.symbol === symbol);

  useEffect(() => {
    if (!book.bids.length && !book.asks.length) return;
    if (!(mark > 0)) return;
    tickBook(symbol, book, mark, marks);
  }, [book, mark, symbol, marks, tickBook]);

  const filtered = MARKETS.filter((m) => {
    const s = q.trim().toLowerCase();
    return !s || m.base.toLowerCase().includes(s) || m.symbol.toLowerCase().includes(s);
  });

  const maxBid = Math.max(1, ...book.bids.map((l) => l.qty));
  const maxAsk = Math.max(1, ...book.asks.map((l) => l.qty));

  return (
    <div className="flex min-h-dvh flex-col overflow-x-hidden bg-bg text-fg">
      <Shell marks={marks} />
      <div className="flex min-h-0 flex-1 flex-col lg:flex-row">
        <aside className="hidden w-56 shrink-0 flex-col overflow-hidden border-r border-line lg:flex">
          <MarketList markets={filtered} query={q} setQuery={setQ} active={symbol} onPick={setSymbol} marks={marks} rows={tickers.data} />
        </aside>
        <section className="flex min-w-0 flex-1 flex-col">
          <TickerBar market={market} tick={tick} mark={mark} tf={tf} setTf={setTf} />
          {banner ? (
            <p className="border-b border-line bg-raised px-3 py-1 text-xs text-accent">{banner}</p>
          ) : null}
          {tickers.isError || bookQ.isError ? (
            <p className="border-b border-line px-3 py-1 text-xs text-down">Live feed unavailable. Retrying.</p>
          ) : null}
          <div className="flex gap-1 border-b border-line px-2 py-1 lg:hidden">
            {(["chart", "book", "order"] as const).map((p) => (
              <button key={p} type="button" onClick={() => setPane(p)} className={"rounded-sm px-3 py-2 text-xs capitalize " + (pane === p ? "bg-raised text-fg" : "text-muted")}>
                {p}
              </button>
            ))}
          </div>
          <div className={"min-h-72 flex-1 " + (pane === "chart" ? "block" : "hidden lg:block")}>
            {candles.data && candles.data.length > 0 ? (
              <ChartPane candles={candles.data} symbol={symbol + tf} />
            ) : (
              <div className="grid h-full min-h-64 place-items-center text-sm text-muted">Loading candles…</div>
            )}
          </div>
          <div className="hidden border-t border-line lg:block">
            <Blotter tab={tab} setTab={setTab} market={market} marks={marks} onCancel={cancel} onClose={(s) => {
              const err = close(s, book, marks[s] ?? mark, marks);
              setBanner(err ?? `Closed ${s}`);
            }} />
          </div>
        </section>
        <aside className={"w-full shrink-0 border-line lg:flex lg:w-80 lg:flex-col lg:border-l " + (pane === "chart" ? "hidden lg:flex" : "flex")}>
          <div className={pane === "order" ? "hidden lg:block" : "block"}>
            <BookView market={market} book={book} mark={mark} maxBid={maxBid} maxAsk={maxAsk} tape={tapeQ.data ?? []} />
          </div>
          <div className={pane === "book" ? "hidden lg:block" : "block"}>
            <OrderForm
              market={market}
              book={book}
              mark={mark}
              free={Math.max(0, stats.available)}
              hasPos={!!pos}
              posSide={pos?.side}
              posQty={pos?.qty ?? 0}
              onError={(m) => setBanner(m)}
              onDone={(m) => setBanner(m)}
            />
          </div>
        </aside>
      </div>
      <div className="border-t border-line lg:hidden">
        <Blotter tab={tab} setTab={setTab} market={market} marks={marks} onCancel={cancel} onClose={(s) => {
          const err = close(s, book, marks[s] ?? mark, marks);
          setBanner(err ?? `Closed ${s}`);
        }} />
      </div>
    </div>
  );
}

function MarketList({
  markets, query, setQuery, active, onPick, marks, rows,
}: {
  markets: Market[];
  query: string;
  setQuery: (v: string) => void;
  active: string;
  onPick: (s: string) => void;
  marks: Record<string, number>;
  rows: ReturnType<typeof useTickers>["data"];
}) {
  return (
    <>
      <input
        value={query}
        onChange={(e) => setQuery(e.target.value)}
        placeholder="Search"
        className="m-2 rounded-sm border border-line bg-bg px-2 py-2 text-sm"
      />
      <ul className="min-h-0 flex-1 overflow-auto">
        {markets.map((m) => {
          const t = tickerFor(rows, m.binance);
          const up = (t?.changePct ?? 0) >= 0;
          return (
            <li key={m.symbol}>
              <button
                type="button"
                onClick={() => onPick(m.symbol)}
                className={"flex w-full items-center justify-between px-3 py-2 text-left text-xs " + (active === m.symbol ? "bg-raised" : "hover:bg-raised")}
              >
                <span className="font-medium">{m.base}</span>
                <span className="text-right">
                  <span className="num block">{fmtPx(marks[m.symbol] ?? t?.last ?? 0, m.pxDp)}</span>
                  <span className={up ? "text-up" : "text-down"}>{fmtPct(t?.changePct ?? 0)}</span>
                </span>
              </button>
            </li>
          );
        })}
      </ul>
    </>
  );
}

function TickerBar({
  market, tick, mark, tf, setTf,
}: {
  market: Market;
  tick: ReturnType<typeof tickerFor>;
  mark: number;
  tf: (typeof TFS)[number];
  setTf: (t: (typeof TFS)[number]) => void;
}) {
  const up = (tick?.changePct ?? 0) >= 0;
  return (
    <div className="flex flex-wrap items-center gap-x-4 gap-y-1 border-b border-line px-3 py-2">
      <div>
        <div className="text-sm font-semibold">{market.symbol}</div>
        <div className="text-xs text-muted">Perp · max {market.maxLev}x</div>
      </div>
      <div className={"num text-lg font-medium " + (up ? "text-up" : "text-down")}>{fmtPx(mark, market.pxDp)}</div>
      <div className={"text-xs " + (up ? "text-up" : "text-down")}>{fmtPct(tick?.changePct ?? 0)}</div>
      <div className="hidden text-xs text-muted sm:block">24h vol <span className="num text-fg">{fmtVol(tick?.quoteVol ?? 0)}</span></div>
      <div className="hidden text-xs text-muted md:block">H <span className="num text-fg">{fmtPx(tick?.high ?? 0, market.pxDp)}</span></div>
      <div className="hidden text-xs text-muted md:block">L <span className="num text-fg">{fmtPx(tick?.low ?? 0, market.pxDp)}</span></div>
      <div className="ml-auto flex gap-1">
        {TFS.map((t) => (
          <button key={t} type="button" onClick={() => setTf(t)} className={"rounded-sm px-2 py-1 text-xs " + (tf === t ? "bg-raised text-fg" : "text-muted")}>
            {t}
          </button>
        ))}
      </div>
    </div>
  );
}

function BookView({
  market, book, mark, maxBid, maxAsk, tape,
}: {
  market: Market;
  book: { bids: { price: number; qty: number }[]; asks: { price: number; qty: number }[] };
  mark: number;
  maxBid: number;
  maxAsk: number;
  tape: { id: number; price: number; qty: number; buyerMaker: boolean; time: number }[];
}) {
  const spread = book.asks[0] && book.bids[0] ? book.asks[0].price - book.bids[0].price : 0;
  return (
    <div className="flex max-h-80 flex-col lg:max-h-none lg:min-h-0 lg:flex-1">
      <div className="grid grid-cols-3 px-3 py-1 text-xs text-muted">
        <span>Price</span>
        <span className="text-right">Size</span>
        <span className="text-right">Total</span>
      </div>
      <div className="min-h-0 flex-1 overflow-auto">
        {[...book.asks].reverse().map((l) => (
          <Row key={`a${l.price}`} price={l.price} qty={l.qty} dp={market.pxDp} qdp={market.qtyDp} pct={l.qty / maxAsk} side="ask" />
        ))}
        <div className="flex items-center justify-between px-3 py-1 text-xs">
          <span className="num text-sm">{fmtPx(mark, market.pxDp)}</span>
          <span className="text-muted">spread {fmtPx(spread, market.pxDp)}</span>
        </div>
        {book.bids.map((l) => (
          <Row key={`b${l.price}`} price={l.price} qty={l.qty} dp={market.pxDp} qdp={market.qtyDp} pct={l.qty / maxBid} side="bid" />
        ))}
      </div>
      <ul className="max-h-28 overflow-auto border-t border-line">
        {tape.slice(0, 12).map((t) => (
          <li key={t.id} className="grid grid-cols-3 px-3 py-0.5 text-xs">
            <span className={"num " + (t.buyerMaker ? "text-down" : "text-up")}>{fmtPx(t.price, market.pxDp)}</span>
            <span className="num text-right text-muted">{fmtQty(t.qty, market.qtyDp)}</span>
            <span className="num text-right text-muted">{fmtTime(t.time)}</span>
          </li>
        ))}
      </ul>
    </div>
  );
}

function Row({ price, qty, dp, qdp, pct, side }: { price: number; qty: number; dp: number; qdp: number; pct: number; side: "bid" | "ask" }) {
  return (
    <div className="relative grid grid-cols-3 px-3 py-0.5 text-xs">
      <span className={"absolute inset-y-0 right-0 " + (side === "bid" ? "bg-up/15" : "bg-down/15")} style={{ width: `${Math.min(100, pct * 100)}%` }} />
      <span className={"num relative " + (side === "bid" ? "text-up" : "text-down")}>{fmtPx(price, dp)}</span>
      <span className="num relative text-right">{fmtQty(qty, qdp)}</span>
      <span className="num relative text-right text-muted">{fmtUsd(price * qty, 0)}</span>
    </div>
  );
}

function Blotter({
  tab, setTab, market, marks, onCancel, onClose,
}: {
  tab: "pos" | "ord" | "hist";
  setTab: (t: "pos" | "ord" | "hist") => void;
  market: Market;
  marks: Record<string, number>;
  onCancel: (id: string) => void;
  onClose: (symbol: string) => void;
}) {
  const ledger = useDesk((s) => s.ledger);
  const open = ledger.orders.filter((o) => o.status === "open" || o.status === "partial");
  return (
    <div>
      <div className="flex gap-1 border-b border-line px-2">
        {(
          [
            ["pos", `Positions ${ledger.positions.length}`],
            ["ord", `Orders ${open.length}`],
            ["hist", "Fills"],
          ] as const
        ).map(([id, label]) => (
          <button key={id} type="button" onClick={() => setTab(id)} className={"px-3 py-2 text-xs " + (tab === id ? "border-b border-accent text-fg" : "text-muted")}>
            {label}
          </button>
        ))}
      </div>
      <div className="max-h-56 overflow-auto">
        {tab === "pos" ? (
          ledger.positions.length === 0 ? (
            <p className="px-3 py-4 text-xs text-muted">No open positions. A market order fills against the live book.</p>
          ) : (
            <table className="w-full text-left text-xs">
              <thead className="text-muted">
                <tr>
                  <th className="px-3 py-1 font-normal">Market</th>
                  <th className="px-2 py-1 font-normal">Side</th>
                  <th className="px-2 py-1 font-normal">Size</th>
                  <th className="px-2 py-1 font-normal">Entry</th>
                  <th className="px-2 py-1 font-normal">uPnL</th>
                  <th className="px-2 py-1 font-normal">Liq</th>
                  <th className="px-2 py-1 font-normal" />
                </tr>
              </thead>
              <tbody>
                {ledger.positions.map((p) => {
                  const m = marketBySymbol(p.symbol);
                  const mark = marks[p.symbol] ?? p.entry;
                  const u = upnl(p, mark);
                  return (
                    <tr key={p.symbol} className="border-t border-line">
                      <td className="px-3 py-2">{p.symbol}</td>
                      <td className={"px-2 " + (p.side === "long" ? "text-up" : "text-down")}>{p.side} {p.leverage}x</td>
                      <td className="num px-2">{fmtQty(p.qty, m.qtyDp)}</td>
                      <td className="num px-2">{fmtPx(p.entry, m.pxDp)}</td>
                      <td className={"num px-2 " + (u >= 0 ? "text-up" : "text-down")}>{fmtUsd(u)}</td>
                      <td className="num px-2 text-down">{fmtPx(liqPrice(p.entry, p.side, p.leverage), m.pxDp)}</td>
                      <td className="px-2">
                        <button type="button" onClick={() => onClose(p.symbol)} className="rounded-sm border border-line px-2 py-1">
                          Close
                        </button>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          )
        ) : null}
        {tab === "ord" ? (
          open.length === 0 ? (
            <p className="px-3 py-4 text-xs text-muted">No resting orders. A limit away from the touch stays on the book until price trades through it.</p>
          ) : (
            <table className="w-full text-left text-xs">
              <thead className="text-muted">
                <tr>
                  <th className="px-3 py-1 font-normal">Market</th>
                  <th className="px-2 py-1 font-normal">Side</th>
                  <th className="px-2 py-1 font-normal">Price</th>
                  <th className="px-2 py-1 font-normal">Filled</th>
                  <th className="px-2 py-1 font-normal" />
                </tr>
              </thead>
              <tbody>
                {open.map((o) => (
                  <tr key={o.id} className="border-t border-line">
                    <td className="px-3 py-2">{o.symbol}</td>
                    <td className={o.side === "buy" ? "text-up" : "text-down"}>{o.side}</td>
                    <td className="num px-2">{fmtPx(o.price, marketBySymbol(o.symbol).pxDp)}</td>
                    <td className="num px-2">{fmtQty(o.filled, 4)} / {fmtQty(o.qty, 4)}</td>
                    <td className="px-2">
                      <button type="button" onClick={() => onCancel(o.id)} className="rounded-sm border border-line px-2 py-1">
                        Cancel
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )
        ) : null}
        {tab === "hist" ? (
          ledger.fills.length === 0 ? (
            <p className="px-3 py-4 text-xs text-muted">Fills, deposits and withdrawals land here.</p>
          ) : (
            <table className="w-full text-left text-xs">
              <tbody>
                {ledger.fills.slice(0, 40).map((f) => (
                  <tr key={f.id} className="border-t border-line">
                    <td className="px-3 py-1.5 text-muted">{fmtTime(f.time)}</td>
                    <td className="px-2">{f.symbol}</td>
                    <td className={f.side === "buy" ? "text-up" : "text-down"}>{f.note}</td>
                    <td className="num px-2">{fmtPx(f.price, market.pxDp)}</td>
                    <td className="num px-2">{fmtQty(f.qty, 4)}</td>
                    <td className={"num px-2 " + (f.pnl >= 0 ? "text-up" : "text-down")}>{f.pnl ? fmtUsd(f.pnl) : ""}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )
        ) : null}
      </div>
    </div>
  );
}
