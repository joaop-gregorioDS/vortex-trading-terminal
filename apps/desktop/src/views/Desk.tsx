import { useEffect, useRef, useState } from "react";
import type { ChartBody } from "../bindings/ChartBody";
import type { ChartSnapshot } from "../bindings/ChartSnapshot";
import type { HistoryInterval } from "../bindings/HistoryInterval";
import type { MarketQuote } from "../bindings/MarketQuote";
import type { Order } from "../bindings/Order";
import type { Position } from "../bindings/Position";
import type { Theme } from "../bindings/Theme";
import type { YahooHistory } from "../bindings/YahooHistory";
import { CandleChart } from "../chart/CandleChart";
import { formatMoney, formatSignedMoney } from "../format";
import { insideDesktop, loadChart, loadHistory, placeProtection, previewChart, readError, reviseOrder } from "../ipc/client";
import { HISTORY_INTERVALS, SESSION_INTERVALS, type SessionInterval } from "../market";
import { closingSide, lineTitle, resting, type ChartLine } from "../trading";
import { Ticket } from "./Ticket";

function isSnapshot(body: ChartBody): body is ChartSnapshot {
  return "candles" in body;
}

export function Desk({
  ticker,
  instrumentId,
  quotes,
  orders,
  positions,
  theme,
  onTicker,
}: {
  ticker: string | null;
  instrumentId: string | null;
  quotes: MarketQuote[] | null;
  orders: Order[];
  positions: Position[];
  theme: Theme;
  onTicker: (ticker: string) => void;
}) {
  const [mode, setMode] = useState<"session" | "history">("session");
  const [sessionInterval, setSessionInterval] = useState<SessionInterval>("5m");
  const [historyInterval, setHistoryInterval] = useState<HistoryInterval>("1d");
  const [refresh, setRefresh] = useState(0);
  const [sessionChart, setSessionChart] = useState<ChartSnapshot | null>(null);
  const [history, setHistory] = useState<YahooHistory | null>(null);
  const [empty, setEmpty] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [protectArmed, setProtectArmed] = useState(false);
  const [draftLine, setDraftLine] = useState<ChartLine | null>(null);
  const [priceEdits, setPriceEdits] = useState<Record<string, string>>({});
  const gate = useRef(false);

  function run<T>(task: () => Promise<T>): Promise<T | undefined> {
    if (gate.current) return Promise.resolve(undefined);
    gate.current = true;
    setBusy(true);
    return task().finally(() => {
      gate.current = false;
      setBusy(false);
    });
  }

  useEffect(() => {
    if (!ticker || !insideDesktop()) return;
    let cancelled = false;
    setSessionChart(null);
    setHistory(null);
    setEmpty(false);
    setError(null);
    const request =
      mode === "session"
        ? loadChart(ticker, sessionInterval).then((body) => {
            if (cancelled) return;
            if (isSnapshot(body) && body.candles.length > 0) {
              setSessionChart(body);
              return;
            }
            setEmpty(true);
          })
        : loadHistory(ticker, historyInterval).then((body) => {
            if (cancelled) return;
            setHistory(body);
            if (body.candles.length === 0) setEmpty(true);
          });
    void request.catch((caught: unknown) => {
      if (!cancelled) setError(readError(caught));
    });
    return () => {
      cancelled = true;
    };
  }, [ticker, mode, sessionInterval, historyInterval, refresh]);

  useEffect(() => {
    setDraftLine(null);
    setPriceEdits({});
    setProtectArmed(false);
  }, [instrumentId]);

  useEffect(() => {
    if (mode !== "session") setProtectArmed(false);
  }, [mode]);

  useEffect(() => {
    if (draftLine?.id && orders.some((order) => order.orderId === draftLine.id)) {
      setDraftLine(null);
    }
  }, [orders, draftLine]);

  useEffect(() => {
    setPriceEdits((current) => {
      let changed = false;
      const next: Record<string, string> = {};
      for (const [id, price] of Object.entries(current)) {
        const order = orders.find((item) => item.orderId === id);
        if (!order || order.limitPrice === price) {
          changed = true;
          continue;
        }
        next[id] = price;
      }
      return changed ? next : current;
    });
  }, [orders]);

  async function revise(orderId: string, price: string) {
    if (orderId === "draft") return;
    setPriceEdits((current) => ({ ...current, [orderId]: price }));
    try {
      const order = await run(() => reviseOrder(orderId, price));
      if (!order) {
        setPriceEdits((current) => dropEdit(current, orderId));
        return;
      }
      setPriceEdits((current) => ({ ...current, [orderId]: order.limitPrice ?? price }));
    } catch (caught) {
      setPriceEdits((current) => dropEdit(current, orderId));
      setNotice(readError(caught));
    }
  }

  async function protect(price: string) {
    if (!ticker || !instrumentId) return;
    const position = positions.find((item) => item.instrumentId === instrumentId && !item.isClosed);
    const side = position ? closingSide(position.quantity) : null;
    if (!side) {
      setNotice("Não há posição aberta neste ativo.");
      setProtectArmed(false);
      return;
    }
    setProtectArmed(false);
    try {
      const placed = await run(async () => {
        const preview = await previewChart({ ticker, side, price, quantity: null });
        const title = lineTitle(preview.type);
        setDraftLine({ id: "draft", price: preview.price, title });
        const order = await placeProtection({
          instrumentId,
          price: preview.price,
          origin: "chart",
        });
        return { order, price: preview.price, title };
      });
      if (!placed) {
        setDraftLine(null);
        return;
      }
      setDraftLine({ id: placed.order.orderId, price: placed.price, title: placed.title });
      setNotice(null);
    } catch (caught) {
      setDraftLine(null);
      setNotice(readError(caught));
    }
  }

  const sessionLines: ChartLine[] =
    mode === "session" && instrumentId
      ? orders.flatMap((order) => {
          if (order.instrumentId !== instrumentId || !resting(order.status) || !order.limitPrice) return [];
          return [
            {
              id: order.orderId,
              price: priceEdits[order.orderId] ?? order.limitPrice,
              title: lineTitle(order.type),
            },
          ];
        })
      : [];
  if (
    mode === "session" &&
    draftLine &&
    draftLine.price &&
    !sessionLines.some((line) => line.id === draftLine.id)
  ) {
    sessionLines.push(draftLine);
  }

  const intervals = mode === "session" ? SESSION_INTERVALS : HISTORY_INTERVALS;
  const selectedInterval = mode === "session" ? sessionInterval : historyInterval;
  const chartReady =
    mode === "session" ? sessionChart !== null : history !== null && history.candles.length > 0;

  return (
    <main className="workspace">
      <section className="quotes" aria-label="Cotações">
        {quotes?.map((quote) => (
          <button
            key={quote.ticker}
            type="button"
            className={quote.ticker === ticker ? "quote selected" : "quote"}
            onClick={() => onTicker(quote.ticker)}
          >
            <span>{quote.ticker}</span>
            <span>{formatMoney(quote.lastPrice)}</span>
            <span>{formatSignedMoney(quote.changePoints)}</span>
          </button>
        ))}
        {quotes === null ? <p className="muted">Lendo o pregão…</p> : null}
        {quotes && quotes.length === 0 ? <p className="muted">Sem ativos no pregão.</p> : null}
      </section>
      <section className="chart-panel" aria-label="Gráfico">
        <div className="chart-head">
          <div className="chart-tools">
            <strong>{ticker ?? "Gráfico"}</strong>
            <span className="instrument">{instrumentId ? `Instrumento ${instrumentId}` : "Instrumento —"}</span>
            <div className="modes" role="group" aria-label="Série">
              <button type="button" className={mode === "session" ? "selected" : ""} onClick={() => setMode("session")}>
                Pregão
              </button>
              <button type="button" className={mode === "history" ? "selected" : ""} onClick={() => setMode("history")}>
                Histórico
              </button>
            </div>
            <div className="intervals">
              {intervals.map((item) => (
                <button
                  key={item}
                  type="button"
                  className={item === selectedInterval ? "selected" : ""}
                  onClick={() => {
                    if (mode === "session") setSessionInterval(item as SessionInterval);
                    else setHistoryInterval(item as HistoryInterval);
                  }}
                >
                  {item}
                </button>
              ))}
            </div>
            <button type="button" onClick={() => setRefresh((value) => value + 1)}>
              Atualizar gráfico
            </button>
          </div>
          {mode === "history" && history ? (
            <p className="history-note">
              {history.seriesName} · {history.yahooSymbol} · {history.currency} ·{" "}
              {history.delayed ? "Atrasada" : "Do pregão"} · {history.note} · Minutos em formação:{" "}
              {history.formingMinutes.length}
            </p>
          ) : null}
        </div>
        {mode === "session" && sessionChart ? (
          <CandleChart
            interval={sessionChart.context.interval}
            barCount={sessionChart.context.barCount}
            candles={sessionChart.candles}
            theme={theme}
            timeVisible
            lines={sessionLines}
            protectArmed={protectArmed}
            onRevise={(orderId, price) => void revise(orderId, price)}
            onProtect={(price) => void protect(price)}
          />
        ) : null}
        {/* Minutos em formação repetem o último candle. A série usa só os fechados. */}
        {mode === "history" && history && history.candles.length > 0 ? (
          <CandleChart
            interval={history.interval}
            barCount={history.candles.length}
            candles={history.candles}
            theme={theme}
            timeVisible={history.interval !== "1d"}
          />
        ) : null}
        {empty ? <p className="chart-note">Sem candles para este ativo.</p> : null}
        {error ? <p className="chart-note">{error}</p> : null}
        {!chartReady && !empty && !error ? (
          <p className="chart-note">{mode === "history" ? "Lendo histórico…" : "Lendo candles…"}</p>
        ) : null}
      </section>
      <div className="ticket-wrap">
        {notice ? <p className="ticket-note">{notice}</p> : null}
        <Ticket
          ticker={ticker}
          instrumentId={instrumentId}
          orders={orders}
          positions={positions}
          busy={busy}
          protectArmed={protectArmed && mode === "session"}
          run={run}
          onNotice={setNotice}
          onArm={(armed) => {
            if (mode === "session") setProtectArmed(armed);
          }}
          onRevised={() => setNotice(null)}
          onProtected={(orderId, price, title) => {
            if (!price) {
              setDraftLine(null);
              return;
            }
            setDraftLine({ id: orderId ?? "draft", price, title });
          }}
        />
      </div>
    </main>
  );
}

function dropEdit(current: Record<string, string>, orderId: string): Record<string, string> {
  if (!(orderId in current)) return current;
  const next = { ...current };
  delete next[orderId];
  return next;
}
