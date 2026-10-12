"use client";

import { useEffect, useRef, useState } from "react";
import type { IChartApi, UTCTimestamp } from "lightweight-charts";
import { decimalForDrawing } from "@/lib/charts/adapter";
import { formatChartCrosshair, formatChartPrice, formatChartTick, groupDecimal } from "@/lib/trading/format";
import { useTerminal } from "@/lib/trading/terminal-context";
import { fetchYahooHistory, type YahooHistoryTransport } from "@/lib/trading/transport";

const ASSETS = [
  "WING26",
  "WDOU26",
  "PETR4",
  "VALE3",
  "ITUB4",
  "BBDC4",
  "BBAS3",
  "ABEV3",
  "MGLU3",
  "RENT3",
  "WEGE3",
  "PRIO3",
  "B3SA3",
  "SUZB3",
  "GGBR4",
] as const;

const INTERVALS = [
  { id: "1d", label: "Diário" },
  { id: "1h", label: "1h" },
  { id: "15m", label: "15m" },
  { id: "5m", label: "5m" },
  { id: "1m", label: "1m" },
] as const;

export function HistoricoView() {
  const { activeTicker, setActiveTicker } = useTerminal();
  const [interval, setInterval] = useState<(typeof INTERVALS)[number]["id"]>("1d");
  const [loaded, setLoaded] = useState<{
    ticker: string;
    interval: string;
    history: YahooHistoryTransport | null;
    error: string | null;
  } | null>(null);

  useEffect(() => {
    let active = true;
    void fetchYahooHistory(activeTicker, interval).then((result) => {
      if (!active) return;
      setLoaded({
        ticker: activeTicker,
        interval,
        history: result.ok ? result.value : null,
        error: result.ok ? null : result.error,
      });
    });
    return () => {
      active = false;
    };
  }, [activeTicker, interval]);

  const pending = loaded?.ticker !== activeTicker || loaded?.interval !== interval;
  const choices = (ASSETS as readonly string[]).includes(activeTicker)
    ? ASSETS
    : [activeTicker, ...ASSETS];
  const history = pending ? null : loaded?.history ?? null;
  const error = pending ? null : loaded?.error ?? null;

  return (
    <section className="flex flex-col gap-4">
      <div className="rounded-xl border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-950">
        Série atrasada do Yahoo Finance, sincronizada com o ativo selecionado. O mini-índice usa o
        Ibovespa. O mini-dólar usa o dólar comercial em pontos do mini, porque o Yahoo não lista esses
        contratos. Estes preços não entram no livro nem nas ordens.
      </div>

      <div className="flex flex-wrap items-end gap-3">
        <label className="flex flex-col gap-1 text-xs font-semibold text-stone-600">
          Ativo
          <select
            value={activeTicker}
            onChange={(event) => setActiveTicker(event.target.value)}
            className="rounded-md border border-[#ded8cb] bg-white px-2 py-1.5 text-sm font-bold text-stone-900"
          >
            {choices.map((item) => (
              <option key={item} value={item}>
                {assetLabel(item)}
              </option>
            ))}
          </select>
        </label>
        <div className="flex items-center gap-1 rounded-md bg-[#eae4d5] p-0.5 text-xs font-bold">
          {INTERVALS.map((item) => (
            <button
              key={item.id}
              type="button"
              onClick={() => setInterval(item.id)}
              className={`rounded px-2 py-1 ${
                interval === item.id ? "bg-white text-stone-900 shadow-2xs" : "text-stone-600"
              }`}
            >
              {item.label}
            </button>
          ))}
        </div>
      </div>

      {pending && <p className="text-sm text-stone-500">Lendo {assetLabel(activeTicker)} no Yahoo Finance…</p>}
      {error && <p className="text-sm text-rose-700">{error}</p>}

      {history && (
        <div className="overflow-hidden rounded-xl border border-[#ded8cb] bg-white">
          <div className="flex flex-wrap items-baseline justify-between gap-2 border-b border-[#ded8cb] px-4 py-3 text-xs text-stone-500">
            <span className="font-semibold text-stone-800">
              {history.seriesName || history.ticker} · {history.yahooSymbol} · {history.currency || "BRL"} ·{" "}
              {history.interval}
            </span>
            <span>{history.candles.length} candles · atraso de mercado</span>
          </div>
          {history.note && (
            <p className="border-b border-[#ded8cb] px-4 py-2 text-xs text-stone-600">{history.note}</p>
          )}
          <HistoryChart history={history} />
          <div className="max-h-72 overflow-auto">
            <table className="w-full text-left text-xs">
              <thead className="sticky top-0 bg-[#faf8f4] text-[10px] uppercase tracking-wide text-stone-500">
                <tr>
                  <th className="px-3 py-2 font-semibold">Quando</th>
                  <th className="px-3 py-2 font-semibold">Abertura</th>
                  <th className="px-3 py-2 font-semibold">Máxima</th>
                  <th className="px-3 py-2 font-semibold">Mínima</th>
                  <th className="px-3 py-2 font-semibold">Fechamento</th>
                  <th className="px-3 py-2 font-semibold">Volume</th>
                </tr>
              </thead>
              <tbody>
                {[...history.candles].reverse().map((candle) => (
                  <tr key={candle.openTime} className="border-t border-[#f0ece3] font-mono text-stone-800">
                    <td className="px-3 py-1.5 text-stone-500">{labelFor(candle.openTime, history.interval)}</td>
                    <td className="px-3 py-1.5">{groupDecimal(candle.open)}</td>
                    <td className="px-3 py-1.5">{groupDecimal(candle.high)}</td>
                    <td className="px-3 py-1.5">{groupDecimal(candle.low)}</td>
                    <td className="px-3 py-1.5">{groupDecimal(candle.close)}</td>
                    <td className="px-3 py-1.5">{groupDecimal(candle.volume)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </section>
  );
}

function assetLabel(ticker: string): string {
  if (ticker === "WING26") return "WING26 · Ibovespa";
  if (ticker === "WDOU26") return "WDOU26 · Dólar comercial";
  return ticker;
}

function labelFor(iso: string, interval: string): string {
  const match = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})/.exec(iso);
  if (!match) return iso;
  if (interval === "1d") return `${match[3]}/${match[2]}/${match[1]}`;
  const clock = formatChartCrosshair(Math.floor(Date.parse(iso) / 1000));
  return clock;
}

function HistoryChart({ history }: { history: YahooHistoryTransport }) {
  const containerRef = useRef<HTMLDivElement>(null);
  const chartRef = useRef<IChartApi | null>(null);
  const [failedHistory, setFailedHistory] = useState<YahooHistoryTransport | null>(null);
  const failed = failedHistory === history;

  useEffect(() => {
    const container = containerRef.current;
    if (!container || history.candles.length === 0) return;
    let disposed = false;

    void (async () => {
      try {
        const { createChart, CandlestickSeries, ColorType } = await import("lightweight-charts");
        if (disposed || !containerRef.current) return;
        chartRef.current?.remove();
        const chart = createChart(containerRef.current, {
          autoSize: true,
          height: 360,
          layout: {
            background: { type: ColorType.Solid, color: "#090d16" },
            textColor: "#78879e",
            attributionLogo: false,
          },
          grid: { vertLines: { color: "#131a28" }, horzLines: { color: "#131a28" } },
          rightPriceScale: { borderColor: "#1e293b" },
          timeScale: {
            borderColor: "#1e293b",
            timeVisible: history.interval !== "1d",
            secondsVisible: false,
            tickMarkFormatter: (time: any, tickMarkType: any) =>
              typeof time === "number" ? formatChartTick(time, tickMarkType) : null,
          },
          localization: {
            locale: "pt-BR",
            priceFormatter: (price: number) => formatChartPrice(price, history.decimals),
            timeFormatter: (time: any) => (typeof time === "number" ? formatChartCrosshair(time) : "—"),
          },
        });
        chartRef.current = chart;
        const series = chart.addSeries(CandlestickSeries, {
          upColor: "#089981",
          downColor: "#f23645",
          wickUpColor: "#089981",
          wickDownColor: "#f23645",
          borderVisible: false,
          priceFormat: {
            type: "custom",
            minMove: Number(history.tickSize),
            formatter: (price: number) => formatChartPrice(price, history.decimals),
          },
        });
        series.setData(
          history.candles.map((candle) => ({
            time: Math.floor(Date.parse(candle.openTime) / 1000) as UTCTimestamp,
            open: decimalForDrawing(candle.open),
            high: decimalForDrawing(candle.high),
            low: decimalForDrawing(candle.low),
            close: decimalForDrawing(candle.close),
          })),
        );
        chart.timeScale().fitContent();
      } catch {
        if (!disposed) setFailedHistory(history);
      }
    })();

    return () => {
      disposed = true;
      chartRef.current?.remove();
      chartRef.current = null;
    };
  }, [history]);

  return (
    <div className="relative h-[360px] bg-[#090d16]">
      <div ref={containerRef} className="h-full w-full" />
      {failed && (
        <p className="absolute inset-0 flex items-center justify-center text-xs text-rose-300">
          Não foi possível desenhar esta série.
        </p>
      )}
    </div>
  );
}
