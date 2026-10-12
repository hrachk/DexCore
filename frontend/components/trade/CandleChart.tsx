"use client";

import { useEffect, useRef } from "react";
import {
  createChart,
  type IChartApi,
  type ISeriesApi,
  type CandlestickData,
  ColorType,
  CrosshairMode,
} from "lightweight-charts";
import { generateCandles } from "@/lib/mockData";
import type { TradeTick } from "@/lib/types";

export function CandleChart({
  symbol,
  lastPrice,
  trades,
}: {
  symbol: string;
  lastPrice: number;
  trades: TradeTick[];
}) {
  const containerRef = useRef<HTMLDivElement>(null);
  const chartRef = useRef<IChartApi | null>(null);
  const seriesRef = useRef<ISeriesApi<"Candlestick"> | null>(null);

  useEffect(() => {
    if (!containerRef.current) return;

    const chart = createChart(containerRef.current, {
      layout: {
        background: { type: ColorType.Solid, color: "#0b0e11" },
        textColor: "#848e9c",
      },
      grid: {
        vertLines: { color: "#1a1f27" },
        horzLines: { color: "#1a1f27" },
      },
      crosshair: { mode: CrosshairMode.Normal },
      rightPriceScale: { borderColor: "#2a3340" },
      timeScale: { borderColor: "#2a3340", timeVisible: true },
      width: containerRef.current.clientWidth,
      height: containerRef.current.clientHeight,
    });

    const series = chart.addCandlestickSeries({
      upColor: "#0ecb81",
      downColor: "#f6465d",
      borderUpColor: "#0ecb81",
      borderDownColor: "#f6465d",
      wickUpColor: "#0ecb81",
      wickDownColor: "#f6465d",
    });

    const candles = generateCandles(lastPrice || 95000, 180, 60);
    series.setData(
      candles.map((c) => ({
        time: c.time as CandlestickData["time"],
        open: c.open,
        high: c.high,
        low: c.low,
        close: c.close,
      }))
    );
    chart.timeScale().fitContent();

    chartRef.current = chart;
    seriesRef.current = series;

    const ro = new ResizeObserver(() => {
      if (containerRef.current && chartRef.current) {
        chartRef.current.applyOptions({
          width: containerRef.current.clientWidth,
          height: containerRef.current.clientHeight,
        });
      }
    });
    ro.observe(containerRef.current);

    return () => {
      ro.disconnect();
      chart.remove();
      chartRef.current = null;
      seriesRef.current = null;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [symbol]);

  // Update last candle on new trades
  useEffect(() => {
    if (!seriesRef.current || trades.length === 0) return;
    const last = trades[0];
    const t = Math.floor(Date.now() / 1000 / 60) * 60;
    try {
      seriesRef.current.update({
        time: t as CandlestickData["time"],
        open: last.price,
        high: last.price,
        low: last.price,
        close: last.price,
      });
    } catch {
      /* ignore out-of-order */
    }
  }, [trades]);

  return (
    <div className="relative w-full h-full min-h-[320px] bg-surface">
      <div className="absolute top-2 left-3 z-10 text-xs text-muted font-medium">
        {symbol} · 1m
      </div>
      <div ref={containerRef} className="w-full h-full" />
    </div>
  );
}
