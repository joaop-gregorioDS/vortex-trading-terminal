"use client";

import { useEffect, useMemo, useRef, useState, type RefObject } from "react";
import type { ISeriesApi } from "lightweight-charts";
import { decimalForDrawing } from "@/lib/charts/adapter";
import { groupDecimal, trimDecimal } from "@/lib/trading/format";
import type { OrderTransport } from "@/lib/trading/transport";

export type ChartLabelCommit = { kind: "order"; id: string } | { kind: "position" };

type DrawnLabel = {
  id: string;
  kind: "order" | "position";
  exact: string;
  price: number;
  side: "buy" | "sell";
  type: "limit" | "stop";
  role: OrderTransport["role"];
  quantity: string;
};

type Drag = { id: string; y: number; draft: string; type: "limit" | "stop" };

type LabelMenu = { id: string; x: number; y: number };

export function chartOrderColor(order: Pick<OrderTransport, "side">): string {
  return chartLevelColor(order.side);
}

/** Buy orders stay green and sell orders stay red, including stops. */
export function chartLevelColor(side: "buy" | "sell"): string {
  return side === "buy" ? "#089981" : "#f23645";
}

/** Profit-style caption: "Compra 1" or "Venda Stop 1". Limit keeps the side only. */
export function chartOrderCaption(
  label: Pick<DrawnLabel, "side" | "type" | "role" | "quantity">,
  type: "limit" | "stop",
  compact: boolean,
  dragging: boolean,
): string {
  if (!dragging && label.role === "target") return "Alvo";
  if (!dragging && label.role === "stop") return "Stop";
  const side = label.side === "buy" ? "Compra" : "Venda";
  const stop = type === "stop" ? " Stop" : "";
  if (compact) return `${side}${stop}`;
  const quantity = trimDecimal(label.quantity);
  return quantity ? `${side}${stop} ${groupDecimal(quantity)}` : `${side}${stop}`;
}

function drawnPrice(value: string): number | null {
  try {
    const price = decimalForDrawing(value);
    return price > 0 ? price : null;
  } catch {
    return null;
  }
}

function sameDrawnPrice(exact: string, draft: string, decimals: number): boolean {
  const price = drawnPrice(exact);
  return price !== null && price.toFixed(decimals) === draft;
}

export function ChartOrderLabels({
  orders,
  instrumentId,
  averageCost,
  enabled,
  phase,
  epoch,
  seriesRef,
  decimalsRef,
  locate,
  onCancel,
  onCommit,
  onClassify,
  onDraft,
}: {
  orders: readonly OrderTransport[];
  instrumentId: string | undefined;
  averageCost: string | null;
  enabled: boolean;
  phase: string;
  epoch: number;
  seriesRef: RefObject<ISeriesApi<"Candlestick"> | null>;
  decimalsRef: RefObject<number>;
  locate: (clientY: number) => { y: number; draft: string } | null;
  onCancel: (orderId: string) => void;
  onCommit: (action: ChartLabelCommit, draft: string) => void;
  /** Server says whether this draft is a limit or a stop. Null keeps the previous word. */
  onClassify: (orderId: string, draft: string) => Promise<"limit" | "stop" | null>;
  /** Moves the dashed line with the label. The book changes only when the drag ends. */
  onDraft: (orderId: string, draft: string, color: string) => void;
}) {
  const labelsRef = useRef<DrawnLabel[]>([]);
  const topsRef = useRef<Record<string, number>>({});
  const dragRef = useRef<Drag | null>(null);
  const classifySeq = useRef(0);
  const classifyTimer = useRef<number | null>(null);
  const [tops, setTops] = useState<Record<string, number>>({});
  const [drag, setDrag] = useState<Drag | null>(null);
  const [compact, setCompact] = useState(false);
  const [menu, setMenu] = useState<LabelMenu | null>(null);

  const labels = useMemo(() => {
    const next: DrawnLabel[] = [];
    if (!enabled || !instrumentId) return next;
    for (const order of orders) {
      if (order.origin !== "chart" || order.instrumentId !== instrumentId || !order.limitPrice) continue;
      if (order.status !== "accepted" && order.status !== "partially_filled") continue;
      const price = drawnPrice(order.limitPrice);
      if (price === null) continue;
      next.push({
        id: order.orderId,
        kind: "order",
        exact: order.limitPrice,
        price,
        side: order.side,
        type: order.type === "stop" ? "stop" : "limit",
        role: order.role ?? null,
        quantity: order.remainingQuantity,
      });
    }
    if (averageCost) {
      const price = drawnPrice(averageCost);
      if (price !== null) {
        next.push({
          id: "position",
          kind: "position",
          exact: averageCost,
          price,
          side: "buy",
          type: "limit",
          role: null,
          quantity: "",
        });
      }
    }
    return next;
  }, [orders, instrumentId, averageCost, enabled]);

  useEffect(() => {
    labelsRef.current = labels;
  }, [labels]);

  useEffect(() => {
    if (!menu) return;
    const close = () => setMenu(null);
    window.addEventListener("pointerdown", close);
    return () => window.removeEventListener("pointerdown", close);
  }, [menu]);

  useEffect(() => {
    if (!enabled || phase !== "ready") return;
    let alive = true;
    let frame = 0;
    const tick = () => {
      if (!alive) return;
      frame = requestAnimationFrame(tick);
      const series = seriesRef.current;
      if (!series) return;
      const next: Record<string, number> = {};
      for (const label of labelsRef.current) {
        const y = series.priceToCoordinate(label.price);
        if (y === null || !Number.isFinite(y)) continue;
        next[label.id] = y;
      }
      const previous = topsRef.current;
      const keys = Object.keys(next);
      let changed = keys.length !== Object.keys(previous).length;
      if (!changed) {
        for (const key of keys) {
          if (previous[key] !== next[key]) {
            changed = true;
            break;
          }
        }
      }
      if (!changed) return;
      topsRef.current = next;
      setTops(next);
    };
    frame = requestAnimationFrame(tick);
    return () => {
      alive = false;
      cancelAnimationFrame(frame);
    };
  }, [enabled, phase, epoch, seriesRef]);

  const rememberDrag = (next: Drag | null) => {
    dragRef.current = next;
    setDrag(next);
  };

  const scheduleClassify = (label: DrawnLabel, draft: string) => {
    if (classifyTimer.current !== null) window.clearTimeout(classifyTimer.current);
    const token = ++classifySeq.current;
    classifyTimer.current = window.setTimeout(() => {
      classifyTimer.current = null;
      void onClassify(label.id, draft).then((type) => {
        if (token !== classifySeq.current || !type) return;
        const current = dragRef.current;
        if (!current || current.id !== label.id || current.draft !== draft || current.type === type) return;
        const next = { ...current, type };
        rememberDrag(next);
        onDraft(label.id, draft, chartLevelColor(label.side));
      });
    }, 160);
  };

  return (
    <div className="pointer-events-none absolute inset-0 z-20">
      {labels.map((label) => {
        const dragging = drag?.id === label.id;
        const y = dragging ? drag.y : tops[label.id];
        if (y === undefined) return null;
        const shownType = dragging ? drag.type : label.type;
        const color = label.kind === "position" ? "#d97706" : chartLevelColor(label.side);
        const shown = dragging ? drag.draft : label.exact;
        const text = label.kind === "position"
          ? "Posição"
          : chartOrderCaption(label, shownType, compact, dragging);
        return (
          <div
            key={label.id}
            data-order-label={label.id}
            data-order-type={shownType}
            className="pointer-events-auto absolute flex -translate-y-1/2 items-stretch"
            style={{ top: y, left: "25%" }}
            onPointerDown={(event) => {
              if ((event.target as HTMLElement).dataset.action === "cancel") return;
              event.preventDefault();
              event.stopPropagation();
              setMenu(null);
              try {
                event.currentTarget.setPointerCapture(event.pointerId);
              } catch {
                // Capture is only a convenience. The move and release listeners stay on this label.
              }
              rememberDrag({
                id: label.id,
                y,
                draft: label.price.toFixed(decimalsRef.current),
                type: label.type,
              });
            }}
            onPointerMove={(event) => {
              if (dragRef.current?.id !== label.id) return;
              const located = locate(event.clientY);
              if (!located) return;
              const draftChanged = located.draft !== dragRef.current.draft;
              const next = { id: label.id, y: located.y, draft: located.draft, type: dragRef.current.type };
              rememberDrag(next);
              if (!draftChanged) return;
              const color = label.kind === "position" ? "#d97706" : chartLevelColor(label.side);
              onDraft(label.id, located.draft, color);
              if (label.kind === "order") scheduleClassify(label, located.draft);
            }}
            onPointerUp={() => {
              const current = dragRef.current;
              if (current?.id !== label.id) return;
              classifySeq.current += 1;
              if (classifyTimer.current !== null) window.clearTimeout(classifyTimer.current);
              rememberDrag(null);
              if (sameDrawnPrice(label.exact, current.draft, decimalsRef.current)) return;
              onCommit(label.kind === "position" ? { kind: "position" } : { kind: "order", id: label.id }, current.draft);
            }}
            onContextMenu={(event) => {
              event.preventDefault();
              event.stopPropagation();
              if (label.kind !== "order") return;
              const host = (event.currentTarget.parentElement ?? event.currentTarget).getBoundingClientRect();
              setMenu({
                id: label.id,
                x: Math.min(Math.max(8, event.clientX - host.left), host.width - 180),
                y: Math.min(Math.max(8, event.clientY - host.top), host.height - 96),
              });
            }}
          >
            <button
              type="button"
              className="cursor-grab rounded-l px-1.5 py-0.5 text-left text-[10px] font-bold leading-tight text-white active:cursor-grabbing"
              style={{ background: color }}
              title={label.kind === "position" ? "Arraste para criar alvo ou stop" : "Arraste: o fechamento do gráfico escolhe limite ou stop"}
            >
              {text}
              {label.kind === "order" && !compact && (
                <span className="ml-1 font-mono font-semibold">· {groupDecimal(shown)}</span>
              )}
            </button>
            {label.kind === "order" && (
              <button
                type="button"
                data-action="cancel"
                aria-label="Cancelar ordem"
                title="Cancelar ordem"
                className="rounded-r border-l border-white/30 px-1 text-[10px] font-bold leading-none text-white"
                style={{ background: color }}
                onPointerDown={(event) => event.stopPropagation()}
                onClick={(event) => {
                  event.stopPropagation();
                  onCancel(label.id);
                }}
              >
                ×
              </button>
            )}
          </div>
        );
      })}
      {menu && (
        <div
          className="pointer-events-auto absolute z-30 w-44 rounded-md border border-[#d8d2c4] bg-[#f7f5f0] py-1 text-xs text-stone-800 shadow-lg"
          style={{ left: menu.x, top: menu.y }}
          onPointerDown={(event) => event.stopPropagation()}
        >
          <p className="px-3 py-1 text-[10px] text-stone-500">Arraste a etiqueta para editar</p>
          <button
            type="button"
            className="flex w-full items-center px-3 py-1.5 text-left hover:bg-white"
            onClick={() => {
              setMenu(null);
              onCancel(menu.id);
            }}
          >
            Cancelar ordem
          </button>
          <button
            type="button"
            className="flex w-full items-center justify-between px-3 py-1.5 text-left hover:bg-white"
            onClick={() => {
              setCompact((value) => !value);
              setMenu(null);
            }}
          >
            {compact ? "Exibição completa" : "Exibição compacta"}
          </button>
        </div>
      )}
    </div>
  );
}
