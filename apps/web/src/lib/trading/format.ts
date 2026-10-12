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
  stop: "Stop",
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

/** Groups an exact money string and prefixes the real sign. An empty value stays blank. */
export function formatBrl(value: string | null | undefined): string {
  if (!value) return "—";
  return `R$ ${formatMoney(value)}`;
}

export function formatSignedBrl(value: string | null | undefined): string {
  if (!value) return "—";
  const negative = value.startsWith("-");
  const magnitude = negative ? value.slice(1) : value;
  if (isZeroDecimal(magnitude)) return `R$ ${formatMoney(magnitude)}`;
  return `${negative ? "-" : "+"}R$ ${formatMoney(magnitude)}`;
}

/** -1 when the decimal string is negative, 0 when it is blank or zero, 1 otherwise. */
export function signOf(value: string | null | undefined): -1 | 0 | 1 {
  if (!value || isZeroDecimal(value.startsWith("-") ? value.slice(1) : value)) return 0;
  return value.startsWith("-") ? -1 : 1;
}

/**
 * Shows a ratio as a percent by moving the decimal point two places, then keeping two fractional
 * digits. 0.125 becomes 12,5% and a long session ratio becomes 0,02%. Money strings are not rounded.
 */
export function formatRatioPercent(ratio: string | null | undefined): string {
  if (!ratio) return "—";
  const shifted = shiftDecimalPoint(ratio, 2);
  return `${groupDecimal(trimDecimal(roundDecimal(shifted, 2)))}%`;
}

/**
 * Groups a chart coordinate the same way a quote groups an API decimal.
 * The coordinate is already a drawing number; this only chooses the glyphs.
 */
export function formatChartPrice(value: number, precision: number): string {
  if (!Number.isFinite(value)) return "—";
  const places = precision > 0 ? precision : 0;
  const fixed = Math.abs(value).toFixed(places);
  return groupDecimal(value < 0 ? `-${fixed}` : fixed);
}

/** UTC unix seconds on the Brasília clock. The stored candle time is not shifted. */
export function brasiliaFromUnix(unixSeconds: number): { time: string; date: string } {
  if (!Number.isFinite(unixSeconds)) return { time: "—", date: "—" };
  return formatBrasilia(new Date(unixSeconds * 1000).toISOString());
}

/** Crosshair label, Brasília, minute resolution. */
export function formatChartCrosshair(unixSeconds: number): string {
  const clock = brasiliaFromUnix(unixSeconds);
  if (clock.time === "—") return "—";
  return `${clock.date} ${clock.time.slice(0, 5)}`;
}

/**
 * Time-scale tick. `mark` follows lightweight-charts TickMarkType:
 * 0 year, 1 month, 2 day, 3 time, 4 time with seconds. Labels stay within 8 characters.
 */
export function formatChartTick(unixSeconds: number, mark: number): string {
  const clock = brasiliaFromUnix(unixSeconds);
  if (clock.time === "—") return "—";
  if (mark === 0) return clock.date.slice(6);
  if (mark === 1) return `${clock.date.slice(3, 5)}/${clock.date.slice(6)}`;
  if (mark === 2) return clock.date.slice(0, 5);
  if (mark === 4) return clock.time;
  return clock.time.slice(0, 5);
}

/** Simulated clock in Brasília, which is UTC−3. This only rewrites the clock, never a price. */
export function formatBrasilia(iso: string | null | undefined): { time: string; date: string } {
  if (!iso) return { time: "—", date: "—" };
  const match = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2}):(\d{2})/.exec(iso);
  if (!match) return { time: "—", date: "—" };

  let year = Number(match[1]);
  let month = Number(match[2]);
  let day = Number(match[3]);
  let hour = Number(match[4]) - 3;
  if (hour < 0) {
    hour += 24;
    day -= 1;
    if (day < 1) {
      month -= 1;
      if (month < 1) {
        month = 12;
        year -= 1;
      }
      day = daysInMonth(year, month);
    }
  }

  return {
    time: `${pad(hour)}:${match[5]}:${match[6]}`,
    date: `${pad(day)}/${pad(month)}/${year}`,
  };
}

function isZeroDecimal(value: string): boolean {
  return /^0*(?:\.0*)?$/.test(value);
}

function pad(value: number): string {
  return value < 10 ? `0${value}` : String(value);
}

function daysInMonth(year: number, month: number): number {
  return new Date(Date.UTC(year, month, 0)).getUTCDate();
}

/** Rounds an exact decimal string half away from zero. Used for percent display, never for money. */
function roundDecimal(value: string, places: number): string {
  const negative = value.startsWith("-");
  const unsigned = negative ? value.slice(1) : value;
  const [wholeRaw, fraction = ""] = unsigned.split(".");
  const whole = wholeRaw.replace(/^0+/, "") || "0";
  if (fraction.length <= places) {
    const text = places === 0 ? whole : `${whole}.${fraction.padEnd(places, "0")}`;
    return negative && !isZeroDecimal(text) ? `-${text}` : text;
  }

  const keep = fraction.slice(0, places);
  if (fraction[places] < "5") {
    const text = places === 0 ? whole : `${whole}.${keep}`;
    return negative && !isZeroDecimal(text) ? `-${text}` : text;
  }

  const incremented = incrementDigits(`${whole}${keep}`.replace(/^0+/, "") || "0");
  let text: string;
  if (places === 0) {
    text = incremented;
  } else if (incremented.length <= places) {
    text = `0.${incremented.padStart(places, "0")}`;
  } else {
    const split = incremented.length - places;
    text = `${incremented.slice(0, split)}.${incremented.slice(split)}`;
  }
  return negative && !isZeroDecimal(text) ? `-${text}` : text;
}

function incrementDigits(digits: string): string {
  const chars = digits.split("");
  let carry = 1;
  for (let index = chars.length - 1; index >= 0 && carry === 1; index -= 1) {
    const sum = chars[index].charCodeAt(0) - 48 + carry;
    chars[index] = String(sum % 10);
    carry = sum >= 10 ? 1 : 0;
  }
  return carry === 1 ? `1${chars.join("")}` : chars.join("");
}

/** Moves the decimal point of an exact decimal string. places may be negative. */
function shiftDecimalPoint(value: string, places: number): string {
  const negative = value.startsWith("-");
  const unsigned = negative ? value.slice(1) : value;
  const [wholeRaw, fraction = ""] = unsigned.split(".");
  const whole = wholeRaw.replace(/^0+/, "") || "0";
  const digits = `${whole}${fraction}`;
  const point = whole.length + places;
  let shifted: string;
  if (point <= 0) {
    shifted = `0.${"0".repeat(-point)}${digits}`;
  } else if (point >= digits.length) {
    shifted = `${digits}${"0".repeat(point - digits.length)}`;
  } else {
    shifted = `${digits.slice(0, point)}.${digits.slice(point)}`;
  }

  const [integerRaw, fractionRaw] = shifted.split(".");
  const integer = integerRaw.replace(/^0+/, "") || "0";
  shifted = fractionRaw === undefined ? integer : `${integer}.${fractionRaw}`.replace(/(\.\d*?)0+$/, "$1").replace(/\.$/, "");
  return negative && !isZeroDecimal(shifted) ? `-${shifted}` : shifted;
}
