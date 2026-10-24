import { Link, useRouterState } from "@tanstack/react-router";
import { useEffect, useState } from "react";
import { deskStats, useDesk } from "@/lib/exchange/store";

const LINKS = [
  { to: "/", label: "Trade" },
  { to: "/markets", label: "Markets" },
  { to: "/portfolio", label: "Portfolio" },
  { to: "/vault", label: "Vault" },
] as const;

type Eth = { request: (a: { method: string }) => Promise<string[]> };

export function Shell({ marks }: { marks: Record<string, number> }) {
  const path = useRouterState({ select: (s) => s.location.pathname });
  const ledger = useDesk((s) => s.ledger);
  const setHydrated = useDesk((s) => s.setHydrated);
  const stats = deskStats(ledger, marks);
  const [address, setAddress] = useState<string | null>(null);
  const [walletNote, setWalletNote] = useState<string | null>(null);

  useEffect(() => {
    void Promise.resolve(useDesk.persist.rehydrate()).then(() => setHydrated());
  }, [setHydrated]);

  async function connect() {
    const eth = (window as unknown as { ethereum?: Eth }).ethereum;
    if (!eth) {
      setWalletNote("No wallet in this browser. Trading still runs on the desk balance.");
      return;
    }
    try {
      const acc = await eth.request({ method: "eth_requestAccounts" });
      setAddress(acc[0] ?? null);
      setWalletNote(null);
    } catch {
      setWalletNote("Wallet request was rejected.");
    }
  }

  return (
    <header className="relative flex h-12 shrink-0 items-center gap-4 border-b border-line bg-surface px-3">
      <Link to="/" className="flex items-center gap-2">
        <span className="grid size-6 place-items-center rounded-sm bg-accent text-xs font-semibold text-bg">D</span>
        <span className="text-sm font-semibold tracking-wide">DexCore</span>
        <span className="hidden text-xs text-muted sm:inline">Perps</span>
      </Link>
      <nav className="flex items-center gap-1">
        {LINKS.map((l) => {
          const on = l.to === "/" ? path === "/" : path.startsWith(l.to);
          return (
            <Link
              key={l.to}
              to={l.to}
              className={
                "rounded-sm px-2 py-1 text-sm " + (on ? "bg-raised text-fg" : "text-muted hover:text-fg")
              }
            >
              {l.label}
            </Link>
          );
        })}
      </nav>
      <div className="ml-auto flex items-center gap-4 text-xs">
        <div className="hidden items-center gap-3 sm:flex">
          <span className="text-muted">
            Equity <span className="num text-fg">{stats.equity.toLocaleString("en-US", { maximumFractionDigits: 2 })}</span>
          </span>
          <span className="text-muted">
            Free <span className="num text-fg">{Math.max(0, stats.available).toLocaleString("en-US", { maximumFractionDigits: 2 })}</span>
          </span>
        </div>
        <button
          type="button"
          onClick={() => void connect()}
          className="rounded-sm border border-line bg-raised px-3 py-1.5 text-xs text-fg"
        >
          {address ? `${address.slice(0, 6)}…${address.slice(-4)}` : "Connect wallet"}
        </button>
      </div>
      {walletNote ? (
        <p className="absolute left-0 right-0 top-12 z-20 border-b border-line bg-raised px-3 py-1 text-xs text-muted">
          {walletNote}
        </p>
      ) : null}
    </header>
  );
}
