window.dexChart = {
  _charts: {},

  init: function (elementId, candles, levels) {
    const el = document.getElementById(elementId);
    if (!el || typeof LightweightCharts === "undefined") return false;

    if (this._charts[elementId]) {
      this._charts[elementId].chart.remove();
      delete this._charts[elementId];
    }

    const chart = LightweightCharts.createChart(el, {
      layout: {
        background: { type: "solid", color: "#0b0e11" },
        textColor: "#5a6b80"
      },
      grid: {
        vertLines: { color: "#0e1620" },
        horzLines: { color: "#0e1620" }
      },
      crosshair: {
        mode: 0,
        vertLine: { color: "rgba(0,255,136,0.25)", labelBackgroundColor: "#0a1018" },
        horzLine: { color: "rgba(0,255,136,0.25)", labelBackgroundColor: "#0a1018" }
      },
      rightPriceScale: { borderColor: "#15202e", scaleMargins: { top: 0.08, bottom: 0.12 } },
      timeScale: { borderColor: "#15202e", timeVisible: true },
      width: el.clientWidth,
      height: el.clientHeight || 420
    });

    const series = chart.addCandlestickSeries({
      upColor: "#0ecb81",
      downColor: "#1a2838",
      borderUpColor: "#0ecb81",
      borderDownColor: "#5a6b80",
      wickUpColor: "#0ecb81",
      wickDownColor: "#5a6b80"
    });

    if (candles && candles.length) {
      series.setData(candles);
      chart.timeScale().fitContent();
    }

    const priceLines = [];
    if (levels) {
      if (levels.takeProfit) {
        priceLines.push(series.createPriceLine({
          price: levels.takeProfit,
          color: "#0ecb81",
          lineWidth: 2,
          lineStyle: 0,
          axisLabelVisible: true,
          title: "TP"
        }));
      }
      if (levels.stopLoss) {
        priceLines.push(series.createPriceLine({
          price: levels.stopLoss,
          color: "#f0b90b",
          lineWidth: 2,
          lineStyle: 2,
          axisLabelVisible: true,
          title: "SL"
        }));
      }
      if (levels.entry) {
        priceLines.push(series.createPriceLine({
          price: levels.entry,
          color: "#00d4ff",
          lineWidth: 1,
          lineStyle: 3,
          axisLabelVisible: true,
          title: "ENTRY"
        }));
      }
    }

    const ro = new ResizeObserver(() => {
      chart.applyOptions({ width: el.clientWidth, height: el.clientHeight || 420 });
    });
    ro.observe(el);

    this._charts[elementId] = { chart, series, ro, priceLines };
    return true;
  },

  setLevels: function (elementId, levels) {
    const entry = this._charts[elementId];
    if (!entry) return;
    (entry.priceLines || []).forEach(pl => { try { entry.series.removePriceLine(pl); } catch (_) {} });
    entry.priceLines = [];
    if (!levels) return;
    const add = (price, color, style, title) => {
      if (!price) return;
      entry.priceLines.push(entry.series.createPriceLine({
        price, color, lineWidth: 2, lineStyle: style, axisLabelVisible: true, title
      }));
    };
    add(levels.takeProfit, "#0ecb81", 0, "TP");
    add(levels.stopLoss, "#f0b90b", 2, "SL");
    add(levels.entry, "#00d4ff", 3, "ENTRY");
  },

  updateLast: function (elementId, candle) {
    const entry = this._charts[elementId];
    if (!entry || !candle) return;
    try { entry.series.update(candle); } catch (_) {}
  },

  dispose: function (elementId) {
    const entry = this._charts[elementId];
    if (!entry) return;
    entry.ro.disconnect();
    entry.chart.remove();
    delete this._charts[elementId];
  },

  generateSeedCandles: function (basePrice, count) {
    const now = Math.floor(Date.now() / 1000);
    const candles = [];
    let price = basePrice || 95000;
    for (let i = count; i > 0; i--) {
      const open = price;
      const change = (Math.random() - 0.46) * basePrice * 0.0035;
      const close = Math.max(100, open + change);
      const high = Math.max(open, close) * (1 + Math.random() * 0.0012);
      const low = Math.min(open, close) * (1 - Math.random() * 0.0012);
      candles.push({ time: now - i * 60, open, high, low, close });
      price = close;
    }
    return candles;
  }
};
