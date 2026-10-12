"use client";

import { useCallback, useState } from "react";
import { Header } from "@/components/layout/Header";
import { CandleChart } from "@/components/trade/CandleChart";
import { OrderBookPanel } from "@/components/trade/OrderBook";
import { OrderForm } from "@/components/trade/OrderForm";
import { TradesTape } from "@/components/trade/TradesTape";
import { BottomPanel } from "@/components/trade/BottomPanel";
import { useOrderBook } from "@/hooks/useOrderBook";
import { getMarket } from "@/lib/markets";
import {
  DEMO_HISTORY,
  DEMO_ORDERS,
  DEMO_POSITIONS,
} from "@/lib/mockData";
import type { OpenOrder, Position, TradeHistoryItem } from "@/lib/types";

export function TradingTerminal({ symbol }: { symbol: string }) {
  const market = getMarket(symbol);
  const {
    bids,
    asks,
    trades,
    ticker,
    mark,
    connected,
    statusMsg,
    spread,
    mid,
  } = useOrderBook(market.marketId, 95000);

  const [orders, setOrders] = useState<OpenOrder[]>(DEMO_ORDERS);
  const [positions, setPositions] = useState<Position[]>(DEMO_POSITIONS);
  const [history] = useState<TradeHistoryItem[]>(DEMO_HISTORY);
  const [toast, setToast] = useState<string | null>(null);

  const lastPrice = ticker?.lastPrice ?? mark?.markPrice ?? mid;
  const change24h = ticker?.priceChange24h ?? 0;

  const showToast = useCallback((msg: string) => {
    setToast(msg);
    setTimeout(() => setToast(null), 3500);
  }, []);

  const onCancelOrder = (id: number) => {
    setOrders((prev) => prev.filter((o) => o.orderId !== id));
    showToast(`Cancelled order #${id}`);
  };

  const onClosePosition = (sym: string) => {
    setPositions((prev) => prev.filter((p) => p.symbol !== sym));
    showToast(`Closing position ${sym}`);
  };

  const onOrderPlaced = (msg: string) => {
    showToast(msg);
    // Optimistic local open order for demo
    const match = msg.match(/(Buy|Sell).*?([\d.]+).*?@\s*([\d.]+|MARKET|MKT)/i);
    if (match) {
      const side = match[1] as "Buy" | "Sell";
      const qty = parseFloat(match[2]);
      const price =
        match[3] === "MARKET" || match[3] === "MKT"
          ? lastPrice
          : parseFloat(match[3]);
      setOrders((prev) => [
        {
          orderId: Date.now(),
          clientOrderId: Date.now(),
          marketId: market.marketId,
          symbol: market.symbol,
          side,
          type: match[3] === "MARKET" || match[3] === "MKT" ? "Market" : "Limit",
          price,
          quantity: qty,
          filledQuantity: 0,
          remainingQuantity: qty,
          status: "New",
          createdAt: Date.now(),
        },
        ...prev,
      ]);
    }
  };

  return (
    <div className="h-screen flex flex-col overflow-hidden bg-surface">
      <Header
        symbol={market.symbol}
        lastPrice={lastPrice}
        change24h={change24h}
        connected={connected}
        statusMsg={statusMsg}
      />

      {/* Main grid */}
      <div className="flex-1 grid grid-cols-12 grid-rows-[1fr_220px] min-h-0">
        {/* Chart */}
        <div className="col-span-12 lg:col-span-6 xl:col-span-7 row-span-1 border-r border-border min-h-0">
          <CandleChart
            symbol={market.symbol}
            lastPrice={lastPrice}
            trades={trades}
          />
        </div>

        {/* Order book + trades */}
        <div className="hidden lg:flex col-span-3 xl:col-span-2 row-span-1 border-r border-border flex-col min-h-0">
          <div className="flex-1 min-h-0 border-b border-border">
            <OrderBookPanel
              bids={bids}
              asks={asks}
              spread={spread}
              mid={mid}
            />
          </div>
          <div className="h-[40%] min-h-[120px]">
            <div className="px-3 py-1.5 text-[11px] uppercase tracking-wide text-muted border-b border-border">
              Recent trades
            </div>
            <div className="h-[calc(100%-28px)]">
              <TradesTape trades={trades} />
            </div>
          </div>
        </div>

        {/* Order form */}
        <div className="hidden lg:block col-span-3 row-span-1 min-h-0">
          <OrderForm
            market={market}
            midPrice={lastPrice}
            onOrderPlaced={onOrderPlaced}
          />
        </div>

        {/* Bottom panel */}
        <div className="col-span-12 row-span-1 min-h-0">
          <BottomPanel
            positions={positions}
            orders={orders}
            history={history}
            onCancelOrder={onCancelOrder}
            onClosePosition={onClosePosition}
          />
        </div>
      </div>

      {/* Funding / mark strip */}
      <div className="h-7 border-t border-border bg-surface-50 flex items-center px-4 gap-6 text-[11px] text-muted font-mono shrink-0">
        <span>
          Mark{" "}
          <span className="text-white">
            {(mark?.markPrice ?? lastPrice).toLocaleString(undefined, {
              maximumFractionDigits: 2,
            })}
          </span>
        </span>
        <span>
          Index{" "}
          <span className="text-white">
            {(mark?.indexPrice ?? lastPrice).toLocaleString(undefined, {
              maximumFractionDigits: 2,
            })}
          </span>
        </span>
        <span>
          Funding{" "}
          <span className="text-bid">
            {((mark?.fundingRate ?? 0.0001) * 100).toFixed(4)}%
          </span>
        </span>
        <span className="ml-auto">DexCore Institutional · Perpetuals</span>
      </div>

      {toast && (
        <div className="fixed bottom-10 right-4 z-50 bg-surface-200 border border-border px-4 py-2.5 rounded-lg shadow-xl text-sm max-w-sm">
          {toast}
        </div>
      )}
    </div>
  );
}
