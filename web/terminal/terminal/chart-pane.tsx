import { useEffect, useRef } from "react";
import type { Candle } from "@/lib/exchange/types";

type Props = { candles: Candle[]; symbol: string };

export function ChartPane({ candles, symbol }: Props) {
  const host = useRef<HTMLDivElement>(null);
  const seriesRef = useRef<{ setData: (d: { time: number; open: number; high: number; low: number; close: number }[]) => void } | null>(null);

  useEffect(() => {
    const el = host.current;
    if (!el) return;
    let dead = false;
    let remove = () => {};
    void (async () => {
      const lc = await import("lightweight-charts");
      if (dead || !host.current) return;
      const chart = lc.createChart(host.current, {
        autoSize: true,
        layout: {
          background: { color: "#07090d" },
          textColor: "#8b97a8",
          fontFamily: "IBM Plex Sans, sans-serif",
        },
        grid: {
          vertLines: { color: "#1e2733" },
          horzLines: { color: "#1e2733" },
        },
        rightPriceScale: { borderColor: "#1e2733" },
        timeScale: { borderColor: "#1e2733", timeVisible: true, secondsVisible: false },
        crosshair: { mode: lc.CrosshairMode.Normal },
      });
      const series = chart.addCandlestickSeries({
        upColor: "#3dd68c",
        downColor: "#ff5d73",
        borderVisible: false,
        wickUpColor: "#3dd68c",
        wickDownColor: "#ff5d73",
      });
      seriesRef.current = {
        setData: (d) => series.setData(d as never),
      };
      series.setData(
        candles.map((c) => ({
          time: c.time as never,
          open: c.open,
          high: c.high,
          low: c.low,
          close: c.close,
        })),
      );
      chart.timeScale().fitContent();
      remove = () => chart.remove();
    })();
    return () => {
      dead = true;
      seriesRef.current = null;
      remove();
    };
    // Recreate when the market changes; live updates go through setData.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [symbol]);

  useEffect(() => {
    seriesRef.current?.setData(
      candles.map((c) => ({
        time: c.time as never,
        open: c.open,
        high: c.high,
        low: c.low,
        close: c.close,
      })),
    );
  }, [candles]);

  return <div ref={host} className="h-full min-h-64 w-full overflow-hidden" />;
}
