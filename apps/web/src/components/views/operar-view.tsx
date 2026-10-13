"use client";

import { useMemo, useState } from "react";
import { useTerminal } from "@/lib/trading/terminal-context";
import { formatRatioPercent, groupDecimal, signOf } from "@/lib/trading/format";
import { MarketWatchDrawer } from "./market-watch-drawer";
import { MarketDepthDrawer } from "./market-depth-drawer";
import { OrderTicket } from "./order-ticket";
import { PositionsPanel } from "./positions-panel";
import { ProTradingChart } from "./pro-trading-chart";
import { IconPlus, IconX } from "@/components/ui/icons";

export function OperarView() {
  const {
    activeTicker,
    setActiveTicker,
    chartTabs,
    activeChartTabId,
    selectChartTab,
    closeChartTab,
    addChartTab,
    setTimeframeForActiveTab,
    bookDrawerOpen,
    setBookDrawerOpen,
    currentTimeStr,
    currentDateStr,
    activeQuote,
  } = useTerminal();

  const [tickerDraft, setTickerDraft] = useState<{ forTicker: string; text: string } | null>(null);
  const [autoScale, setAutoScale] = useState(false);
  const tickerInput = tickerDraft?.forTicker === activeTicker ? tickerDraft.text : activeTicker;

  const activeTab = useMemo(
    () => chartTabs.find((t) => t.id === activeChartTabId) ?? chartTabs[0],
    [chartTabs, activeChartTabId]
  );

  const lastPrice = activeQuote?.lastPrice ? groupDecimal(activeQuote.lastPrice) : "—";
  const changePercent = formatRatioPercent(activeQuote?.changePercent);
  const changePoints = activeQuote?.changePoints ? groupDecimal(activeQuote.changePoints) : "—";
  const min24 = activeQuote?.min ? groupDecimal(activeQuote.min) : "—";
  const max24 = activeQuote?.max ? groupDecimal(activeQuote.max) : "—";
  const assetName = activeQuote?.name ?? "Fora do pregão";
  const changeSign = signOf(activeQuote?.changePoints);

  const handleTickerSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    const clean = tickerInput.trim().toUpperCase();
    if (clean) {
      setActiveTicker(clean);
    }
  };

  return (
    <div className="flex h-auto min-h-full w-full flex-col overflow-hidden rounded-xl border border-[#ded8cb] bg-white shadow-sm select-none lg:h-full lg:min-h-0">
      {/* 1. Multi-Chart Tabs Bar */}
      <div className="flex h-9 shrink-0 items-center justify-between border-b border-[#ded8cb] bg-[#f5f2e9] px-2 text-xs">
        <div className="flex items-center gap-1 overflow-x-auto">
          {chartTabs.map((tab) => {
            const isActive = tab.id === activeChartTabId;
            return (
              <div
                key={tab.id}
                onClick={() => selectChartTab(tab.id)}
                className={`flex cursor-pointer items-center gap-2 rounded-t-md px-3 py-1.5 transition text-xs font-semibold ${
                  isActive
                    ? "bg-white text-stone-900 border-t-2 border-t-[#8a6833] shadow-xs"
                    : "text-stone-600 hover:bg-[#eae4d5] hover:text-stone-900"
                }`}
              >
                <span>
                  {tab.ticker} <span className="text-[10px] text-stone-400 font-normal">{tab.timeframe}</span>
                </span>
                {chartTabs.length > 1 && (
                  <button
                    type="button"
                    onClick={(e) => {
                      e.stopPropagation();
                      closeChartTab(tab.id);
                    }}
                    className="rounded p-0.5 text-stone-400 hover:bg-stone-200 hover:text-stone-700"
                  >
                    <IconX className="h-3 w-3" />
                  </button>
                )}
              </div>
            );
          })}

          <button
            type="button"
            onClick={() => addChartTab("PETR4", "5m")}
            className="flex h-6 w-6 items-center justify-center rounded-md border border-[#dcd6c7] bg-[#fdfcf9] text-stone-600 hover:bg-white hover:text-stone-900 shadow-2xs"
            title="Adicionar nova aba gráfica"
          >
            <IconPlus className="h-3.5 w-3.5" />
          </button>
        </div>

        {/* Toolbar */}
        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={() => setBookDrawerOpen(!bookDrawerOpen)}
            className={`rounded border px-2.5 py-1 text-[11px] font-semibold transition ${
              bookDrawerOpen
                ? "border-emerald-600 bg-emerald-50 text-emerald-800 shadow-2xs"
                : "border-[#dcd6c7] bg-white text-stone-700 hover:bg-stone-50"
            }`}
          >
            📊 Book de Ofertas (DOM)
          </button>

          {/* Timeframe Zoom Buttons */}
          <div className="flex items-center gap-1 rounded bg-[#eae4d5] p-0.5 text-[10px] font-bold">
            <span className="px-1 text-stone-500 font-medium">Zoom:</span>
            {["1m", "5m", "15m", "1h"].map((tf) => (
              <button
                key={tf}
                type="button"
                onClick={() => setTimeframeForActiveTab(tf)}
                className={`rounded px-1.5 py-0.5 transition ${
                  activeTab?.timeframe === tf
                    ? "bg-white text-stone-900 shadow-2xs"
                    : "text-stone-600 hover:text-stone-900"
                }`}
              >
                {tf}
              </button>
            ))}
          </div>

          <button
            type="button"
            onClick={() => setAutoScale(!autoScale)}
            className="rounded border border-[#dcd6c7] bg-white px-2 py-1 text-[11px] font-medium text-stone-600 hover:bg-stone-50"
          >
            Ajuste Auto ({autoScale ? "ON" : "OFF"})
          </button>
        </div>
      </div>

      {/* 2. Active Asset Quote Info Bar */}
      <div className="flex h-13 shrink-0 items-center justify-between border-b border-[#ded8cb] bg-[#faf8f4] px-4">
        {/* Left: Input Search + Name + Live Badge */}
        <div className="flex items-center gap-4">
          <form onSubmit={handleTickerSubmit} className="flex items-center gap-2">
            <div className="relative">
              <input
                type="text"
                value={tickerInput}
                onChange={(e) => setTickerDraft({ forTicker: activeTicker, text: e.target.value })}
                onFocus={() => setTickerDraft({ forTicker: activeTicker, text: "" })}
                placeholder="Código (Enter)"
                className="w-28 rounded-md border border-[#ded8cb] bg-white px-2 py-1 text-xs font-bold text-stone-900 uppercase focus:border-amber-500 focus:outline-hidden"
              />
            </div>
            <span className="text-xs font-bold text-stone-800">
              {activeTicker} - <span className="font-normal text-stone-600">{assetName}</span>
            </span>
          </form>

          <div className="flex items-center gap-1.5 rounded-full bg-emerald-50 border border-emerald-300 px-2.5 py-0.5 text-[10px] font-bold text-emerald-800">
            <span className="h-1.5 w-1.5 rounded-full bg-emerald-500 animate-pulse" />
            <span>AO VIVO</span>
          </div>
        </div>

        {/* Center: Prices & Extremes */}
        <div className="flex items-center gap-6 text-xs">
          <div>
            <div className="text-sm font-black font-mono text-stone-900">{lastPrice}</div>
            <div className="text-[9px] text-stone-400 leading-none">Preço de mercado</div>
          </div>

          <div>
            <div
              className={`font-mono font-bold ${
                changeSign < 0 ? "text-[#be123c]" : changeSign > 0 ? "text-[#0d6838]" : "text-stone-500"
              }`}
            >
              {changePoints} ({changePercent})
            </div>
            <div className="text-[9px] text-stone-400 leading-none">Variação do pregão</div>
          </div>

          <div className="flex gap-3 text-stone-600">
            <div>
              <span className="font-mono font-bold text-stone-900">{max24}</span>
              <span className="ml-1 text-[9px] text-stone-400">Máx do pregão</span>
            </div>
            <div>
              <span className="font-mono font-bold text-stone-900">{min24}</span>
              <span className="ml-1 text-[9px] text-stone-400">Mín do pregão</span>
            </div>
          </div>
        </div>

        {/* Right: Brasília Trading Clock */}
        <div className="text-right font-mono">
          <div className="text-xs font-bold text-stone-800">{currentTimeStr}</div>
          <div className="text-[10px] text-stone-500">{currentDateStr} · Brasília</div>
        </div>
      </div>

      {/* 3. Main Center Workspace: Drawers + Chart + Boleta */}
      <div className="relative flex min-h-[320px] flex-1 flex-col overflow-hidden lg:min-h-0 lg:flex-row">
        {/* Drawers */}
        <MarketWatchDrawer />
        <MarketDepthDrawer />

        {/* Chart Canvas */}
        <div className="min-h-[280px] min-w-0 flex-1 overflow-hidden lg:min-h-0">
          <ProTradingChart />
        </div>

        {/* Boleta Lateral Direita */}
        <OrderTicket />
      </div>

      {/* 4. Bottom Positions & Orders Panel */}
      <PositionsPanel />
    </div>
  );
}
