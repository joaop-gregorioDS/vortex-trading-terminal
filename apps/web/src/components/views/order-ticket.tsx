"use client";

import { useState } from "react";
import { useTerminal } from "@/lib/trading/terminal-context";
import { flattenPosition, reversePosition, submitOrder, type OrderType } from "@/lib/trading/transport";
import { groupDecimal } from "@/lib/trading/format";
import { idleProtection, protectionPayload, ProtectionFields, type ProtectionDraft } from "./protection-fields";

export function OrderTicket() {
  const { activeTicker, activeQuote, refreshAll, emergencyFlattenPositions, chartPriceDraft } = useTerminal();

  const [quantity, setQuantity] = useState("1");
  const [orderType, setOrderType] = useState<OrderType>("limit");
  const [limitDraft, setLimitDraft] = useState<{ forTicker: string; text: string } | null>(null);
  const [notice, setNotice] = useState<{ tone: "ok" | "error"; text: string } | null>(null);
  const [busy, setBusy] = useState(false);
  const [strategy, setStrategy] = useState<ProtectionDraft>(idleProtection);
  const [seenChartPrice, setSeenChartPrice] = useState(chartPriceDraft);
  if (chartPriceDraft !== seenChartPrice) {
    setSeenChartPrice(chartPriceDraft);
    if (chartPriceDraft && chartPriceDraft.ticker === activeTicker) {
      setOrderType("limit");
      setLimitDraft({ forTicker: chartPriceDraft.ticker, text: chartPriceDraft.price });
    }
  }
  const limitPrice = limitDraft?.forTicker === activeTicker ? limitDraft.text : (activeQuote?.lastPrice ?? "");

  const reference = activeQuote?.lastPrice ? groupDecimal(activeQuote.lastPrice) : "—";

  const send = async (side: "buy" | "sell") => {
    setBusy(true);
    setNotice(null);
    try {
      if (!activeQuote?.instrumentId) {
        setNotice({ tone: "error", text: "Este ativo não faz parte do pregão." });
        return;
      }
      if (orderType === "limit" && limitPrice.trim().length === 0) {
        setNotice({ tone: "error", text: "Informe o preço limite." });
        return;
      }
      const protection = protectionPayload(strategy);
      if (protection.error) {
        setNotice({ tone: "error", text: protection.error });
        return;
      }

      const result = await submitOrder({
        instrumentId: activeQuote.instrumentId,
        side,
        type: orderType,
        quantity: quantity.trim(),
        limitPrice: orderType === "limit" ? limitPrice.trim() : undefined,
        protection: protection.body,
      });
      if (result.ok) {
        const verb = side === "buy" ? "Compra" : "Venda";
        const how = orderType === "limit" ? "limitada" : "a mercado";
        setNotice({
          tone: "ok",
          text: `${verb} ${how} de ${activeTicker} enviada. A execução acontece no próximo print deste ativo.`,
        });
        await refreshAll();
      } else {
        setNotice({ tone: "error", text: result.error });
      }
    } finally {
      setBusy(false);
    }
  };

  const handleFlatten = async () => {
    setBusy(true);
    setNotice(null);
    try {
      const result = await emergencyFlattenPositions();
      setNotice({ tone: result.ok ? "ok" : "error", text: result.message });
    } finally {
      setBusy(false);
    }
  };

  const handleBookAction = async (action: "flatten" | "reverse") => {
    setBusy(true);
    setNotice(null);
    try {
      if (!activeQuote?.instrumentId) {
        setNotice({ tone: "error", text: "Este ativo não faz parte do pregão." });
        return;
      }
      const result = action === "flatten"
        ? await flattenPosition(activeQuote.instrumentId)
        : await reversePosition(activeQuote.instrumentId);
      if (!result.ok) {
        setNotice({ tone: "error", text: result.error });
        return;
      }
      if (result.value.status === "flat") {
        setNotice({ tone: "error", text: "Não há posição aberta neste ativo." });
        return;
      }
      if (result.value.order?.status === "rejected") {
        setNotice({ tone: "error", text: "A ordem foi recusada antes de entrar no livro." });
        return;
      }
      setNotice({
        tone: "ok",
        text: action === "flatten"
          ? "Zeragem enviada. As ordens que já estão no livro continuam. A execução acontece no próximo print."
          : "Inversão enviada. A ordem a mercado executa no próximo print.",
      });
      await refreshAll();
    } finally {
      setBusy(false);
    }
  };

  return (
    <aside className="flex w-full shrink-0 select-none flex-col justify-between overflow-y-auto border-t border-[#ded8cb] bg-white p-4 lg:w-80 lg:border-t-0 lg:border-l">
      <div className="space-y-4">
        <div className="flex items-center justify-between border-b border-[#eeebe2] pb-3">
          <span className="text-base font-black tracking-tight text-stone-900">{activeTicker}</span>
          <span className="font-mono text-xs font-bold text-stone-700">{reference}</span>
        </div>

        <div className="rounded-md border border-[#e5e1d7] bg-[#f9f8f5] p-2 text-[11px] leading-tight">
          <div className="font-semibold text-stone-800">Conta demo compartilhada</div>
          <div className="text-stone-500">Preço do Yahoo neste ativo. A ordem de papel não sai da conta de demonstração.</div>
        </div>

        <div className="grid grid-cols-2 gap-1 rounded-md bg-[#f6f3eb] p-1 text-[11px] font-semibold">
          <button
            type="button"
            onClick={() => setOrderType("limit")}
            className={`rounded px-2 py-1 ${orderType === "limit" ? "bg-white text-stone-900 shadow-xs" : "text-stone-500"}`}
          >
            Limitada
          </button>
          <button
            type="button"
            onClick={() => setOrderType("market")}
            className={`rounded px-2 py-1 ${orderType === "market" ? "bg-white text-stone-900 shadow-xs" : "text-stone-500"}`}
          >
            A mercado
          </button>
        </div>

        <div className="space-y-3">
          <div>
            <div className="mb-1 text-[11px] font-semibold text-stone-600">
              {orderType === "limit" ? "Preço limite" : "Preço de referência"}
            </div>
            <input
              type="text"
              value={orderType === "limit" ? limitPrice : reference}
              readOnly={orderType === "market"}
              onChange={(event) => setLimitDraft({ forTicker: activeTicker, text: event.target.value })}
              className="w-full rounded-md border border-[#d8d2c4] bg-[#fdfcf9] px-3 py-1.5 text-right font-mono text-xs font-bold text-stone-900 focus:border-amber-500 focus:outline-hidden read-only:text-stone-500"
            />
          </div>

          <div>
            <div className="mb-1 text-[11px] font-semibold text-stone-600">Quantidade</div>
            <input
              type="text"
              value={quantity}
              onChange={(event) => setQuantity(event.target.value)}
              className="w-full rounded-md border border-[#d8d2c4] bg-[#fdfcf9] px-3 py-1.5 text-right font-mono text-xs font-bold text-stone-900 focus:border-amber-500 focus:outline-hidden"
            />
          </div>
        </div>

        <ProtectionFields value={strategy} onChange={setStrategy} />

        {notice && (
          <div
            className={`rounded-md p-2 text-xs font-medium ${
              notice.tone === "ok"
                ? "border border-emerald-200 bg-emerald-50 text-emerald-800"
                : "border border-rose-200 bg-rose-50 text-rose-800"
            }`}
          >
            {notice.text}
          </div>
        )}

        <div className="space-y-2 pt-1">
          <div className="grid grid-cols-2 gap-2">
            <button
              type="button"
              disabled={busy}
              onClick={() => send("buy")}
              className="rounded-lg bg-[#15803d] py-3 text-xs font-bold text-white shadow-xs transition hover:bg-[#166534] active:scale-[0.98] disabled:opacity-50"
            >
              Comprar
            </button>
            <button
              type="button"
              disabled={busy}
              onClick={() => send("sell")}
              className="rounded-lg bg-[#b91c1c] py-3 text-xs font-bold text-white shadow-xs transition hover:bg-[#991b1b] active:scale-[0.98] disabled:opacity-50"
            >
              Vender
            </button>
          </div>
          <div className="grid grid-cols-2 gap-2">
            <button
              type="button"
              disabled={busy}
              onClick={() => void handleBookAction("reverse")}
              className="rounded-lg border border-stone-300 py-2 text-xs font-bold text-stone-800 transition hover:bg-stone-50 active:scale-[0.98] disabled:opacity-50"
            >
              Inverter
            </button>
            <button
              type="button"
              disabled={busy}
              onClick={() => void handleBookAction("flatten")}
              className="rounded-lg border border-blue-300 py-2 text-xs font-bold text-blue-800 transition hover:bg-blue-50 active:scale-[0.98] disabled:opacity-50"
            >
              Zerar
            </button>
          </div>
          <button
            type="button"
            disabled={busy}
            onClick={handleFlatten}
            className="w-full rounded-lg bg-[#1d4ed8] py-2.5 text-xs font-bold text-white shadow-xs transition hover:bg-[#1e40af] active:scale-[0.98] disabled:opacity-50"
          >
            Cancelar + Zerar
          </button>
        </div>
      </div>

      <div className="border-t border-[#f0ece4] pt-3 text-center text-[10px] text-stone-400">
        A limitada descansa neste livro até um print posterior alcançá-la.
      </div>
    </aside>
  );
}
