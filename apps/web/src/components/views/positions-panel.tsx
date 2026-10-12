"use client";

import { useState } from "react";
import { useTerminal } from "@/lib/trading/terminal-context";
import { cancelOrder } from "@/lib/trading/transport";
import { formatSignedBrl, groupDecimal, signOf, statusTone, typeLabels } from "@/lib/trading/format";

export function PositionsPanel() {
  const {
    orders,
    portfolio,
    marketQuotes,
    closePositionByTicker,
    refreshAll,
  } = useTerminal();

  const [activeTab, setActiveTab] = useState<"positions" | "orders">("positions");
  const [closingTicker, setClosingTicker] = useState<string | null>(null);

  const openPositions = (portfolio?.positions ?? [])
    .filter((position) => !position.isClosed)
    .map((position) => {
      const quote = marketQuotes.find((item) => item.instrumentId === position.instrumentId);
      return {
        ticker: quote?.ticker ?? position.instrumentId,
        quantity: position.quantity,
        averageCost: position.averageCost,
        lastPrice: quote?.lastPrice ?? "",
        pnl: position.unrealizedProfitAndLoss,
      };
    });

  const pendingOrders = orders.filter(
    (o) => o.status === "created" || o.status === "accepted" || o.status === "partially_filled"
  );

  const handleClose = async (ticker: string) => {
    setClosingTicker(ticker);
    try {
      await closePositionByTicker(ticker);
    } finally {
      setClosingTicker(null);
    }
  };

  const handleCancelOrder = async (orderId: string) => {
    await cancelOrder(orderId);
    await refreshAll();
  };

  return (
    <div className="h-44 border-t border-[#ded8cb] bg-white flex flex-col select-none">
      {/* Tabs */}
      <div className="flex items-center gap-6 border-b border-[#e5e1d7] bg-[#f9f8f5] px-4 text-xs font-semibold">
        <button
          type="button"
          onClick={() => setActiveTab("positions")}
          className={`py-2 transition border-b-2 ${
            activeTab === "positions"
              ? "border-[#8a6833] text-stone-900 font-bold"
              : "border-transparent text-stone-500 hover:text-stone-800"
          }`}
        >
          Posições abertas ({openPositions.length})
        </button>
        <button
          type="button"
          onClick={() => setActiveTab("orders")}
          className={`py-2 transition border-b-2 ${
            activeTab === "orders"
              ? "border-[#8a6833] text-stone-900 font-bold"
              : "border-transparent text-stone-500 hover:text-stone-800"
          }`}
        >
          Ordens pendentes ({pendingOrders.length})
        </button>
      </div>

      {/* Table Content */}
      <div className="flex-1 overflow-y-auto">
        {activeTab === "positions" ? (
          <table className="w-full text-left text-xs">
            <thead className="border-b border-[#eeebe2] bg-[#fdfcf9] text-[10px] font-semibold text-stone-500 uppercase tracking-wider">
              <tr>
                <th className="py-1.5 px-4">Ativo</th>
                <th className="py-1.5 px-4">Tipo</th>
                <th className="py-1.5 px-4 text-center">Qtd</th>
                <th className="py-1.5 px-4 text-right">Preço médio</th>
                <th className="py-1.5 px-4 text-right">Preço atual</th>
                <th className="py-1.5 px-4 text-right">P&L aberto</th>
                <th className="py-1.5 px-4 text-center">Ação</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-[#f2ede4]">
              {openPositions.length === 0 && (
                <tr>
                  <td colSpan={7} className="py-8 text-center text-xs text-stone-400">
                    Nenhuma posição aberta neste pregão.
                  </td>
                </tr>
              )}
              {openPositions.map((pos) => {
                const pnlSign = signOf(pos.pnl);
                const sold = signOf(pos.quantity) < 0;
                return (
                <tr key={pos.ticker} className="hover:bg-[#faf8f4] transition">
                  <td className="py-1.5 px-4 font-bold text-stone-900">{pos.ticker}</td>
                  <td className="py-1.5 px-4 font-semibold">
                    <span className={sold ? "text-rose-600" : "text-sky-600"}>{sold ? "Venda" : "Compra"}</span>
                  </td>
                  <td className="py-1.5 px-4 text-center font-mono">{groupDecimal(pos.quantity)}</td>
                  <td className="py-1.5 px-4 text-right font-mono text-stone-700">{groupDecimal(pos.averageCost)}</td>
                  <td className="py-1.5 px-4 text-right font-mono text-stone-900">
                    {pos.lastPrice ? groupDecimal(pos.lastPrice) : "—"}
                  </td>
                  <td
                    className={`py-1.5 px-4 text-right font-mono font-bold ${
                      pnlSign > 0 ? "text-[#0d6838]" : pnlSign < 0 ? "text-[#be123c]" : "text-stone-600"
                    }`}
                  >
                    {formatSignedBrl(pos.pnl)}
                  </td>
                  <td className="py-1.5 px-4 text-center">
                    <button
                      type="button"
                      disabled={closingTicker === pos.ticker}
                      onClick={() => handleClose(pos.ticker)}
                      className="rounded border border-rose-300 px-2.5 py-0.5 text-[11px] font-semibold text-rose-600 hover:bg-rose-50 transition active:scale-95 disabled:opacity-50"
                    >
                      {closingTicker === pos.ticker ? "Fechando..." : "Fechar"}
                    </button>
                  </td>
                </tr>
                );
              })}
            </tbody>
          </table>
        ) : (
          <div>
            {pendingOrders.length === 0 ? (
              <div className="py-8 text-center text-xs text-stone-400">
                Nenhuma ordem pendente no momento.
              </div>
            ) : (
              <table className="w-full text-left text-xs">
                <thead className="border-b border-[#eeebe2] bg-[#fdfcf9] text-[10px] font-semibold text-stone-500 uppercase tracking-wider">
                  <tr>
                    <th className="py-1.5 px-4">Ativo</th>
                    <th className="py-1.5 px-4">Lado</th>
                    <th className="py-1.5 px-4">Tipo</th>
                    <th className="py-1.5 px-4 text-center">Qtd Restante</th>
                    <th className="py-1.5 px-4 text-right">Preço Limite</th>
                    <th className="py-1.5 px-4 text-center">Status</th>
                    <th className="py-1.5 px-4 text-center">Ação</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-[#f2ede4]">
                  {pendingOrders.map((ord) => (
                    <tr key={ord.orderId} className="hover:bg-[#faf8f4]">
                      <td className="py-1.5 px-4 font-bold text-stone-900">
                        {marketQuotes.find((quote) => quote.instrumentId === ord.instrumentId)?.ticker ?? "—"}
                      </td>
                      <td className="py-1.5 px-4 font-semibold uppercase">{ord.side}</td>
                      <td className="py-1.5 px-4">
                        {ord.role === "target" ? "Alvo" : ord.role === "stop" ? "Stop" : typeLabels[ord.type]}
                        {ord.origin === "chart" ? " · gráfico" : ""}
                      </td>
                      <td className="py-1.5 px-4 text-center font-mono">{ord.remainingQuantity}</td>
                      <td className="py-1.5 px-4 text-right font-mono">{ord.limitPrice ?? "—"}</td>
                      <td className="py-1.5 px-4 text-center">
                        <span className={`rounded px-1.5 py-0.5 text-[10px] ${statusTone(ord.status)}`}>
                          {ord.status}
                        </span>
                      </td>
                      <td className="py-1.5 px-4 text-center">
                        <button
                          type="button"
                          onClick={() => handleCancelOrder(ord.orderId)}
                          className="rounded border border-slate-300 px-2 py-0.5 text-[11px] text-slate-700 hover:bg-slate-100"
                        >
                          Cancelar
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </div>
        )}
      </div>
    </div>
  );
}
