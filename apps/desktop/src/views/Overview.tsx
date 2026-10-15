import type { MarketQuote } from "../bindings/MarketQuote";
import { formatMoney, formatPercentRatio, formatSignedMoney } from "../format";

function textMoney(value: string): string {
  return value ? formatMoney(value) : "—";
}

function textSigned(value: string): string {
  return value ? formatSignedMoney(value) : "—";
}

export function Overview({
  quotes,
  ticker,
  instrumentId,
  onTicker,
}: {
  quotes: MarketQuote[] | null;
  ticker: string | null;
  instrumentId: string | null;
  onTicker: (ticker: string) => void;
}) {
  return (
    <main className="workspace single">
      <div className="overview-head">
        <p className="brand">Visão Geral</p>
        <p className="instrument">{instrumentId ? `Instrumento ${instrumentId}` : "Instrumento —"}</p>
      </div>
      {quotes === null ? <p className="muted">Lendo o pregão…</p> : null}
      {quotes && quotes.length === 0 ? <p className="muted">Sem ativos no pregão.</p> : null}
      {quotes && quotes.length > 0 ? (
        <table className="catalog">
          <thead>
            <tr>
              <th>Ativo</th>
              <th>Nome</th>
              <th>Último</th>
              <th>Pontos</th>
              <th>Variação</th>
              <th>Volume</th>
            </tr>
          </thead>
          <tbody>
            {quotes.map((quote) => (
              <tr key={quote.ticker} className={quote.ticker === ticker ? "selected" : ""}>
                <td>
                  <button type="button" onClick={() => onTicker(quote.ticker)}>
                    {quote.ticker}
                  </button>
                </td>
                <td>{quote.name}</td>
                <td>{textMoney(quote.lastPrice)}</td>
                <td>{textSigned(quote.changePoints)}</td>
                <td>{formatPercentRatio(quote.changePercent)}</td>
                <td>{textMoney(quote.volume)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      ) : null}
    </main>
  );
}
