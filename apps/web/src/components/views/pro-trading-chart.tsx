"use client";

import { useEffect, useRef, useState } from "react";
import { useTerminal } from "@/lib/trading/terminal-context";
import { adaptSnapshot, decimalForDrawing } from "@/lib/charts/adapter";
import { absorb, seedPlayback, stepPlayback, type PlayBar, type Playback } from "@/lib/charts/candle-playback";
import type { ChartSnapshot } from "@/lib/charts/transport";
import { formatChartCrosshair, formatChartPrice, formatChartTick, brasiliaFromUnix, groupDecimal } from "@/lib/trading/format";
import {
  cancelOrder,
  fetchYahooHistory,
  placeProtection,
  previewChartOrder,
  reviseOrder,
  submitOrder,
  type ChartPreviewTransport,
  type YahooHistoryTransport,
} from "@/lib/trading/transport";
import { ChartOrderLabels, chartOrderColor, type ChartLabelCommit } from "./chart-order-labels";
import { idleProtection, protectionPayload, ProtectionFields, type ProtectionDraft } from "./protection-fields";
import type { IChartApi, IPriceLine, ISeriesApi, CandlestickData, MouseEventParams, UTCTimestamp } from "lightweight-charts";

type ChartPointerPrice = { draft: string; label: string };
type ChartMenuState = ChartPointerPrice & { x: number; y: number; section: "root" | "time" };
type ChartTicketState = { side: "buy" | "sell"; price: string; quantity: string };

type HoverStats = {
  time: number;
  open: number;
  high: number;
  low: number;
  close: number;
  volume?: number;
};

export function ProTradingChart() {
  const {
    activeTicker,
    activeQuote,
    orders,
    chartTabs,
    activeChartTabId,
    setTimeframeForActiveTab,
    indicatorSettings,
    toggleIndicator,
    refreshAll,
    setChartPriceDraft,
    portfolio,
  } = useTerminal();

  const containerRef = useRef<HTMLDivElement>(null);
  const chartRef = useRef<IChartApi | null>(null);
  const candleSeriesRef = useRef<ISeriesApi<"Candlestick"> | null>(null);
  const smaSeriesRef = useRef<ISeriesApi<"Line"> | null>(null);
  const bollingerUpperRef = useRef<ISeriesApi<"Line"> | null>(null);
  const bollingerMiddleRef = useRef<ISeriesApi<"Line"> | null>(null);
  const bollingerLowerRef = useRef<ISeriesApi<"Line"> | null>(null);
  const vwapSeriesRef = useRef<ISeriesApi<"Line"> | null>(null);
  const seriesDecimalsRef = useRef(2);
  const playbackRef = useRef<Playback | null>(null);
  const priceLinesRef = useRef<IPriceLine[]>([]);
  const linesByIdRef = useRef<Map<string, IPriceLine>>(new Map());
  const lastPointerOrder = useRef(0);
  const tradingRef = useRef<{
    enabled: boolean;
    open: (side: "buy" | "sell", draft: string) => void;
    cancel: (orderId: string) => void;
  }>({
    enabled: true,
    open: () => {},
    cancel: () => {},
  });

  const [phase, setPhase] = useState<"loading" | "ready" | "empty" | "error">("loading");
  const [chartEpoch, setChartEpoch] = useState(0);
  const loadingRef = useRef(false);
  const [hoveredStats, setHoveredStats] = useState<HoverStats | null>(null);
  const [latestStats, setLatestStats] = useState<HoverStats | null>(null);
  const [latestRsi, setLatestRsi] = useState<number | null>(null);
  const [seriesNote, setSeriesNote] = useState<string | null>(null);
  const [indicatorMenuOpen, setIndicatorMenuOpen] = useState(false);
  const [chartTrading, setChartTrading] = useState(true);
  const [menu, setMenu] = useState<ChartMenuState | null>(null);
  const [ticket, setTicket] = useState<ChartTicketState | null>(null);
  const [preview, setPreview] = useState<ChartPreviewTransport | null>(null);
  const [previewError, setPreviewError] = useState<string | null>(null);
  const [ticketBusy, setTicketBusy] = useState(false);
  const [strategy, setStrategy] = useState<ProtectionDraft>(idleProtection);
  const [notice, setNotice] = useState<string | null>(null);

  const activeTab = chartTabs.find((t) => t.id === activeChartTabId) ?? chartTabs[0];
  const timeframe = activeTab?.timeframe ?? "5m";

  // Every catalog asset draws the delayed Yahoo series. The quote, the book and the ticket stay on the simulated tape.
  const assetConfig = getAssetConfig(activeTicker);
  const delayedYahoo = true;

  // Initialize and Mount Chart
  useEffect(() => {
    let disposed = false;

    async function loadAndMount() {
      if (!containerRef.current) return;
      setPhase("loading");

      loadingRef.current = true;
      try {
        let session: ReturnType<typeof adaptSnapshot> | null = null;
        let candles: CandlestickData<UTCTimestamp>[] = [];
        let formingMinutes: PlayBar[] = [];
        let precision = assetConfig.precision;
        let minMove = assetConfig.minMove;
        playbackRef.current = null;

        if (delayedYahoo) {
          const history = await fetchYahooHistory(activeTicker, timeframe);
          if (disposed) return;
          if (!history.ok) {
            setSeriesNote(null);
            setLatestRsi(null);
            setPhase("error");
            return;
          }
          if (history.value.candles.length === 0) {
            setSeriesNote(history.value.note);
            setPhase("empty");
            return;
          }
          precision = history.value.decimals;
          minMove = Number(history.value.tickSize);
          candles = yahooCandles(history.value);
          formingMinutes = playMinutes(history.value);
          setSeriesNote(history.value.note);
          setLatestRsi(null);
        } else {
          const response = await fetch(
            `/api/chart?ticker=${encodeURIComponent(activeTicker)}&interval=${encodeURIComponent(timeframe)}&maxBars=200`,
            { cache: "no-store", headers: { Accept: "application/json" }, signal: AbortSignal.timeout(5000) },
          );
          if (!response.ok) {
            if (!disposed) {
              setSeriesNote(null);
              setPhase("error");
            }
            return;
          }
          const json = await response.json();
          if (!json || json.status === "empty" || !Array.isArray(json.candles) || json.candles.length === 0) {
            if (!disposed) {
              setSeriesNote(null);
              setPhase("empty");
            }
            return;
          }
          if (disposed || !containerRef.current) return;
          session = adaptSnapshot(json as ChartSnapshot);
          candles = session.candles;
          setSeriesNote(null);
        }

        if (disposed || !containerRef.current || candles.length === 0) return;
        seriesDecimalsRef.current = precision;

        const data = session;
        const { createChart, CandlestickSeries, LineSeries, ColorType } = await import(
          "lightweight-charts"
        );
        const priceFormat = {
          type: "custom" as const,
          minMove,
          formatter: (price: number) => formatChartPrice(price, precision),
        };

        if (disposed || !containerRef.current) return;

        // Clean up previous instance
        if (chartRef.current) {
          chartRef.current.remove();
          chartRef.current = null;
        }
        candleSeriesRef.current = null;
        smaSeriesRef.current = null;
        bollingerUpperRef.current = null;
        bollingerMiddleRef.current = null;
        bollingerLowerRef.current = null;
        vwapSeriesRef.current = null;
        containerRef.current.innerHTML = "";

        // Chart surface. Colors are local presentation, not a third-party identity.
        const chart = createChart(containerRef.current, {
          autoSize: true,
          layout: {
            background: { type: ColorType.Solid, color: "#090d16" },
            textColor: "#78879e",
            attributionLogo: false,
          },
          grid: {
            vertLines: { color: "#131a28" },
            horzLines: { color: "#131a28" },
          },
          rightPriceScale: {
            borderColor: "#1e293b",
            visible: true,
            autoScale: true,
          },
          timeScale: {
            borderColor: "#1e293b",
            timeVisible: true,
            secondsVisible: false,
            tickMarkFormatter: (time: any, tickMarkType: any) =>
              typeof time === "number" ? formatChartTick(time, tickMarkType) : null,
          },
          localization: {
            locale: "pt-BR",
            dateFormat: "dd/MM/yyyy",
            priceFormatter: (price: number) => formatChartPrice(price, precision),
            timeFormatter: (time: any) => (typeof time === "number" ? formatChartCrosshair(time) : "—"),
          },
          crosshair: {
            mode: 1, // Magnet crosshair
            vertLine: {
              color: "#334155",
              width: 1,
              style: 3,
              labelBackgroundColor: "#1e293b",
            },
            horzLine: {
              color: "#334155",
              width: 1,
              style: 3,
              labelBackgroundColor: "#1e293b",
            },
          },
        });

        chartRef.current = chart;

        // Candles aggregated by the session.
        const candleSeries = chart.addSeries(CandlestickSeries, {
          upColor: "#089981",
          downColor: "#f23645",
          wickUpColor: "#089981",
          wickDownColor: "#f23645",
          borderUpColor: "#089981",
          borderDownColor: "#f23645",
          borderVisible: true,
          priceFormat: priceFormat,
          priceLineVisible: true,
          priceLineWidth: 1,
          priceLineStyle: 2, // Dashed
          lastValueVisible: true,
        });

        candleSeriesRef.current = candleSeries;
        candleSeries.setData(candles);
        if (candles.length > 0) {
          playbackRef.current = seedPlayback(toPlay(candles[candles.length - 1]), formingMinutes);
        }

        // Session indicators stay on the simulated tape. The Yahoo series is candles only.
        if (data && indicatorSettings.sma && data.sma.length > 0) {
          const smaSeries = chart.addSeries(LineSeries, {
            color: "#f59e0b",
            lineWidth: 2,
            priceLineVisible: false,
            lastValueVisible: true,
            priceFormat,
          });
          smaSeriesRef.current = smaSeries;
          smaSeries.setData(data.sma);
        }

        // 3. Bollinger Bands (20, 2) — Sky Blue Upper/Lower, Slate Middle
        if (data && indicatorSettings.bollinger && data.bollinger.upper.length > 0) {
          const bUpper = chart.addSeries(LineSeries, {
            color: "#38bdf8",
            lineWidth: 1,
            lineStyle: 2,
            priceLineVisible: false,
            lastValueVisible: false,
            priceFormat,
          });
          const bMiddle = chart.addSeries(LineSeries, {
            color: "#64748b",
            lineWidth: 1,
            priceLineVisible: false,
            lastValueVisible: false,
            priceFormat,
          });
          const bLower = chart.addSeries(LineSeries, {
            color: "#38bdf8",
            lineWidth: 1,
            lineStyle: 2,
            priceLineVisible: false,
            lastValueVisible: false,
            priceFormat,
          });

          bUpper.setData(data.bollinger.upper);
          bMiddle.setData(data.bollinger.middle);
          bLower.setData(data.bollinger.lower);

          bollingerUpperRef.current = bUpper;
          bollingerMiddleRef.current = bMiddle;
          bollingerLowerRef.current = bLower;
        }

        // 4. VWAP Intraday — Violet / Purple
        if (data && indicatorSettings.vwap && data.vwap.length > 0) {
          const vwapSeries = chart.addSeries(LineSeries, {
            color: "#c084fc",
            lineWidth: 2,
            priceLineVisible: false,
            lastValueVisible: true,
            priceFormat,
          });
          vwapSeriesRef.current = vwapSeries;
          vwapSeries.setData(data.vwap);
        }

        // 5. RSI (14) value extraction
        if (data && data.rsi.length > 0) {
          setLatestRsi(Number(data.rsi[data.rsi.length - 1].value));
        }

        // Store latest bar stats
        if (candles.length > 0) {
          const last = candles[candles.length - 1];
          setLatestStats({
            time: Number(last.time),
            open: last.open,
            high: last.high,
            low: last.low,
            close: last.close,
          });
        }

        // Crosshair subscription for floating OHLCV tooltip
        chart.subscribeCrosshairMove((param) => {
          if (param.time && param.seriesData) {
            const bar = param.seriesData.get(candleSeries) as CandlestickData<UTCTimestamp> | undefined;
            if (bar && bar.open !== undefined) {
              setHoveredStats({
                time: Number(bar.time),
                open: bar.open,
                high: bar.high,
                low: bar.low,
                close: bar.close,
              });
              return;
            }
          }
          setHoveredStats(null);
        });

        showSession(chart, candles.length, containerRef.current?.clientWidth ?? 0);
        setPhase("ready");
      } catch {
        if (!disposed) {
          setPhase("error");
        }
      } finally {
        loadingRef.current = false;
      }
    }

    void loadAndMount();

    return () => {
      disposed = true;
      if (chartRef.current) {
        chartRef.current.remove();
        chartRef.current = null;
      }
      candleSeriesRef.current = null;
      smaSeriesRef.current = null;
      bollingerUpperRef.current = null;
      bollingerMiddleRef.current = null;
      bollingerLowerRef.current = null;
      vwapSeriesRef.current = null;
    };
  }, [
    activeTicker,
    timeframe,
    assetConfig.precision,
    assetConfig.minMove,
    indicatorSettings.sma,
    indicatorSettings.bollinger,
    indicatorSettings.vwap,
    chartEpoch,
    delayedYahoo,
  ]);

  // The forming candle tracks the quote. A full read picks up minutes the feed has stored.
  useEffect(() => {
    let active = true;

    const intervalTimer = setInterval(async () => {
      if (!active || !candleSeriesRef.current) return;

      try {
        let incoming: CandlestickData<UTCTimestamp>[] = [];
        let formingMinutes: PlayBar[] = [];
        let session: ReturnType<typeof adaptSnapshot> | null = null;
        if (delayedYahoo) {
          const history = await fetchYahooHistory(activeTicker, timeframe);
          if (!history.ok || history.value.candles.length === 0) return;
          incoming = yahooCandles(history.value);
          formingMinutes = playMinutes(history.value);
        } else {
          const response = await fetch(
            `/api/chart?ticker=${encodeURIComponent(activeTicker)}&interval=${encodeURIComponent(timeframe)}&maxBars=200`,
            {
              cache: "no-store",
              headers: { Accept: "application/json" },
              signal: AbortSignal.timeout(5000),
            }
          );

          if (!response.ok) return;
          const json = await response.json();
          if (!json || !Array.isArray(json.candles) || json.candles.length === 0) return;
          session = adaptSnapshot(json as ChartSnapshot);
          incoming = session.candles;
        }
        if (incoming.length === 0) return;
        if (!candleSeriesRef.current) {
          if (!loadingRef.current) setChartEpoch((value) => value + 1);
          return;
        }

        const data = session;
        const latestTime = Number(incoming[incoming.length - 1].time);
        const mounted = candleSeriesRef.current.data();
        const mountedTime = mounted.length > 0 ? Number(mounted[mounted.length - 1].time) : -1;
        if (mountedTime >= 0 && latestTime < mountedTime) {
          if (!loadingRef.current) setChartEpoch((value) => value + 1);
          return;
        }
        const indicatorsNeedMount =
          !!data &&
          ((indicatorSettings.sma && data.sma.length > 0 && !smaSeriesRef.current) ||
            (indicatorSettings.bollinger && data.bollinger.upper.length > 0 && !bollingerUpperRef.current) ||
            (indicatorSettings.vwap && data.vwap.length > 0 && !vwapSeriesRef.current));
        if (indicatorsNeedMount && !loadingRef.current) {
          setChartEpoch((value) => value + 1);
          return;
        }

        if (delayedYahoo) {
          const forming = incoming[incoming.length - 1];
          const current = playbackRef.current ?? seedPlayback(toPlay(forming), formingMinutes);
          const next = absorb(current, toPlay(forming), formingMinutes);
          playbackRef.current = next;
          pulseSeries(candleSeriesRef.current, incoming.slice(0, -1));
          if (current.bucket !== next.bucket) candleSeriesRef.current.update(drawn(next));
        } else {
          pulseSeries(candleSeriesRef.current, incoming);
        }
        if (data && indicatorSettings.sma) pulseSeries(smaSeriesRef.current, data.sma);
        if (data && indicatorSettings.bollinger) {
          pulseSeries(bollingerUpperRef.current, data.bollinger.upper);
          pulseSeries(bollingerMiddleRef.current, data.bollinger.middle);
          pulseSeries(bollingerLowerRef.current, data.bollinger.lower);
        }
        if (data && indicatorSettings.vwap) pulseSeries(vwapSeriesRef.current, data.vwap);
        if (data && data.rsi.length > 0) setLatestRsi(data.rsi[data.rsi.length - 1].value);
        if (!delayedYahoo) {
          const latest = incoming[incoming.length - 1];
          setLatestStats({
            time: Number(latest.time),
            open: latest.open,
            high: latest.high,
            low: latest.low,
            close: latest.close,
          });
        }
      } catch {
        // Silently tolerate temporary polling skips
      }
    }, delayedYahoo ? 3000 : 2500);

    return () => {
      active = false;
      clearInterval(intervalTimer);
    };
  }, [activeTicker, timeframe, indicatorSettings, delayedYahoo]);

  // Replay the cached minute path across the Yahoo delay. The wick grows as the price
  // reaches each extreme. The boleta keeps the official quote; this state is display only.
  useEffect(() => {
    if (!delayedYahoo) return;
    const timer = setInterval(() => {
      const state = playbackRef.current;
      const series = candleSeriesRef.current;
      if (!state || !series || state.cursor >= 1) return;
      const stepped = stepPlayback(state, 200);
      playbackRef.current = stepped;
      series.update(drawn(stepped));
      setLatestStats({
        time: stepped.bucket,
        open: stepped.open,
        high: stepped.high,
        low: stepped.low,
        close: stepped.close,
      });
    }, 200);
    return () => clearInterval(timer);
  }, [delayedYahoo, activeTicker, timeframe, chartEpoch]);

  function priceFromCoordinate(coordinate: number): ChartPointerPrice | null {
    const series = candleSeriesRef.current;
    if (!series || !Number.isFinite(coordinate)) return null;
    const price = series.coordinateToPrice(coordinate);
    if (price === null || !Number.isFinite(price) || price <= 0) return null;
    const decimals = seriesDecimalsRef.current;
    const draft = price.toFixed(decimals);
    return { draft, label: formatChartPrice(price, decimals) };
  }

  async function placeChartClick(side: "buy" | "sell", draft: string) {
    if (!chartTrading || !activeQuote?.instrumentId) return;
    const now = Date.now();
    if (now - lastPointerOrder.current < 500) return;
    lastPointerOrder.current = now;
    setMenu(null);
    setTicket(null);
    setNotice(null);
    const result = await submitOrder({
      instrumentId: activeQuote.instrumentId,
      side,
      type: "limit",
      quantity: "1",
      limitPrice: draft,
      origin: "chart",
    });
    if (result.ok) {
      const name = result.value.side === "buy" ? "Compra" : "Venda";
      const kind = result.value.type === "stop" ? "stop" : "limite";
      const price = result.value.limitPrice ? groupDecimal(result.value.limitPrice) : draft;
      setNotice(`${name} ${kind} inserida em ${price}.`);
      await refreshAll();
    } else {
      setNotice(result.error);
    }
  }

  function openTicket(side: "buy" | "sell", draft: string) {
    if (!chartTrading) return;
    setMenu(null);
    setNotice(null);
    setPreview(null);
    setPreviewError(null);
    setChartPriceDraft(activeTicker, draft);
    setTicket({ side, price: draft, quantity: "1" });
  }

  async function cancelResting(orderId: string) {
    setMenu(null);
    const result = await cancelOrder(orderId);
    setNotice(result.ok ? "Ordem do gráfico cancelada." : result.error);
    await refreshAll();
  }

  const [interactionTicker, setInteractionTicker] = useState(activeTicker);
  if (activeTicker !== interactionTicker) {
    setInteractionTicker(activeTicker);
    setMenu(null);
    setTicket(null);
    setPreview(null);
    setPreviewError(null);
  }

  const previewKey = ticket && chartTrading
    ? `${activeTicker}|${ticket.side}|${ticket.price}|${ticket.quantity}`
    : "";
  const [previewKeySeen, setPreviewKeySeen] = useState(previewKey);
  if (previewKey !== previewKeySeen) {
    setPreviewKeySeen(previewKey);
    setPreview(null);
    setPreviewError(null);
  }

  useEffect(() => {
    tradingRef.current = {
      enabled: chartTrading,
      open: (side, draft) => {
        void placeChartClick(side, draft);
      },
      cancel: (orderId) => {
        void cancelResting(orderId);
      },
    };
  });

  useEffect(() => {
    if (!menu) return;
    const onKey = (event: KeyboardEvent) => {
      if (event.key === "Escape") setMenu(null);
    };
    const onPointer = () => setMenu(null);
    window.addEventListener("keydown", onKey);
    window.addEventListener("mousedown", onPointer);
    return () => {
      window.removeEventListener("keydown", onKey);
      window.removeEventListener("mousedown", onPointer);
    };
  }, [menu]);

  useEffect(() => {
    if (!ticket || !chartTrading) return;
    let cancelled = false;
    const handle = window.setTimeout(async () => {
      const result = await previewChartOrder({
        ticker: activeTicker,
        side: ticket.side,
        price: ticket.price.trim(),
        quantity: ticket.quantity.trim() || "1",
      });
      if (cancelled) return;
      if (result.ok) {
        setPreview(result.value);
        setChartPriceDraft(activeTicker, result.value.price);
      } else {
        setPreview(null);
        setPreviewError(result.error);
      }
    }, 250);
    return () => {
      cancelled = true;
      window.clearTimeout(handle);
    };
  }, [ticket, chartTrading, activeTicker, setChartPriceDraft]);

  useEffect(() => {
    const chart = chartRef.current;
    if (!chart || phase !== "ready") return;
    const onClick = (param: MouseEventParams) => {
      const trading = tradingRef.current;
      if (!trading.enabled) return;
      const series = candleSeriesRef.current;
      const point = param.point;
      if (!series || !point) return;
      const shift = param.sourceEvent?.shiftKey ?? false;
      const alt = param.sourceEvent?.altKey ?? false;
      const hovered = param.hoveredInfo;
      const lineId =
        hovered?.objectKind === "custom-price-line" && typeof hovered.objectId === "string"
          ? hovered.objectId
          : null;
      if (lineId && !lineId.startsWith("position") && !shift && !alt) {
        trading.cancel(lineId);
        return;
      }
      if (!shift && !alt) return;
      const located = priceFromCoordinate(point.y);
      if (!located) return;
      trading.open(alt && !shift ? "sell" : "buy", located.draft);
    };
    chart.subscribeClick(onClick);
    return () => chart.unsubscribeClick(onClick);
  }, [phase, chartEpoch]);

  useEffect(() => {
    const series = candleSeriesRef.current;
    const clear = () => {
      linesByIdRef.current.clear();
      const current = candleSeriesRef.current;
      if (!current) {
        priceLinesRef.current = [];
        return;
      }
      for (const line of priceLinesRef.current) current.removePriceLine(line);
      priceLinesRef.current = [];
    };
    if (!series || phase !== "ready" || !chartTrading) {
      clear();
      return;
    }
    clear();
    const instrumentId = activeQuote?.instrumentId;
    if (!instrumentId) return;
    for (const order of orders) {
      if (order.origin !== "chart" || order.instrumentId !== instrumentId || !order.limitPrice) continue;
      if (order.status !== "accepted" && order.status !== "partially_filled") continue;
      const color = chartOrderColor(order);
      const line = series.createPriceLine({
        id: order.orderId,
        price: decimalForDrawing(order.limitPrice),
        color,
        lineWidth: 1,
        lineStyle: 1,
        lineVisible: true,
        axisLabelVisible: false,
        title: "",
      });
      priceLinesRef.current.push(line);
      linesByIdRef.current.set(order.orderId, line);
    }
    const average = portfolio?.positions.find(
      (position) => position.instrumentId === instrumentId && !position.isClosed,
    )?.averageCost;
    if (average) {
      try {
        const positionLine = series.createPriceLine({
          id: "position",
          price: decimalForDrawing(average),
          color: "#d97706",
          lineWidth: 1,
          lineStyle: 0,
          lineVisible: true,
          axisLabelVisible: false,
          title: "",
        });
        priceLinesRef.current.push(positionLine);
        linesByIdRef.current.set("position", positionLine);
      } catch {
        // A position average that cannot be drawn stays off this chart.
      }
    }
    return clear;
  }, [orders, activeQuote, portfolio, chartTrading, phase, chartEpoch]);

  const chartPosition = portfolio?.positions.find(
    (position) => position.instrumentId === activeQuote?.instrumentId && !position.isClosed,
  );

  function locateChartPrice(clientY: number): { y: number; draft: string } | null {
    const series = candleSeriesRef.current;
    const host = containerRef.current;
    if (!series || !host) return null;
    const y = clientY - host.getBoundingClientRect().top;
    const price = series.coordinateToPrice(y);
    if (price === null || !Number.isFinite(price) || price <= 0) return null;
    return { y, draft: price.toFixed(seriesDecimalsRef.current) };
  }

  async function commitLabel(action: ChartLabelCommit, draft: string) {
    if (!activeQuote?.instrumentId) return;
    const result = action.kind === "position"
      ? await placeProtection({ instrumentId: activeQuote.instrumentId, price: draft, origin: "chart" })
      : await reviseOrder(action.id, draft);
    if (result.ok) {
      if (action.kind === "position") {
        setNotice("Proteção do gráfico enviada.");
      } else {
        const side = result.value.side === "buy" ? "compra" : "venda";
        const kind = result.value.type === "stop" ? "stop" : "limite";
        setNotice(`Ordem de ${side} atualizada para ${kind}.`);
      }
    } else if (action.kind === "position" && result.error === "The order is not in a state that allows this operation.") {
      setNotice("Não há posição aberta neste ativo.");
    } else {
      setNotice(result.error);
    }
    await refreshAll();
  }

  const sendTicket = async () => {
    if (!ticket || !preview || !activeQuote?.instrumentId) return;
    const protection = protectionPayload(strategy);
    if (protection.error) {
      setPreviewError(protection.error);
      return;
    }
    setTicketBusy(true);
    setNotice(null);
    try {
      const result = await submitOrder({
        instrumentId: activeQuote.instrumentId,
        side: ticket.side,
        type: preview.type,
        quantity: ticket.quantity.trim() || "1",
        limitPrice: preview.price,
        origin: "chart",
        protection: protection.body,
      });
      if (result.ok) {
        setTicket(null);
        setPreview(null);
        setNotice(preview.type === "stop" ? "Ordem stop do gráfico enviada." : "Ordem limitada do gráfico enviada.");
        await refreshAll();
      } else {
        setPreviewError(result.error);
      }
    } finally {
      setTicketBusy(false);
    }
  };

  const displayStats = hoveredStats ?? latestStats;
  const candleMove =
    displayStats && displayStats.open > 0
      ? ((displayStats.close - displayStats.open) / displayStats.open) * 100
      : 0;
  const isBullish = displayStats ? displayStats.close >= displayStats.open : true;
  const candleMoveText = `${candleMove >= 0 ? "+" : ""}${candleMove.toFixed(2).replace(".", ",")}%`;

  const dateFormatted = displayStats
    ? (() => {
        const clock = brasiliaFromUnix(displayStats.time);
        return clock.time === "—" ? "" : `${clock.date.slice(0, 5)}, ${clock.time.slice(0, 5)}`;
      })()
    : "";

  return (
    <div className="relative flex h-full min-h-[280px] w-full flex-col overflow-hidden bg-[#090d16] font-sans select-none lg:min-h-0">
      {delayedYahoo && seriesNote && (
        <p className="shrink-0 border-b border-[#1e293b] bg-[#0d131f] px-3 py-1 text-[10px] leading-snug text-amber-200/90">
          {seriesNote}
        </p>
      )}
      {/* Stats ribbon */}
      <div className="h-8 shrink-0 bg-[#0d131f] border-b border-[#1e293b] px-3 flex items-center justify-between text-xs z-10">
        {/* Left: OHLCV Ribbon */}
        <div className="flex items-center gap-3 overflow-x-auto text-[11px] font-mono">
          <div className="flex items-center gap-1.5 font-bold">
            <span className="text-white">{activeTicker}</span>
            <span className="rounded bg-[#1e293b] px-1.5 py-0.2 text-[10px] text-amber-400 font-semibold">
              {timeframe}
            </span>
          </div>

          {displayStats && (
            <>
              <span className="text-stone-500">|</span>
              <span className="text-stone-400">{dateFormatted}</span>
              <span className="text-stone-500">|</span>

              <div className="flex items-center gap-2">
                <span>
                  <span className="text-stone-500">A: </span>
                  <span className="text-stone-200 font-semibold">
                    {formatChartPrice(displayStats.open, assetConfig.precision)}
                  </span>
                </span>
                <span>
                  <span className="text-stone-500">M: </span>
                  <span className="text-[#089981] font-semibold">
                    {formatChartPrice(displayStats.high, assetConfig.precision)}
                  </span>
                </span>
                <span>
                  <span className="text-stone-500">m: </span>
                  <span className="text-[#f23645] font-semibold">
                    {formatChartPrice(displayStats.low, assetConfig.precision)}
                  </span>
                </span>
                <span>
                  <span className="text-stone-500">F: </span>
                  <span className={`font-bold ${isBullish ? "text-[#089981]" : "text-[#f23645]"}`}>
                    {formatChartPrice(displayStats.close, assetConfig.precision)}
                  </span>
                </span>
                <span className={`font-bold ${candleMove >= 0 ? "text-[#089981]" : "text-[#f23645]"}`}>
                  ({candleMoveText})
                </span>
              </div>
            </>
          )}
        </div>

        {/* Right: Indicators Selector + Countdown Timer + Timeframe Buttons */}
        <div className="flex items-center gap-2 text-[11px]">
          {/* Indicator Selector Popover Trigger */}
          <div className="relative">
            <button
              type="button"
              onClick={() => setIndicatorMenuOpen(!indicatorMenuOpen)}
              className="flex items-center gap-1 rounded bg-[#131d2e] border border-[#1e293b] px-2 py-0.5 text-stone-300 font-semibold text-[10px] hover:bg-[#1a263d] transition"
            >
              <span>+ Indicadores</span>
              <span className="text-amber-400 text-[9px]">▾</span>
            </button>

            {indicatorMenuOpen && (
              <div className="absolute right-0 top-full mt-1 w-52 rounded-lg bg-[#0f172a] border border-[#334155] shadow-xl p-2 z-50 text-xs">
                <div className="text-[10px] font-bold text-stone-400 uppercase tracking-wider mb-1.5 px-1">
                  Indicadores Técnicos
                </div>
                <div className="space-y-1">
                  <label className="flex items-center justify-between px-1 py-1 rounded hover:bg-[#1e293b] cursor-pointer text-stone-200">
                    <span className="flex items-center gap-1.5">
                      <span className="h-2 w-2 rounded-full bg-[#f59e0b]" />
                      <span>SMA 20 (Média)</span>
                    </span>
                    <input
                      type="checkbox"
                      checked={indicatorSettings.sma}
                      onChange={() => toggleIndicator("sma")}
                      className="accent-amber-500 rounded"
                    />
                  </label>

                  <label className="flex items-center justify-between px-1 py-1 rounded hover:bg-[#1e293b] cursor-pointer text-stone-200">
                    <span className="flex items-center gap-1.5">
                      <span className="h-2 w-2 rounded-full bg-[#38bdf8]" />
                      <span>Bollinger (20, 2)</span>
                    </span>
                    <input
                      type="checkbox"
                      checked={indicatorSettings.bollinger}
                      onChange={() => toggleIndicator("bollinger")}
                      className="accent-sky-500 rounded"
                    />
                  </label>

                  <label className="flex items-center justify-between px-1 py-1 rounded hover:bg-[#1e293b] cursor-pointer text-stone-200">
                    <span className="flex items-center gap-1.5">
                      <span className="h-2 w-2 rounded-full bg-[#c084fc]" />
                      <span>VWAP Intraday</span>
                    </span>
                    <input
                      type="checkbox"
                      checked={indicatorSettings.vwap}
                      onChange={() => toggleIndicator("vwap")}
                      className="accent-purple-500 rounded"
                    />
                  </label>

                  <label className="flex items-center justify-between px-1 py-1 rounded hover:bg-[#1e293b] cursor-pointer text-stone-200">
                    <span className="flex items-center gap-1.5">
                      <span className="h-2 w-2 rounded-full bg-[#10b981]" />
                      <span>IFR / RSI (14)</span>
                    </span>
                    <input
                      type="checkbox"
                      checked={indicatorSettings.rsi}
                      onChange={() => toggleIndicator("rsi")}
                      className="accent-emerald-500 rounded"
                    />
                  </label>
                </div>
              </div>
            )}
          </div>

          <div className="flex items-center gap-0.5 bg-[#131d2e] p-0.5 rounded border border-[#1e293b]">
            {["1m", "5m", "15m", "1h"].map((tf) => (
              <button
                key={tf}
                type="button"
                onClick={() => setTimeframeForActiveTab(tf)}
                className={`px-1.5 py-0.5 text-[10px] font-bold rounded transition ${
                  timeframe === tf
                    ? "bg-[#089981] text-white shadow-xs"
                    : "text-stone-400 hover:text-stone-200 hover:bg-[#1e293b]"
                }`}
              >
                {tf}
              </button>
            ))}
          </div>
        </div>
      </div>

      {/* 2. Chart Canvas Container */}
      <div
        className="relative h-full w-full flex-1"
        onPointerDown={(event) => {
          if (event.button !== 0 || phase !== "ready" || !containerRef.current) return;
          if (!event.shiftKey && !event.altKey) return;
          if ((event.target as HTMLElement).closest("[data-order-label]")) return;
          event.preventDefault();
          const rect = containerRef.current.getBoundingClientRect();
          const located = priceFromCoordinate(event.clientY - rect.top);
          if (!located) return;
          tradingRef.current.open(event.altKey && !event.shiftKey ? "sell" : "buy", located.draft);
        }}
        onContextMenu={(event) => {
          if (phase !== "ready" || !containerRef.current) return;
          event.preventDefault();
          const rect = containerRef.current.getBoundingClientRect();
          const located = priceFromCoordinate(event.clientY - rect.top);
          if (!located) return;
          const host = event.currentTarget.getBoundingClientRect();
          setMenu({
            ...located,
            x: Math.min(event.clientX - host.left, host.width - 220),
            y: Math.min(event.clientY - host.top, host.height - 220),
            section: "root",
          });
        }}
      >
        <div ref={containerRef} className="h-full w-full" />
        <ChartOrderLabels
          orders={orders}
          instrumentId={activeQuote?.instrumentId}
          averageCost={chartPosition?.averageCost ?? null}
          enabled={chartTrading}
          phase={phase}
          epoch={chartEpoch}
          seriesRef={candleSeriesRef}
          decimalsRef={seriesDecimalsRef}
          locate={locateChartPrice}
          onCancel={(orderId) => {
            void cancelResting(orderId);
          }}
          onCommit={(action, draft) => {
            void commitLabel(action, draft);
          }}
          onClassify={async (orderId, draft) => {
            const order = orders.find((item) => item.orderId === orderId);
            if (!order) return null;
            const result = await previewChartOrder({
              ticker: activeTicker,
              side: order.side,
              price: draft,
              quantity: order.remainingQuantity,
            });
            return result.ok ? result.value.type : null;
          }}
          onDraft={(orderId, draft, color) => {
            const line = linesByIdRef.current.get(orderId);
            if (!line) return;
            try {
              line.applyOptions({ price: decimalForDrawing(draft), color });
            } catch {
              // A draft the series cannot draw keeps the previous line.
            }
          }}
        />
        {chartTrading && phase === "ready" && (
          <div className="pointer-events-none absolute bottom-2 right-3 max-w-[280px] text-right text-[10px] leading-snug text-slate-400">
            Shift+clique insere compra · Alt+clique insere venda · arraste cruza o fechamento e vira stop · X cancela
          </div>
        )}
        {notice && (
          <div className="absolute left-3 top-2 z-20 rounded bg-[#0f172a]/95 px-2 py-1 text-[11px] text-amber-100">
            {notice}
          </div>
        )}
        {menu && (
          <div
            className="absolute z-30 w-52 rounded-md border border-[#d8d2c4] bg-[#f7f5f0] py-1 text-xs text-stone-800 shadow-lg"
            style={{ left: Math.max(8, menu.x), top: Math.max(8, menu.y) }}
            onMouseDown={(event) => {
              event.stopPropagation();
              event.nativeEvent.stopPropagation();
            }}
          >
            <div className="px-3 py-1.5 text-[11px] font-semibold text-stone-500">Ordem a {menu.label}</div>
            <button
              type="button"
              disabled={!chartTrading}
              onClick={() => openTicket("buy", menu.draft)}
              className="flex w-full items-center justify-between px-3 py-1.5 text-left hover:bg-white disabled:opacity-40"
            >
              Boleta de compra <span className="text-emerald-700">↗</span>
            </button>
            <button
              type="button"
              disabled={!chartTrading}
              onClick={() => openTicket("sell", menu.draft)}
              className="flex w-full items-center justify-between px-3 py-1.5 text-left hover:bg-white disabled:opacity-40"
            >
              Boleta de venda <span className="text-rose-700">↘</span>
            </button>
            <button
              type="button"
              onClick={() => {
                setChartTrading((value) => !value);
                setMenu(null);
                setTicket(null);
              }}
              className="flex w-full items-center justify-between px-3 py-1.5 text-left hover:bg-white"
            >
              Chart trading <span className="text-stone-500">{chartTrading ? "Ocultar" : "Mostrar"}</span>
            </button>
            <button
              type="button"
              onClick={() => {
                setIndicatorMenuOpen(true);
                setMenu(null);
              }}
              className="flex w-full items-center justify-between px-3 py-1.5 text-left hover:bg-white"
            >
              Inserir indicador <span className="text-stone-500">+</span>
            </button>
            {menu.section === "time" ? (
              ["1m", "5m", "15m", "1h"].map((tf) => (
                <button
                  key={tf}
                  type="button"
                  onClick={() => {
                    setTimeframeForActiveTab(tf);
                    setMenu(null);
                  }}
                  className="flex w-full items-center justify-between px-3 py-1.5 text-left hover:bg-white"
                >
                  {tf}
                  {timeframe === tf ? <span className="text-emerald-700">●</span> : null}
                </button>
              ))
            ) : (
              <button
                type="button"
                onClick={() => setMenu({ ...menu, section: "time" })}
                className="flex w-full items-center justify-between px-3 py-1.5 text-left hover:bg-white"
              >
                Tempo gráfico <span className="text-stone-500">›</span>
              </button>
            )}
          </div>
        )}
      </div>
      {ticket && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/45 p-4">
          <div className="max-h-[90vh] w-full max-w-sm overflow-y-auto rounded-xl bg-white p-5 text-stone-900 shadow-2xl">
            <div className="mb-4 flex items-start justify-between">
              <div>
                <h2 className="text-base font-bold">{ticket.side === "buy" ? "Boleta de compra" : "Boleta de venda"}</h2>
                <p className="text-[11px] text-stone-500">{activeTicker} · Conta demo</p>
              </div>
              <button type="button" onClick={() => setTicket(null)} className="text-stone-400 hover:text-stone-700" aria-label="Fechar">
                ×
              </button>
            </div>
            <label className="mb-3 block text-[11px] font-semibold text-stone-600">
              Preço
              <input
                value={ticket.price}
                onChange={(event) => setTicket({ ...ticket, price: event.target.value })}
                className="mt-1 w-full rounded-md border border-[#d8d2c4] px-3 py-1.5 text-right font-mono text-sm"
              />
            </label>
            <label className="mb-3 block text-[11px] font-semibold text-stone-600">
              Quantidade
              <input
                value={ticket.quantity}
                onChange={(event) => setTicket({ ...ticket, quantity: event.target.value })}
                className="mt-1 w-full rounded-md border border-[#d8d2c4] px-3 py-1.5 text-right font-mono text-sm"
              />
            </label>
            <div className="mb-3 grid grid-cols-2 gap-3 text-[11px]">
              <div>
                <div className="text-stone-500">Preço da ordem</div>
                <div className="font-mono font-bold">{preview ? groupDecimal(preview.price) : "—"}</div>
              </div>
              <div>
                <div className="text-stone-500">Fechamento do gráfico</div>
                <div className="font-mono font-bold">{preview ? groupDecimal(preview.referenceClose) : "—"}</div>
              </div>
            </div>
            <p className="mb-1 text-[11px] text-stone-500">
              {preview
                ? `${preview.type === "stop" ? "Ordem stop" : "Ordem limitada"} · Válida nesta sessão`
                : previewError ?? "Lendo o fechamento do gráfico..."}
            </p>
            <p className="mb-3 font-mono text-[11px] text-stone-600">
              Valor (R$) {preview ? groupDecimal(preview.notional) : "—"}
            </p>
            <div className="mb-4">
              <ProtectionFields value={strategy} onChange={setStrategy} />
            </div>
            <div className="flex justify-end gap-2">
              <button type="button" onClick={() => setTicket(null)} className="rounded-md border border-[#d8d2c4] px-3 py-1.5 text-xs">
                Cancelar
              </button>
              <button
                type="button"
                disabled={ticketBusy || !preview || !activeQuote?.instrumentId}
                onClick={() => void sendTicket()}
                className={`rounded-md px-3 py-1.5 text-xs font-bold text-white disabled:opacity-50 ${
                  ticket.side === "buy" ? "bg-[#15803d]" : "bg-[#b91c1c]"
                }`}
              >
                {ticket.side === "buy" ? "Enviar compra" : "Enviar venda"}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* 3. RSI Status Ribbon at Bottom */}
      {indicatorSettings.rsi && latestRsi !== null && (
        <div className="h-6 shrink-0 bg-[#0d131f]/95 border-t border-[#1e293b] px-3 flex items-center justify-between text-[10px] font-mono z-10">
          <div className="flex items-center gap-2">
            <span className="font-bold text-stone-400">RSI(14):</span>
            <span
              className={`font-black ${
                latestRsi < 30
                  ? "text-[#089981]"
                  : latestRsi > 70
                  ? "text-[#f23645]"
                  : "text-amber-300"
              }`}
            >
              {latestRsi.toFixed(1).replace(".", ",")}
            </span>
            <span className="text-stone-500">|</span>
            <span className="text-stone-400">
              {latestRsi < 30
                ? "🟢 Sobrevenda (Possível Reversão de Alta)"
                : latestRsi > 70
                ? "🔴 Sobrecompra (Possível Reversão de Baixa)"
                : "⚪ Zona Neutra"}
            </span>
          </div>

          <div className="flex items-center gap-2 text-stone-500">
            <span>Níveis: 30 / 70</span>
          </div>
        </div>
      )}

      {/* Status Overlays */}
      {phase === "loading" && (
        <div className="absolute inset-0 flex items-center justify-center bg-[#090d16]/80 text-xs text-stone-400">
          Carregando gráfico de {activeTicker} ({timeframe})...
        </div>
      )}

      {phase === "empty" && (
        <div className="absolute inset-0 flex items-center justify-center bg-[#090d16]/90 text-xs text-stone-400">
          {delayedYahoo
            ? `O Yahoo não devolveu candles de ${activeTicker}.`
            : `O pregão ainda não publicou prints de ${activeTicker}.`}
        </div>
      )}

      {phase === "error" && (
        <div className="absolute inset-0 flex items-center justify-center bg-[#090d16]/90 text-xs text-rose-400">
          Não foi possível ler o gráfico de {activeTicker}.
        </div>
      )}

      <div className="absolute bottom-8 left-3 pointer-events-none text-[10px] font-mono text-slate-600/60 font-semibold tracking-wider">
        {delayedYahoo ? "YAHOO · ATRASADO" : "PREGÃO SIMULADO"}
      </div>
    </div>
  );
}

/** Keeps a short series at a readable width instead of stretching a few bars across the pane. */
function showSession(chart: IChartApi, barCount: number, width: number) {
  const pane = width > 0 ? width : 960;
  const readableSlots = Math.max(32, Math.round(pane / 28));
  const slots = Math.max(barCount + 3, Math.min(readableSlots, 120));
  chart.timeScale().setVisibleLogicalRange({
    from: barCount - slots,
    to: barCount + 2,
  });
}

function pulseSeries<T extends { time: UTCTimestamp }>(
  series: { data: () => readonly any[]; update: (bar: any) => void } | null,
  points: readonly T[],
) {
  if (!series || points.length === 0) return;
  const existing = series.data();
  const lastItem = existing.length > 0 ? (existing[existing.length - 1] as any) : null;
  const lastTime = lastItem && "time" in lastItem ? Number(lastItem.time) : Number.NEGATIVE_INFINITY;
  for (const point of points) {
    if (Number(point.time) >= lastTime) series.update(point);
  }
}

function playMinutes(history: YahooHistoryTransport): PlayBar[] {
  return (history.formingMinutes ?? []).map((candle) => ({
    time: Math.floor(Date.parse(candle.openTime) / 1000),
    open: decimalForDrawing(candle.open),
    high: decimalForDrawing(candle.high),
    low: decimalForDrawing(candle.low),
    close: decimalForDrawing(candle.close),
  }));
}

function toPlay(candle: CandlestickData<UTCTimestamp>): PlayBar {
  return {
    time: Number(candle.time),
    open: candle.open,
    high: candle.high,
    low: candle.low,
    close: candle.close,
  };
}

function drawn(state: Playback): CandlestickData<UTCTimestamp> {
  return {
    time: state.bucket as UTCTimestamp,
    open: state.open,
    high: state.high,
    low: state.low,
    close: state.close,
  };
}

function yahooCandles(history: YahooHistoryTransport): CandlestickData<UTCTimestamp>[] {
  return history.candles.map((candle) => ({
    time: Math.floor(Date.parse(candle.openTime) / 1000) as UTCTimestamp,
    open: decimalForDrawing(candle.open),
    high: decimalForDrawing(candle.high),
    low: decimalForDrawing(candle.low),
    close: decimalForDrawing(candle.close),
  }));
}

function getAssetConfig(ticker: string) {
  const upper = ticker.toUpperCase();
  if (upper === "WING26" || upper.includes("WIN")) {
    return { precision: 0, minMove: 5 };
  }
  if (upper === "WDOU26" || upper.includes("WDO")) {
    return { precision: 1, minMove: 0.5 };
  }
  return { precision: 2, minMove: 0.01 };
}


