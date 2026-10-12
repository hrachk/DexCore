"use client";

import Link from "next/link";
import { Activity, ChevronDown } from "lucide-react";
import { ConnectButton } from "@rainbow-me/rainbowkit";
import { MARKETS } from "@/lib/markets";

export function Header({
  symbol,
  lastPrice,
  change24h,
  connected,
  statusMsg,
}: {
  symbol: string;
  lastPrice: number;
  change24h: number;
  connected: boolean;
  statusMsg: string;
}) {
  const positive = change24h >= 0;

  return (
    <header className="h-14 border-b border-border bg-surface-50 flex items-center px-4 gap-6 shrink-0">
      <Link href="/" className="flex items-center gap-2 font-semibold tracking-tight">
        <div className="w-7 h-7 rounded-md bg-accent flex items-center justify-center text-xs font-bold">
          DX
        </div>
        <span className="hidden sm:inline">DexCore</span>
      </Link>

      <div className="relative group">
        <button className="flex items-center gap-1.5 px-3 py-1.5 rounded-md bg-surface-100 hover:bg-surface-200 text-sm font-medium">
          {symbol}
          <ChevronDown className="w-3.5 h-3.5 text-muted" />
        </button>
        <div className="absolute top-full left-0 mt-1 hidden group-hover:block z-50 min-w-[160px] bg-surface-100 border border-border rounded-md shadow-xl py-1">
          {MARKETS.map((m) => (
            <Link
              key={m.symbol}
              href={`/trade/${m.symbol}`}
              className={`block px-3 py-2 text-sm hover:bg-surface-200 ${
                m.symbol === symbol ? "text-accent" : ""
              }`}
            >
              {m.symbol}
            </Link>
          ))}
        </div>
      </div>

      <div className="flex items-baseline gap-3">
        <span
          className={`text-xl font-semibold font-mono ${
            positive ? "text-bid" : "text-ask"
          }`}
        >
          {lastPrice.toLocaleString("en-US", {
            minimumFractionDigits: 2,
            maximumFractionDigits: 2,
          })}
        </span>
        <span
          className={`text-sm font-mono ${
            positive ? "text-bid" : "text-ask"
          }`}
        >
          {positive ? "+" : ""}
          {change24h.toFixed(2)}%
        </span>
      </div>

      <div className="ml-auto flex items-center gap-3">
        <div className="flex items-center gap-1.5 text-xs text-muted">
          <Activity
            className={`w-3.5 h-3.5 ${
              connected ? "text-bid" : "text-ask"
            }`}
          />
          <span>{statusMsg}</span>
        </div>
        <ConnectButton
          chainStatus="icon"
          accountStatus="address"
          showBalance={false}
        />
      </div>
    </header>
  );
}
