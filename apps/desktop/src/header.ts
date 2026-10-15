import { formatDateTime, formatMoney, formatSignedMoney } from "./format";

export type HeaderLink = "idle" | "connected" | "degraded" | "disconnected";

export function sessionLabel(open: boolean | null | undefined): string {
  if (open === true) return "Pregão aberto";
  if (open === false) return "Pregão fechado";
  return "Aguardando pregão";
}

export function connectionLabel(state: HeaderLink): string {
  switch (state) {
    case "connected":
      return "Conectado";
    case "degraded":
      return "Instável";
    case "disconnected":
      return "Sem conexão";
    default:
      return "Conectando";
  }
}

export function headerModel(input: {
  cash?: string;
  equity?: string;
  pnl?: string;
  state: HeaderLink;
  lastSuccessAt: string | null;
}): { cash: string; equity: string; pnl: string; stamp: string } {
  const stamp = input.lastSuccessAt ? formatDateTime(input.lastSuccessAt) : "Sem leitura";
  const cash = input.cash;
  const equity = input.equity;
  const pnl = input.pnl;
  const hidden = input.state === "disconnected" || input.state === "idle";
  if (hidden || cash === undefined || equity === undefined || pnl === undefined) {
    return { cash: "—", equity: "—", pnl: "—", stamp };
  }
  return {
    cash: formatMoney(cash),
    equity: formatMoney(equity),
    pnl: formatSignedMoney(pnl),
    stamp,
  };
}

export function pickTicker(current: string | null, quotes: readonly { ticker: string }[]): string | null {
  if (current && quotes.some((quote) => quote.ticker === current)) return current;
  return quotes.find((quote) => quote.ticker === "PETR4")?.ticker ?? quotes[0]?.ticker ?? null;
}

export function instrumentFor(
  ticker: string | null,
  quotes: readonly { ticker: string; instrumentId: string }[],
): string | null {
  if (!ticker) return null;
  return quotes.find((quote) => quote.ticker === ticker)?.instrumentId ?? null;
}

export function catalogTickers(quotes: readonly { ticker: string }[]): string[] {
  return quotes.map((quote) => quote.ticker);
}
