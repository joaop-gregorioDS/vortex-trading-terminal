"use client";

import { useTerminal } from "@/lib/trading/terminal-context";
import {
  formatBrl,
  formatDateTime,
  formatSignedBrl,
  groupDecimal,
  signOf,
} from "@/lib/trading/format";
import {
  IconArrowUpRight,
  IconBarChart,
  IconBriefcase,
  IconSparkles,
  IconWallet,
} from "@/components/ui/icons";

export function VisaoGeralView() {
  const { setActiveNav, setActiveTicker, performance, closedTrades, portfolio, marketQuotes } = useTerminal();

  const openPositions = (portfolio?.positions ?? [])
    .filter((position) => !position.isClosed)
    .map((position) => {
      const quote = marketQuotes.find((item) => item.instrumentId === position.instrumentId);
      return {
        ticker: quote?.ticker ?? position.instrumentId,
        quantity: position.quantity,
        pnl: position.unrealizedProfitAndLoss,
      };
    });

  const recentTrades = closedTrades.slice(0, 5);
  const saldo = performance ? formatBrl(performance.currentBalance) : "—";
  const patrimonio = performance ? formatBrl(performance.equity) : "—";
  const pnlRealizado = performance ? formatSignedBrl(performance.realizedPnl) : "—";
  const unrealized = portfolio ? formatSignedBrl(portfolio.unrealizedProfitAndLoss) : "—";
  const tradeCount = performance?.totalTrades ?? 0;
  const realizedSign = signOf(performance?.realizedPnl);

  return (
    <div className="mx-auto max-w-7xl space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-4">
        <div>
          <div className="text-[11px] font-bold tracking-widest text-[#8a6833] uppercase">
            INVESTIMENTOS PRO / VISÃO GERAL
          </div>
          <h1 className="mt-1 text-2xl font-bold tracking-tight text-[#1c1917] sm:text-3xl">Olá, Convidado</h1>
          <p className="mt-1 text-xs text-stone-500">Resumo da conta simulada compartilhada e das operações deste pregão.</p>
        </div>
        <button
          type="button"
          onClick={() => setActiveNav("operar")}
          className="flex items-center gap-1.5 rounded-lg bg-[#8a6833] px-4 py-2.5 text-xs font-semibold text-white shadow-sm transition hover:bg-[#785929]"
        >
          <span>Abrir mesa de operações</span>
          <IconArrowUpRight className="h-4 w-4" />
        </button>
      </div>

      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <div className="rounded-xl border border-[#e5e1d7] bg-white p-5 shadow-xs">
          <div className="flex items-center gap-3">
            <div className="flex h-9 w-9 items-center justify-center rounded-lg bg-[#f4efe4] text-[#8a6833]">
              <IconWallet className="h-5 w-5" />
            </div>
            <div className="text-[11px] font-medium text-stone-500">Saldo Daytrade</div>
          </div>
          <div className="mt-3 text-2xl font-bold tracking-tight text-[#1c1917]">{saldo}</div>
          <div className="mt-1 text-[11px] text-stone-400">Caixa mais resultado realizado</div>
        </div>

        <div className="rounded-xl border border-[#e5e1d7] bg-white p-5 shadow-xs">
          <div className="flex items-center gap-3">
            <div className="flex h-9 w-9 items-center justify-center rounded-lg bg-[#f4efe4] text-[#8a6833]">
              <IconSparkles className="h-5 w-5" />
            </div>
            <div className="text-[11px] font-medium text-stone-500">Patrimônio</div>
          </div>
          <div className="mt-3 text-2xl font-bold tracking-tight text-[#0d6838]">{patrimonio}</div>
          <div className="mt-1 text-[11px] text-stone-400">{unrealized} não realizado</div>
        </div>

        <div className="rounded-xl border border-[#e5e1d7] bg-white p-5 shadow-xs">
          <div className="flex items-center gap-3">
            <div className="flex h-9 w-9 items-center justify-center rounded-lg bg-[#f4efe4] text-[#8a6833]">
              <IconBarChart className="h-5 w-5" />
            </div>
            <div className="text-[11px] font-medium text-stone-500">P&L realizado</div>
          </div>
          <div className={`mt-3 text-2xl font-bold tracking-tight ${realizedSign < 0 ? "text-[#be123c]" : "text-[#1c1917]"}`}>
            {pnlRealizado}
          </div>
          <div className="mt-1 text-[11px] text-stone-400">{tradeCount} operações fechadas</div>
        </div>

        <div className="rounded-xl border border-[#e5e1d7] bg-white p-5 shadow-xs">
          <div className="flex items-center gap-3">
            <div className="flex h-9 w-9 items-center justify-center rounded-lg bg-[#f4efe4] text-[#8a6833]">
              <IconBriefcase className="h-5 w-5" />
            </div>
            <div className="text-[11px] font-medium text-stone-500">Posições abertas</div>
          </div>
          <div className="mt-3 text-2xl font-bold tracking-tight text-[#1c1917]">{openPositions.length}</div>
          <div className="mt-1 text-[11px] text-stone-400">Daytrade em andamento</div>
        </div>
      </div>

      <div className="grid grid-cols-1 gap-6 lg:grid-cols-2">
        <div className="rounded-xl border border-[#e5e1d7] bg-white p-5 shadow-xs">
          <div className="flex items-center justify-between">
            <div>
              <h2 className="text-sm font-bold text-[#1c1917]">Posições abertas</h2>
              <p className="text-[11px] text-stone-400">Exposição atual da conta</p>
            </div>
            <span className="rounded-full border border-emerald-300 bg-emerald-50 px-2 py-0.5 text-[10px] font-semibold text-emerald-800">
              Sessão
            </span>
          </div>
          <div className="mt-4 divide-y divide-[#f0ece4]">
            {openPositions.length === 0 && (
              <p className="py-6 text-center text-xs text-stone-400">Nenhuma posição aberta.</p>
            )}
            {openPositions.map((position) => (
              <div key={position.ticker} className="flex items-center justify-between rounded-lg px-2 py-3">
                <div>
                  <div className="text-sm font-bold text-stone-900">{position.ticker}</div>
                  <div className="text-[11px] text-stone-500">
                    {signOf(position.quantity) < 0 ? "Venda" : "Compra"} · {groupDecimal(position.quantity)}
                  </div>
                </div>
                <div className="flex items-center gap-3">
                  <span className={`text-xs font-bold ${signOf(position.pnl) < 0 ? "text-[#be123c]" : "text-[#0d6838]"}`}>
                    {formatSignedBrl(position.pnl)}
                  </span>
                  <button
                    type="button"
                    onClick={() => {
                      setActiveTicker(position.ticker);
                      setActiveNav("operar");
                    }}
                    title="Abrir no gráfico"
                    className="rounded-md border border-[#e2ddd1] p-1.5 text-stone-600 transition hover:border-amber-400 hover:bg-amber-50"
                  >
                    <IconArrowUpRight className="h-4 w-4" />
                  </button>
                </div>
              </div>
            ))}
          </div>
        </div>

        <div className="rounded-xl border border-[#e5e1d7] bg-white p-5 shadow-xs">
          <div className="flex items-center justify-between">
            <div>
              <h2 className="text-sm font-bold text-[#1c1917]">Últimas movimentações</h2>
              <p className="text-[11px] text-stone-400">Fechamentos deste pregão</p>
            </div>
            <button
              type="button"
              onClick={() => setActiveNav("relatorios")}
              className="text-xs font-semibold text-[#8a6833] hover:underline"
            >
              Ver relatório
            </button>
          </div>
          <div className="mt-4 divide-y divide-[#f0ece4]">
            {recentTrades.length === 0 && (
              <p className="py-6 text-center text-xs text-stone-400">Nenhum trade fechado.</p>
            )}
            {recentTrades.map((trade) => {
              const positive = signOf(trade.pnl) >= 0;
              return (
                <div key={trade.id} className="flex items-center justify-between rounded-lg px-2 py-2.5">
                  <div>
                    <div className="text-xs font-bold text-stone-900">{trade.ticker}</div>
                    <div className="text-[10px] text-stone-500">
                      {formatDateTime(trade.timestamp)} · {trade.type === "sell" ? "Venda" : "Compra"}
                    </div>
                  </div>
                  <div className={`text-xs font-bold ${positive ? "text-[#0d6838]" : "text-[#be123c]"}`}>
                    {formatSignedBrl(trade.pnl)}
                  </div>
                </div>
              );
            })}
          </div>
        </div>
      </div>
    </div>
  );
}
