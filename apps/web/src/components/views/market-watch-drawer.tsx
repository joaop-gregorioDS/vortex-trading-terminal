"use client";

import { useMemo, useState } from "react";
import { useTerminal } from "@/lib/trading/terminal-context";
import { formatRatioPercent, groupDecimal, signOf } from "@/lib/trading/format";
import { IconSearch, IconX } from "@/components/ui/icons";

export function MarketWatchDrawer() {
  const {
    marketDrawerOpen,
    setMarketDrawerOpen,
    activeTicker,
    setActiveTicker,
    marketQuotes,
  } = useTerminal();

  const [search, setSearch] = useState("");

  const filteredQuotes = useMemo(() => {
    if (!search.trim()) return marketQuotes;
    const term = search.toLowerCase();
    return marketQuotes.filter(
      (q) => q.ticker.toLowerCase().includes(term) || q.name.toLowerCase().includes(term)
    );
  }, [marketQuotes, search]);

  if (!marketDrawerOpen) return null;

  return (
    <aside className="absolute inset-y-0 left-0 z-30 flex w-72 flex-col border-r border-[#ded8cb] bg-white shadow-xl animate-in slide-in-from-left duration-150">
      {/* Header */}
      <div className="flex h-11 items-center justify-between border-b border-[#e5e1d7] px-3">
        <h2 className="text-xs font-bold text-stone-900">Observação do Mercado</h2>
        <button
          type="button"
          onClick={() => setMarketDrawerOpen(false)}
          className="rounded p-1 text-stone-500 hover:bg-stone-100 hover:text-stone-800"
          title="Fechar"
        >
          <IconX className="h-4 w-4" />
        </button>
      </div>

      {/* Search Input */}
      <div className="border-b border-[#e5e1d7] p-2">
        <div className="relative">
          <IconSearch className="absolute left-2.5 top-2.5 h-3.5 w-3.5 text-stone-400" />
          <input
            type="text"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder={`Filtrar ${marketQuotes.length || 15} ativos...`}
            className="w-full rounded-md border border-[#e2ddd1] bg-[#f9f8f5] py-1.5 pl-8 pr-3 text-xs text-stone-900 placeholder:text-stone-400 focus:border-amber-500 focus:outline-hidden"
          />
        </div>
      </div>

      {/* Table Header */}
      <div className="grid grid-cols-3 border-b border-[#e5e1d7] bg-[#f5f2e9] px-3 py-1.5 text-[10px] font-semibold text-stone-500 uppercase tracking-wider">
        <span>Ativo</span>
        <span className="text-right">Último</span>
        <span className="text-right">Var %</span>
      </div>

      {/* Tickers List */}
      <div className="flex-1 overflow-y-auto divide-y divide-[#f2ede4]">
        {filteredQuotes.map((q) => {
          const isSelected = q.ticker === activeTicker;
          const changeSign = signOf(q.changePercent);

          return (
            <button
              key={q.ticker}
              type="button"
              onClick={() => setActiveTicker(q.ticker)}
              className={`grid w-full grid-cols-3 items-center px-3 py-2 text-left text-xs transition ${
                isSelected
                  ? "bg-[#f5efe2] font-bold text-stone-900 border-l-3 border-[#8a6833]"
                  : "hover:bg-[#fbf9f5] text-stone-700"
              }`}
            >
              <div className="truncate">
                <span className="font-semibold text-stone-900">{q.ticker}</span>
              </div>
              <div className="text-right font-mono text-stone-800">{q.lastPrice ? groupDecimal(q.lastPrice) : "—"}</div>
              <div
                className={`text-right font-mono text-[11px] font-semibold ${
                  changeSign > 0
                    ? "text-[#0d6838]"
                    : changeSign < 0
                    ? "text-[#be123c]"
                    : "text-stone-500"
                }`}
              >
                {formatRatioPercent(q.changePercent)}
              </div>
            </button>
          );
        })}
      </div>

      {/* Footer */}
      <div className="border-t border-[#e5e1d7] bg-[#f9f8f5] px-3 py-2 text-[10px] text-stone-400">
        {marketQuotes.length} ativos do pregão simulado
      </div>
    </aside>
  );
}
