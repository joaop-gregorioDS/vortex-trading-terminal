import { useEffect, useRef, useState } from "react";
import type { ISeriesApi } from "lightweight-charts";
import type { Theme } from "../bindings/Theme";
import { coordinateDraft, priceScale, type ChartLine } from "../trading";
import { adaptCandles, chartMessage, decimalForDrawing, type DrawingCandle } from "./drawing";

type Series = ISeriesApi<"Candlestick">;

export function CandleChart({
  interval,
  barCount,
  candles,
  theme,
  timeVisible,
  lines = [],
  protectArmed = false,
  onRevise,
  onProtect,
}: {
  interval: string;
  barCount: number;
  candles: DrawingCandle[];
  theme: Theme;
  timeVisible: boolean;
  lines?: ChartLine[];
  protectArmed?: boolean;
  onRevise?: (orderId: string, price: string) => void;
  onProtect?: (price: string) => void;
}) {
  const frame = useRef<HTMLDivElement>(null);
  const seriesRef = useRef<Series | null>(null);
  const linesRef = useRef(lines);
  const armedRef = useRef(protectArmed);
  const reviseRef = useRef(onRevise);
  const protectRef = useRef(onProtect);
  linesRef.current = lines;
  armedRef.current = protectArmed;
  reviseRef.current = onRevise;
  protectRef.current = onProtect;
  const [epoch, setEpoch] = useState(0);
  const [note, setNote] = useState<string | null>(null);

  useEffect(() => {
    const element = frame.current;
    if (!element) return;
    const host = element;
    let disposed = false;
    let remove: (() => void) | undefined;

    async function mount() {
      try {
        const drawing = adaptCandles(interval, barCount, candles);
        const { CandlestickSeries, ColorType, createChart } = await import("lightweight-charts");
        if (disposed || !frame.current) return;
        const paint = (name: string, fallback: string) => {
          const value = getComputedStyle(document.documentElement).getPropertyValue(name).trim();
          return value || fallback;
        };
        const up = paint("--candle-up", "#e0a24a");
        const down = paint("--candle-down", "#c4726a");
        const chart = createChart(host, {
          autoSize: true,
          layout: {
            background: { type: ColorType.Solid, color: paint("--chart-bg", "#12161c") },
            textColor: paint("--chart-text", "#b7b1a6"),
            attributionLogo: true,
          },
          grid: {
            vertLines: { color: paint("--chart-grid", "#1d232c") },
            horzLines: { color: paint("--chart-grid", "#1d232c") },
          },
          rightPriceScale: { borderColor: paint("--chart-border", "#2a313c") },
          timeScale: {
            borderColor: paint("--chart-border", "#2a313c"),
            timeVisible,
            secondsVisible: false,
          },
          localization: { locale: "pt-BR" },
        });
        remove = () => chart.remove();
        const series = chart.addSeries(CandlestickSeries, {
          upColor: up,
          downColor: down,
          wickUpColor: up,
          wickDownColor: down,
          borderVisible: false,
        });
        series.setData(drawing);
        chart.timeScale().fitContent();
        seriesRef.current = series;
        setEpoch((value) => value + 1);
        setNote(null);
      } catch (error) {
        remove?.();
        remove = undefined;
        seriesRef.current = null;
        if (!disposed) setNote(chartMessage(error));
      }
    }

    void mount();
    return () => {
      disposed = true;
      seriesRef.current = null;
      remove?.();
    };
  }, [interval, barCount, candles, theme, timeVisible]);

  useEffect(() => {
    const series = seriesRef.current;
    const host = frame.current;
    if (!series || !host) return;
    const paint = (name: string, fallback: string) =>
      getComputedStyle(document.documentElement).getPropertyValue(name).trim() || fallback;
    const up = paint("--candle-up", "#e0a24a");
    const down = paint("--candle-down", "#c4726a");
    const drawn = lines.flatMap((line) => {
      try {
        const price = decimalForDrawing(line.price);
        return [
          series.createPriceLine({
            id: line.id,
            price,
            color: line.title === "Stop" ? down : up,
            lineWidth: 1,
            lineStyle: (line.title === "Stop" ? 2 : 0) as import("lightweight-charts").LineStyle,
            axisLabelVisible: true,
            title: line.title,
          }),
        ];
      } catch {
        return [];
      }
    });
    return () => {
      for (const line of drawn) series.removePriceLine(line);
    };
  }, [lines, epoch, theme]);

  useEffect(() => {
    const series = seriesRef.current;
    const host = frame.current;
    if (!series || !host) return;
    let drag: { id: string | null; scale: number } | null = null;

    const onDown = (event: PointerEvent) => {
      if (event.button !== 0) return;
      const y = event.clientY - host.getBoundingClientRect().top;
      let nearest: { id: string; scale: number; distance: number } | null = null;
      for (const line of linesRef.current) {
        let price: number;
        try {
          price = decimalForDrawing(line.price);
        } catch {
          continue;
        }
        const coordinate = series.priceToCoordinate(price);
        if (coordinate === null) continue;
        const distance = Math.abs(coordinate - y);
        if (distance <= 8 && (nearest === null || distance < nearest.distance)) {
          nearest = { id: line.id, scale: priceScale(line.price), distance };
        }
      }
      if (nearest && reviseRef.current) {
        drag = { id: nearest.id, scale: nearest.scale };
        event.stopPropagation();
        event.preventDefault();
        host.setPointerCapture(event.pointerId);
        return;
      }
      if (armedRef.current && protectRef.current) {
        drag = { id: null, scale: 2 };
        event.stopPropagation();
        event.preventDefault();
        host.setPointerCapture(event.pointerId);
      }
    };

    const onUp = (event: PointerEvent) => {
      const current = drag;
      drag = null;
      if (!current) return;
      const y = event.clientY - host.getBoundingClientRect().top;
      const price = series.coordinateToPrice(y);
      if (price === null) return;
      const draft = coordinateDraft(price, current.scale);
      if (!draft) return;
      if (current.id) reviseRef.current?.(current.id, draft);
      else protectRef.current?.(draft);
    };

    host.addEventListener("pointerdown", onDown, true);
    host.addEventListener("pointerup", onUp);
    return () => {
      host.removeEventListener("pointerdown", onDown, true);
      host.removeEventListener("pointerup", onUp);
    };
  }, [epoch]);

  return (
    <div className="chart-frame">
      <div ref={frame} className="chart" />
      {note ? <p className="chart-note">{note}</p> : null}
    </div>
  );
}
