import type { OrderSide, OrderStatus, OrderType } from "./transport";

/**
 * Display helpers for the trading panel.
 *
 * These deliberately do no money arithmetic. Every value arrives as an exact decimal string and is
 * only grouped for reading, so the terminal can never show a number the backend did not compute.
 * Grouping uses a fixed pt-BR pattern rather than Intl on a float, which would reintroduce the binary
 * rounding the whole design avoids.
 */
export function groupDecimal(value: string): string {
  const negative = value.startsWith("-");
  const unsigned = negative ? value.slice(1) : value;
  const [whole, fraction] = unsigned.split(".");
  const grouped = whole.replace(/\B(?=(\d{3})+(?!\d))/g, ".");
  const decimals = fraction ? `,${fraction}` : "";
  return `${negative ? "-" : ""}${grouped}${decimals}`;
}

/** Trims insignificant trailing zeros so 99.830 shows as 99,83 but 99.835 stays intact. */
export function trimDecimal(value: string): string {
  if (!value.includes(".")) return value;
  const trimmed = value.replace(/0+$/, "").replace(/\.$/, "");
  return trimmed.length === 0 || trimmed === "-" ? "0" : trimmed;
}

export function formatMoney(value: string, currency = ""): string {
  const text = groupDecimal(trimDecimal(value));
  return currency ? `${text} ${currency}` : text;
}

export function formatSignedMoney(value: string, currency = ""): string {
  const negative = value.startsWith("-");
  const magnitude = negative ? value.slice(1) : value;
  const sign = negative ? "-" : "+";
  return `${sign}${formatMoney(magnitude, currency)}`;
}

/** Shows the clock part of an ISO instant, which is what an operator watches during a session. */
export function formatInstant(value: string | null): string {
  if (!value) return "—";
  const match = /T(\d{2}:\d{2}:\d{2})/.exec(value);
  return match ? match[1] : value;
}

export function formatDateTime(value: string | null): string {
  if (!value) return "—";
  const match = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}:\d{2}:\d{2})/.exec(value);
  return match ? `${match[3]}/${match[2]} ${match[4]}` : value;
}

export const sideLabels: Record<OrderSide, string> = {
  buy: "Compra",
  sell: "Venda",
};

export const typeLabels: Record<OrderType, string> = {
  market: "Mercado",
  limit: "Limite",
};

export const statusLabels: Record<OrderStatus, string> = {
  created: "Criada",
  accepted: "Aceita",
  partially_filled: "Parcial",
  filled: "Executada",
  cancelled: "Cancelada",
  rejected: "Rejeitada",
};

/** Statuses that still have a live presence and can therefore be cancelled. */
export const cancellableStatuses: readonly OrderStatus[] = ["created", "accepted", "partially_filled"];

export function isCancellable(status: OrderStatus): boolean {
  return cancellableStatuses.includes(status);
}

export function statusTone(status: OrderStatus): string {
  switch (status) {
    case "filled":
      return "border-emerald-400/30 bg-emerald-400/10 text-emerald-300";
    case "partially_filled":
      return "border-sky-400/30 bg-sky-400/10 text-sky-300";
    case "accepted":
      return "border-slate-500/30 bg-slate-500/10 text-slate-300";
    case "cancelled":
      return "border-slate-600/30 bg-slate-600/10 text-slate-400";
    case "rejected":
      return "border-rose-400/30 bg-rose-400/10 text-rose-300";
    default:
      return "border-slate-700/40 bg-slate-700/10 text-slate-400";
  }
}

export function toneForSignedValue(value: string): string {
  if (value.startsWith("-")) return "text-rose-300";
  if (/^0(\.0+)?$/.test(value)) return "text-slate-300";
  return "text-emerald-300";
}
