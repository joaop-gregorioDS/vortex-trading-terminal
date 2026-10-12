"use client";

import { useEffect, useState } from "react";
import { useTerminal } from "@/lib/trading/terminal-context";
import { fetchMarketDepth, submitOrder, type MarketDepthTransport } from "@/lib/trading/transport";
import { groupDecimal } from "@/lib/trading/format";
import { IconX } from "@/components/ui/icons";

export function MarketDepthDrawer() {
  const {
    bookDrawerOpen,
    setBookDrawerOpen,
    activeTicker,
    marketQuotes,
    refreshAll,
  } = useTerminal();

  const [depth, setDepth] = useState<MarketDepthTransport | null>(null);
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const quote = marketQuotes.find((item) => item.ticker === activeTicker);
  const instrumentId = depth?.instrumentId || quote?.instrumentId || "";

  useEffect(() => {
    if (!bookDrawerOpen) return;

    let active = true;
    const loadDepth = async () => {
      const res = await fetchMarketDepth(activeTicker);
      if (!active) return;
      if (res.ok) {
        setDepth(res.value);
        setError(null);
      } else {
        setDepth(null);
        setError(res.error);
      }
    };

    void loadDepth();
    const timer = setInterval(loadDepth, 1500);
    return () => {
      active = false;
      clearInterval(timer);
    };
  }, [bookDrawerOpen, activeTicker]);

  if (!bookDrawerOpen) return null;

  const handleMarketOrder = async (side: "buy" | "sell") => {
    setBusy(true);
    setNotice(null);
    try {
      if (!instrumentId) {
        setNotice("Este ativo não faz parte do pregão simulado.");
        return;
      }
      const res = await submitOrder({
        instrumentId,
        side,
        type: "market",
        quantity: "1",
      });
      if (res.ok) {
        setNotice(side === "buy" ? "Compra a mercado enviada." : "Venda a mercado enviada.");
        await refreshAll();
      } else {
        setNotice(res.error);
      }
    } finally {
      setBusy(false);
    }
  };

  return (
    <aside className="absolute inset-y-0 right-0 z-30 flex w-96 flex-col border-l border-[#ded8cb] bg-white shadow-2xl select-none">
      <div className="flex h-11 items-center justify-between border-b border-[#e5e1d7] bg-[#fbf9f5] px-4">
        <h2 className="text-xs font-bold text-stone-900">Livro simulado · {activeTicker}</h2>
        <button
          type="button"
          onClick={() => setBookDrawerOpen(false)}
          className="rounded p-1 text-stone-500 hover:bg-stone-200 hover:text-stone-800"
          title="Fechar"
        >
          <IconX className="h-4 w-4" />
        </button>
      </div>

      <div className="flex-1 space-y-3 overflow-y-auto p-3">
        {error && (
          <p className="rounded border border-rose-200 bg-rose-50 p-2 text-xs text-rose-800">{error}</p>
        )}
        {notice && (
          <p className="rounded border border-emerald-200 bg-emerald-50 p-2 text-center text-xs font-medium text-emerald-800">
            {notice}
          </p>
        )}

        <div className="rounded-lg border border-[#e8e4da] bg-[#f9f8f5] p-3 text-xs">
          <div className="flex items-center justify-between">
            <span className="text-stone-500">Último negócio</span>
            <span className="font-mono text-sm font-bold text-stone-900">
              {depth?.lastPrice ? groupDecimal(depth.lastPrice) : "—"}
            </span>
          </div>
          <div className="mt-2 flex items-center justify-between">
            <span className="text-stone-500">Volume restante neste tick</span>
            <span className="font-mono font-semibold text-stone-800">
              {depth?.availableVolume ? groupDecimal(depth.availableVolume) : "—"}
            </span>
          </div>
          <p className="mt-2 text-[10px] leading-relaxed text-stone-500">
            O pregão oferece um preço e uma quantidade por tick. Não há uma escada de ofertas além disso.
          </p>
        </div>

        <div className="overflow-hidden rounded-lg border border-[#e8e4da] text-xs">
          <div className="grid grid-cols-4 bg-[#eae5d8] py-1.5 text-center text-[10px] font-bold uppercase tracking-wider text-stone-700">
            <span>Lado</span>
            <span>Preço</span>
            <span>Qtd</span>
            <span>Estado</span>
          </div>
          {depth && depth.restingOrders.length === 0 && (
            <p className="px-3 py-4 text-center text-stone-500">Nenhuma ordem em aberto neste ativo.</p>
          )}
          {depth?.restingOrders.map((order) => (
            <div
              key={order.orderId}
              className="grid grid-cols-4 border-t border-[#f2ede4] py-1.5 text-center font-mono"
            >
              <span className={order.side === "buy" ? "font-semibold text-emerald-700" : "font-semibold text-rose-700"}>
                {order.side === "buy" ? "Compra" : "Venda"}
              </span>
              <span>{order.limitPrice ? groupDecimal(order.limitPrice) : "Mercado"}</span>
              <span>{groupDecimal(order.remainingQuantity)}</span>
              <span className="text-stone-500">{order.status === "partially_filled" ? "Parcial" : "Aberta"}</span>
            </div>
          ))}
        </div>
      </div>

      <div className="space-y-2 border-t border-[#e5e1d7] bg-[#fbf9f5] p-3">
        <div className="grid grid-cols-2 gap-2">
          <button
            type="button"
            disabled={busy || !instrumentId}
            onClick={() => handleMarketOrder("buy")}
            className="rounded-lg bg-[#15803d] py-2 text-xs font-bold text-white hover:bg-[#166534] disabled:opacity-50"
          >
            Comprar a mercado
          </button>
          <button
            type="button"
            disabled={busy || !instrumentId}
            onClick={() => handleMarketOrder("sell")}
            className="rounded-lg bg-[#b91c1c] py-2 text-xs font-bold text-white hover:bg-[#991b1b] disabled:opacity-50"
          >
            Vender a mercado
          </button>
        </div>
      </div>
    </aside>
  );
}
