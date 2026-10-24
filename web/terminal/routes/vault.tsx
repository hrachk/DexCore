import { createFileRoute } from "@tanstack/react-router";
import { useMemo, useState } from "react";
import { Shell } from "@/components/terminal/shell";
import { marksFrom, useTickers } from "@/components/terminal/use-market";
import { fmtUsd } from "@/lib/exchange/format";
import { deskStats, useDesk } from "@/lib/exchange/store";

export const Route = createFileRoute("/vault")({
  component: VaultPage,
});

function VaultPage() {
  const tickers = useTickers();
  const marks = useMemo(() => marksFrom(tickers.data), [tickers.data]);
  const ledger = useDesk((s) => s.ledger);
  const fund = useDesk((s) => s.fund);
  const cashOut = useDesk((s) => s.cashOut);
  const reset = useDesk((s) => s.reset);
  const stats = deskStats(ledger, marks);
  const [amount, setAmount] = useState("1000");
  const [note, setNote] = useState<string | null>(null);
  const n = Number(amount);

  return (
    <div className="min-h-dvh bg-bg text-fg">
      <Shell marks={marks} />
      <main className="mx-auto max-w-lg px-3 py-4">
        <h1 className="text-lg font-semibold">Vault</h1>
        <p className="mt-1 text-sm text-muted">
          Desk collateral in USDT. Deposits credit the wallet that margin checks use. Withdrawals cannot pull margin locked in positions or resting orders. Connecting a wallet proves an address; it does not move chain funds yet.
        </p>
        <div className="mt-4 rounded-sm border border-line bg-surface p-4">
          <div className="text-xs text-muted">Wallet balance</div>
          <div className="num mt-1 text-2xl">{fmtUsd(ledger.wallet)}</div>
          <div className="mt-2 text-xs text-muted">Free after margin {fmtUsd(Math.max(0, stats.available))}</div>
          <label className="mt-4 block text-xs text-muted">
            Amount
            <input value={amount} onChange={(e) => setAmount(e.target.value)} inputMode="decimal" className="num mt-1 w-full rounded-sm border border-line bg-bg px-3 py-2 text-sm text-fg" />
          </label>
          <div className="mt-3 grid grid-cols-2 gap-2">
            <button
              type="button"
              className="rounded-sm bg-up py-2 text-sm font-semibold text-bg"
              onClick={() => setNote(fund(n) ?? `Deposited ${fmtUsd(n)}`)}
            >
              Deposit
            </button>
            <button
              type="button"
              className="rounded-sm border border-line py-2 text-sm"
              onClick={() => setNote(cashOut(n, marks) ?? `Withdrew ${fmtUsd(n)}`)}
            >
              Withdraw
            </button>
          </div>
          {note ? <p className="mt-3 text-xs text-accent">{note}</p> : null}
        </div>
        <button type="button" onClick={() => { reset(); setNote("Desk reset to 25,000 USDT."); }} className="mt-4 text-xs text-muted underline">
          Reset desk
        </button>
      </main>
    </div>
  );
}
