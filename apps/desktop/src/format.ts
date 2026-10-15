/**
 * Leitura de decimais. A conta continua no servidor.
 * A regra é a de `apps/web/src/lib/trading/format.ts`: agrupa o texto, sem `Number`.
 */
export function groupDecimal(value: string): string {
  const negative = value.startsWith("-");
  const unsigned = negative ? value.slice(1) : value;
  const [whole, fraction] = unsigned.split(".");
  const grouped = whole.replace(/\B(?=(\d{3})+(?!\d))/g, ".");
  const decimals = fraction ? `,${fraction}` : "";
  return `${negative ? "-" : ""}${grouped}${decimals}`;
}

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

/** changePercent chega como razão. 0.12 é 12 %. O ponto anda no texto, sem Number. */
export function formatPercentRatio(value: string): string {
  if (!/^-?\d+(?:\.\d+)?$/.test(value)) return "—";
  return `${groupDecimal(shiftDecimalPoint(value, 2))} %`;
}

function shiftDecimalPoint(value: string, places: number): string {
  const negative = value.startsWith("-");
  const unsigned = negative ? value.slice(1) : value;
  const [wholeRaw, fraction = ""] = unsigned.split(".");
  const whole = wholeRaw.replace(/^0+/, "") || "0";
  const digits = `${whole}${fraction}`;
  const point = whole.length + places;
  let shifted: string;
  if (point <= 0) shifted = `0.${"0".repeat(-point)}${digits}`;
  else if (point >= digits.length) shifted = `${digits}${"0".repeat(point - digits.length)}`;
  else shifted = `${digits.slice(0, point)}.${digits.slice(point)}`;
  const [integerRaw, fractionRaw] = shifted.split(".");
  const integer = integerRaw.replace(/^0+/, "") || "0";
  shifted = fractionRaw === undefined ? integer : `${integer}.${fractionRaw}`.replace(/(\.\d*?)0+$/, "$1").replace(/\.$/, "");
  if (shifted === "0") return "0";
  return negative ? `-${shifted}` : shifted;
}

export function formatDateTime(value: string | null): string {
  if (!value) return "—";
  const match = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}:\d{2}:\d{2})/.exec(value);
  return match ? `${match[3]}/${match[2]} ${match[4]}` : value;
}
