"use client";

import { useMemo } from "react";
import { useTerminal } from "@/lib/trading/terminal-context";
import {
  formatBrl,
  formatDateTime,
  formatRatioPercent,
  formatSignedBrl,
  groupDecimal,
  signOf,
} from "@/lib/trading/format";
import type { ClosedTradeTransport } from "@/lib/trading/transport";

export function RelatoriosView() {
  const { performance, closedTrades, filterTicker, setFilterTicker, marketQuotes, currentDateStr } = useTerminal();

  const filteredTrades = useMemo(() => {
    if (filterTicker === "all") return closedTrades;
    return closedTrades.filter((trade) => trade.ticker === filterTicker);
  }, [closedTrades, filterTicker]);

  const handleExportCsv = () => {
    const headers = "Data/Hora,Ativo,Mercado,Tipo,Quantidade,Entrada,Saida,PnL\n";
    const rows = filteredTrades
      .map(
        (trade) =>
          `"${trade.timestamp}","${trade.ticker}","${trade.market}","${trade.type}","${trade.quantity}","${trade.entryPrice}","${trade.exitPrice}","${trade.pnl}"`,
      )
      .join("\n");
    const blob = new Blob(["\uFEFF" + headers + rows], { type: "text/csv;charset=utf-8;" });
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = url;
    link.download = `relatorio-daytrade-${filterTicker}.csv`;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
    URL.revokeObjectURL(url);
  };

  const ratioOrDash = (value: string | undefined) => (value ? groupDecimal(value) : "—");

  return (
    <div className="mx-auto max-w-7xl space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-4">
        <div>
          <div className="text-[11px] font-bold tracking-widest text-[#8a6833] uppercase">
            INVESTIMENTOS PRO / RELATÓRIOS
          </div>
          <h1 className="mt-1 text-2xl font-bold tracking-tight text-[#1c1917] sm:text-3xl">Desempenho do Daytrade</h1>
          <p className="mt-1 text-xs text-stone-500">
            Conta compartilhada · fechamentos calculados dos fills · pregão {currentDateStr}
          </p>
        </div>
        <button
          type="button"
          onClick={handleExportCsv}
          className="flex items-center gap-2 rounded-lg bg-[#8a6833] px-4 py-2.5 text-xs font-semibold text-white shadow-xs transition hover:bg-[#785929]"
        >
          <span>Exportar CSV</span>
        </button>
      </div>

      <div className="flex flex-wrap items-center gap-3 text-xs">
        <select
          value={filterTicker}
          onChange={(event) => setFilterTicker(event.target.value)}
          className="rounded-lg border border-[#ded8cb] bg-white px-3 py-1.5 font-semibold text-stone-800 focus:border-amber-500 focus:outline-hidden"
        >
          <option value="all">Todos os ativos</option>
          {marketQuotes.map((quote) => (
            <option key={quote.instrumentId} value={quote.ticker}>
              {quote.ticker}
            </option>
          ))}
        </select>
      </div>

      <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-6">
        <Kpi label="Saldo atual" value={performance ? formatBrl(performance.currentBalance) : "—"} />
        <Kpi label="P&L do dia" value={performance ? formatSignedBrl(performance.dailyPnl) : "—"} tone={signOf(performance?.dailyPnl)} hint={currentDateStr} />
        <Kpi label="Lucro líquido realizado" value={performance ? formatSignedBrl(performance.realizedPnl) : "—"} tone={signOf(performance?.realizedPnl)} />
        <Kpi
          label="Win rate"
          value={performance ? formatRatioPercent(performance.winRate) : "—"}
          hint={performance ? `${performance.winsCount}V / ${performance.lossesCount}D` : "—"}
        />
        <Kpi label="Profit factor" value={ratioOrDash(performance?.profitFactor)} />
        <Kpi label="Payoff" value={ratioOrDash(performance?.payoff)} />
        <Kpi
          label="Melhor trade"
          value={performance?.bestTrade ? formatSignedBrl(performance.bestTrade.pnl) : "—"}
          tone={1}
          hint={performance?.bestTrade?.ticker ?? "—"}
        />
        <Kpi
          label="Pior trade"
          value={performance?.worstTrade ? formatSignedBrl(performance.worstTrade.pnl) : "—"}
          tone={-1}
          hint={performance?.worstTrade?.ticker ?? "—"}
        />
        <Kpi label="Trades" value={performance ? String(performance.totalTrades) : "—"} />
        <Kpi label="Drawdown máximo" value={performance ? formatBrl(performance.maxDrawdown) : "—"} tone={-1} />
      </div>

      <div className="rounded-xl border border-[#e5e1d7] bg-white p-5 shadow-xs">
        <div className="border-b border-[#eeebe2] pb-3">
          <h2 className="text-sm font-bold text-[#1c1917]">Curva de equity</h2>
          <p className="text-[11px] text-stone-400">Patrimônio realizado após cada fechamento</p>
        </div>
        <EquityCurve points={performance?.equityCurve ?? []} />
      </div>

      <div className="rounded-xl border border-[#e5e1d7] bg-white p-5 shadow-xs">
        <h2 className="mb-3 text-sm font-bold text-[#1c1917]">Histórico de operações</h2>
        <div className="overflow-x-auto">
          <table className="w-full text-left text-xs">
            <thead className="border-b border-[#eeebe2] bg-[#fdfcf9] text-[10px] font-semibold tracking-wider text-stone-500 uppercase">
              <tr>
                <th className="px-4 py-2.5">Data/Hora</th>
                <th className="px-4 py-2.5">Ativo</th>
                <th className="px-4 py-2.5">Mercado</th>
                <th className="px-4 py-2.5">Tipo</th>
                <th className="px-4 py-2.5 text-center">Qtd. fechada</th>
                <th className="px-4 py-2.5 text-right">Entrada</th>
                <th className="px-4 py-2.5 text-right">Saída</th>
                <th className="px-4 py-2.5 text-right">P&L</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-[#f2ede4]">
              {filteredTrades.length === 0 && (
                <tr>
                  <td colSpan={8} className="px-4 py-8 text-center text-stone-400">
                    Nenhum trade fechado neste pregão.
                  </td>
                </tr>
              )}
              {filteredTrades.map((trade) => (
                <TradeRow key={trade.id} trade={trade} />
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}

function Kpi({
  label,
  value,
  hint,
  tone = 0,
}: {
  label: string;
  value: string;
  hint?: string;
  tone?: -1 | 0 | 1;
}) {
  const color = tone < 0 ? "text-[#be123c]" : tone > 0 ? "text-[#0d6838]" : "text-stone-900";
  return (
    <div className="rounded-xl border border-[#e5e1d7] bg-white p-4 shadow-2xs">
      <div className="text-[10px] font-semibold text-stone-500 uppercase">{label}</div>
      <div className={`mt-2 text-lg font-bold ${color}`}>{value}</div>
      {hint && <div className="text-[9px] text-stone-400">{hint}</div>}
    </div>
  );
}

function TradeRow({ trade }: { trade: ClosedTradeTransport }) {
  const pnlSign = signOf(trade.pnl);
  return (
    <tr className="transition hover:bg-[#faf8f4]">
      <td className="px-4 py-2 font-mono text-stone-600">{formatDateTime(trade.timestamp)}</td>
      <td className="px-4 py-2 font-bold text-stone-900">{trade.ticker}</td>
      <td className="px-4 py-2 text-[11px] font-medium text-stone-500">{trade.market}</td>
      <td className="px-4 py-2 font-semibold uppercase">{trade.type}</td>
      <td className="px-4 py-2 text-center font-mono">{groupDecimal(trade.quantity)}</td>
      <td className="px-4 py-2 text-right font-mono text-stone-700">{groupDecimal(trade.entryPrice)}</td>
      <td className="px-4 py-2 text-right font-mono text-stone-700">{groupDecimal(trade.exitPrice)}</td>
      <td
        className={`px-4 py-2 text-right font-mono font-bold ${
          pnlSign > 0 ? "text-[#0d6838]" : pnlSign < 0 ? "text-[#be123c]" : "text-stone-600"
        }`}
      >
        {formatSignedBrl(trade.pnl)}
      </td>
    </tr>
  );
}

function EquityCurve({ points }: { points: Array<{ time: string; equity: string; realizedPnl: string }> }) {
  if (points.length === 0) {
    return <p className="py-10 text-center text-xs text-stone-400">Nenhum fechamento neste pregão.</p>;
  }

  const width = 800;
  const height = 180;
  const numbers = points.map((point) => Number(point.equity));
  const min = Math.min(...numbers);
  const max = Math.max(...numbers);
  const span = max - min || 1;
  const coords = points.map((point, index) => {
    const x = points.length === 1 ? width / 2 : (index / (points.length - 1)) * (width - 40) + 20;
    const y = 20 + (1 - (Number(point.equity) - min) / span) * (height - 40);
    return { x, y, equity: point.equity, time: point.time };
  });
  const line = coords.map((point) => `${point.x},${point.y}`).join(" ");
  const area = `${line} ${coords[coords.length - 1].x},${height - 8} ${coords[0].x},${height - 8}`;
  const falling = signOf(points[points.length - 1].realizedPnl) < 0;
  const stroke = falling ? "#be123c" : "#0d6838";

  return (
    <div className="mt-4">
      <svg className="h-48 w-full" viewBox={`0 0 ${width} ${height}`} role="img" aria-label="Curva de equity">
        <polyline fill="none" stroke={stroke} strokeWidth="2.5" points={line} />
        <polygon points={area} fill={stroke} opacity="0.08" />
        {coords.map((point) => (
          <circle key={point.time} cx={point.x} cy={point.y} r="4" fill="white" stroke={stroke} strokeWidth="2">
            <title>{`${formatDateTime(point.time)} · ${formatBrl(point.equity)}`}</title>
          </circle>
        ))}
      </svg>
      <p className="text-right text-[10px] text-stone-400">Último patrimônio realizado: {formatBrl(points[points.length - 1].equity)}</p>
    </div>
  );
}
