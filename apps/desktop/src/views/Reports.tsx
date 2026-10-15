import { useEffect, useRef, useState } from "react";
import type { ClosedTrade } from "../bindings/ClosedTrade";
import type { MarketQuote } from "../bindings/MarketQuote";
import type { PerformanceReport } from "../bindings/PerformanceReport";
import { formatDateTime, formatMoney, formatPercentRatio, formatSignedMoney } from "../format";
import { exportTrades, loadPerformance, loadTrades, readError } from "../ipc/client";
import { equityLine, tradeQuery } from "../reports";

function textMoney(value: string): string {
  return value ? formatMoney(value) : "—";
}

function textSigned(value: string): string {
  return value ? formatSignedMoney(value) : "—";
}

function sideLabel(type: string): string {
  if (type === "buy") return "Compra";
  if (type === "sell") return "Venda";
  return type;
}

export function Reports({
  quotes,
  exportRequest,
  exportSeen,
}: {
  quotes: MarketQuote[] | null;
  exportRequest: number;
  exportSeen: { current: number };
}) {
  const catalog = quotes?.map((quote) => quote.ticker) ?? [];
  const catalogKey = catalog.join("|");
  const [filter, setFilter] = useState("todos");
  const [report, setReport] = useState<PerformanceReport | null>(null);
  const [trades, setTrades] = useState<ClosedTrade[] | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [refresh, setRefresh] = useState(0);

  useEffect(() => {
    const names = catalogKey ? catalogKey.split("|") : [];
    if (filter !== "todos" && !names.includes(filter)) setFilter("todos");
  }, [catalogKey, filter]);

  useEffect(() => {
    let cancelled = false;
    void loadPerformance()
      .then((body) => {
        if (!cancelled) setReport(body);
      })
      .catch((error: unknown) => {
        if (!cancelled) setNotice(readError(error));
      });
    return () => {
      cancelled = true;
    };
  }, [refresh]);

  useEffect(() => {
    let cancelled = false;
    setTrades(null);
    const ticker = tradeQuery(filter, catalog);
    void loadTrades(ticker)
      .then((body) => {
        if (!cancelled) setTrades(body);
      })
      .catch((error: unknown) => {
        if (!cancelled) setNotice(readError(error));
      });
    return () => {
      cancelled = true;
    };
  }, [filter, refresh, catalogKey]);

  const line = report ? equityLine(report.equityCurve.map((point) => point.equity)) : null;

  async function exportCsv() {
    if (busy) return;
    setBusy(true);
    try {
      const saved = await exportTrades(tradeQuery(filter, catalog));
      setNotice(saved.rowCount === 1 ? "CSV gravado. 1 negócio." : `CSV gravado. ${saved.rowCount} negócios.`);
    } catch (error) {
      setNotice(readError(error));
    } finally {
      setBusy(false);
    }
  }

  const exportCsvRef = useRef(exportCsv);
  exportCsvRef.current = exportCsv;

  useEffect(() => {
    if (exportRequest === 0 || exportSeen.current === exportRequest) return;
    exportSeen.current = exportRequest;
    void exportCsvRef.current();
  }, [exportRequest, exportSeen]);

  return (
    <main className="workspace single">
      <div className="overview-head">
        <p className="brand">Relatórios</p>
        <div className="modes" role="group" aria-label="Filtro">
          <button type="button" className={filter === "todos" ? "selected" : ""} onClick={() => setFilter("todos")}>
            Todos
          </button>
          {catalog.map((ticker) => (
            <button
              key={ticker}
              type="button"
              className={filter === ticker ? "selected" : ""}
              onClick={() => setFilter(ticker)}
            >
              {ticker}
            </button>
          ))}
        </div>
        <button type="button" onClick={() => setRefresh((value) => value + 1)}>
          Atualizar relatório
        </button>
        <button type="button" disabled={busy} onClick={() => void exportCsv()}>
          Exportar CSV
        </button>
      </div>
      {notice ? <p className="ticket-note">{notice}</p> : null}
      {report ? (
        <>
          <div className="kpis">
            <p className="kpi"><span>Saldo inicial</span>{textMoney(report.startingBalance)}</p>
            <p className="kpi"><span>Saldo atual</span>{textMoney(report.currentBalance)}</p>
            <p className="kpi"><span>Patrimônio</span>{textMoney(report.equity)}</p>
            <p className="kpi"><span>P&L realizado</span>{textSigned(report.realizedPnl)}</p>
            <p className="kpi"><span>P&L do dia</span>{textSigned(report.dailyPnl)}</p>
            <p className="kpi"><span>Taxa de acerto</span>{formatPercentRatio(report.winRate)}</p>
            <p className="kpi"><span>Vitórias</span>{report.winsCount}</p>
            <p className="kpi"><span>Derrotas</span>{report.lossesCount}</p>
            <p className="kpi"><span>Negócios</span>{report.totalTrades}</p>
            <p className="kpi"><span>Fator de lucro</span>{textMoney(report.profitFactor)}</p>
            <p className="kpi"><span>Payoff</span>{textMoney(report.payoff)}</p>
            <p className="kpi"><span>Drawdown máximo</span>{textMoney(report.maxDrawdown)}</p>
            <p className="kpi">
              <span>Melhor</span>
              {report.bestTrade ? `${report.bestTrade.ticker} ${textSigned(report.bestTrade.pnl)}` : "—"}
            </p>
            <p className="kpi">
              <span>Pior</span>
              {report.worstTrade ? `${report.worstTrade.ticker} ${textSigned(report.worstTrade.pnl)}` : "—"}
            </p>
          </div>
          <p className="brand">Curva de equity</p>
          {line ? (
            <svg className="curve" viewBox="0 0 100 100" preserveAspectRatio="none" aria-hidden="true">
              <polyline points={line} />
            </svg>
          ) : null}
          {report.equityCurve.length === 0 ? <p className="muted">Sem pontos de equity.</p> : null}
          {report.equityCurve.length > 0 ? (
            <table className="catalog plain">
              <thead>
                <tr>
                  <th>Hora</th>
                  <th>Patrimônio</th>
                  <th>Realizado</th>
                </tr>
              </thead>
              <tbody>
                {report.equityCurve.map((point) => (
                  <tr key={`${point.time}-${point.equity}`}>
                    <td>{formatDateTime(point.time)}</td>
                    <td>{textMoney(point.equity)}</td>
                    <td>{textSigned(point.realizedPnl)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          ) : null}
        </>
      ) : notice ? null : (
        <p className="muted">Lendo o relatório…</p>
      )}
      <p className="brand">Negócios fechados</p>
      {trades && trades.length === 0 ? <p className="muted">Sem negócios fechados.</p> : null}
      {trades && trades.length > 0 ? (
        <table className="catalog plain">
          <thead>
            <tr>
              <th>Hora</th>
              <th>Ativo</th>
              <th>Mercado</th>
              <th>Lado</th>
              <th>Quantidade</th>
              <th>Entrada</th>
              <th>Saída</th>
              <th>P&L</th>
            </tr>
          </thead>
          <tbody>
            {trades.map((trade) => (
              <tr key={trade.id}>
                <td>{formatDateTime(trade.timestamp)}</td>
                <td>{trade.ticker}</td>
                <td>{trade.market}</td>
                <td>{sideLabel(trade.type)}</td>
                <td>{textMoney(trade.quantity)}</td>
                <td>{textMoney(trade.entryPrice)}</td>
                <td>{textMoney(trade.exitPrice)}</td>
                <td>{textSigned(trade.pnl)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      ) : null}
      {trades === null && !notice ? <p className="muted">Lendo negócios…</p> : null}
    </main>
  );
}
