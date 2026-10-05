"use client";

import { useEffect, useRef, useState } from "react";
import { adaptSnapshot } from "@/lib/charts/adapter";
import type { ChartSnapshot } from "@/lib/charts/transport";

export function HistoricalChart({ snapshot }: { snapshot: ChartSnapshot }) {
  const container = useRef<HTMLDivElement>(null);
  const [phase, setPhase] = useState<"loading" | "ready" | "error">("loading");
  useEffect(() => {
    if (!container.current || snapshot.candles.length === 0) return;
    const element = container.current;
    let disposed = false;
    let remove: (() => void) | undefined;
    async function mount() {
      try {
        const data = adaptSnapshot(snapshot);
        const { createChart, CandlestickSeries, LineSeries, HistogramSeries, ColorType } = await import("lightweight-charts");
        if (disposed) return;
        const chart = createChart(element, {
          autoSize: true,
          layout: { background: { type: ColorType.Solid, color: "#0a111e" }, textColor: "#8493ab", attributionLogo: true },
          grid: { vertLines: { color: "#142034" }, horzLines: { color: "#142034" } },
          rightPriceScale: { borderColor: "#253147" },
          timeScale: { borderColor: "#253147", timeVisible: true, secondsVisible: false },
          localization: { locale: "pt-BR" },
        });
        remove = () => chart.remove();
        chart.addSeries(CandlestickSeries, {
          upColor: "#35cbaa", downColor: "#ee7180", wickUpColor: "#35cbaa", wickDownColor: "#ee7180", borderVisible: false,
        }, 0).setData(data.candles);
        chart.addSeries(LineSeries, { color: "#f1c76a", lineWidth: 2, priceLineVisible: false }, 0).setData(data.sma);
        chart.addSeries(HistogramSeries, { priceLineVisible: false }, 1).setData(
          data.histogram.map((point) => ({ ...point, color: point.value >= 0 ? "#237d70" : "#984957" })),
        );
        chart.addSeries(LineSeries, { color: "#79a8ff", lineWidth: 2, priceLineVisible: false }, 1).setData(data.macd);
        chart.addSeries(LineSeries, { color: "#d19bff", lineWidth: 1, priceLineVisible: false }, 1).setData(data.signal);
        chart.panes()[1].setHeight(170);
        chart.timeScale().fitContent();
        setPhase("ready");
      } catch {
        remove?.();
        remove = undefined;
        if (!disposed) setPhase("error");
      }
    }
    void mount();
    return () => { disposed = true; remove?.(); };
  }, [snapshot]);

  if (snapshot.candles.length === 0) {
    return <p className="rounded-xl border border-slate-800 p-8 text-slate-400">Nenhuma barra disponível neste histórico.</p>;
  }
  return (
    <section aria-label="Gráfico histórico de preço e MACD" className="overflow-hidden rounded-xl border border-slate-800 bg-[#0a111e]">
      <div className="flex flex-wrap items-center justify-between gap-3 border-b border-slate-800 px-5 py-4 text-xs">
        <span className="font-medium tracking-wider text-slate-300">VORTEX DEMO <span className="ml-3 text-slate-500">1 MIN · FECHAMENTO UTC</span></span>
        <span className="flex gap-4"><span className="text-[#f1c76a]">SMA 20</span><span className="text-[#79a8ff]">MACD 12/26/9</span><span className="text-[#d19bff]">SIGNAL</span></span>
      </div>
      <div className="relative">
        <div ref={container} className="h-[520px] w-full sm:h-[600px]" data-chart-state={phase}
          data-candle-count={snapshot.candles.length} data-sma-count={snapshot.indicators[0]?.series[0]?.points.length}
          data-macd-count={snapshot.indicators[1]?.series[0]?.points.length} />
        {phase !== "ready" && <p role="status" className="absolute inset-0 flex items-center justify-center bg-[#0a111e] px-6 text-sm text-slate-400">
          {phase === "loading" ? "Preparando gráfico…" : "Não foi possível desenhar este histórico."}
        </p>}
      </div>
      <div className="flex flex-wrap justify-between gap-2 border-t border-slate-800 px-5 py-3 text-xs text-slate-500">
        <span>Histórico estático · {snapshot.context.barCount} barras · MACD no painel inferior</span>
        <span><a href="https://www.tradingview.com/" target="_blank" rel="noreferrer" className="text-slate-400 underline underline-offset-4">TradingView Lightweight Charts™</a> · Copyright (с) 2025 TradingView, Inc.</span>
      </div>
    </section>
  );
}
