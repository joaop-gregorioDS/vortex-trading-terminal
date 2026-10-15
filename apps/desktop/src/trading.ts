export type Side = "buy" | "sell";
export type OrderKind = "market" | "limit" | "stop";
export type ProtectionUnit = "ticks" | "points" | "percent";

export type ProtectionDraft = {
  enabled: boolean;
  unit: ProtectionUnit;
  gain: string;
  loss: string;
  trail: string;
  breakEven: string;
};

export const idleProtection: ProtectionDraft = {
  enabled: false,
  unit: "points",
  gain: "",
  loss: "",
  trail: "",
  breakEven: "",
};

export type ProtectionBody = {
  gain: string;
  loss: string;
  unit: ProtectionUnit;
  trail: string | null;
  breakEven: string | null;
};

export type OrderBody = {
  instrumentId: string;
  side: Side;
  type: OrderKind;
  quantity: string;
  limitPrice: string | null;
  timeInForce: null;
  origin: "session";
  protection: ProtectionBody | null;
};

export type ChartLine = {
  id: string;
  price: string;
  title: string;
};

export function protectionBody(draft: ProtectionDraft): { body?: ProtectionBody; error?: string } {
  if (!draft.enabled) return {};
  const gain = draft.gain.trim();
  const loss = draft.loss.trim();
  if (!positive(gain) || !positive(loss)) {
    return { error: "Informe o ganho e a perda da proteção." };
  }
  const trail = draft.trail.trim();
  const breakEven = draft.breakEven.trim();
  if (trail && !positive(trail)) return { error: "A trilha da proteção precisa ser maior que zero." };
  if (breakEven && !positive(breakEven)) return { error: "O empate da proteção precisa ser maior que zero." };
  return {
    body: {
      gain,
      loss,
      unit: draft.unit,
      trail: trail || null,
      breakEven: breakEven || null,
    },
  };
}

export function sessionOrder(input: {
  instrumentId: string;
  side: Side;
  type: "market" | "limit";
  quantity: string;
  limitPrice: string;
  protection: ProtectionDraft;
}): { body?: OrderBody; error?: string } {
  const quantity = input.quantity.trim();
  if (!positive(quantity)) return { error: "A quantidade precisa ser maior que zero." };
  const protection = protectionBody(input.protection);
  if (protection.error) return { error: protection.error };
  const shared = {
    instrumentId: input.instrumentId,
    side: input.side,
    quantity,
    timeInForce: null,
    origin: "session" as const,
    protection: protection.body ?? null,
  };
  if (input.type === "market") {
    return { body: { ...shared, type: "market", limitPrice: null } };
  }
  const limitPrice = input.limitPrice.trim();
  if (!positive(limitPrice)) return { error: "O preço precisa ser maior que zero." };
  return { body: { ...shared, type: "limit", limitPrice } };
}

export function closingSide(quantity: string): Side | null {
  const negative = quantity.startsWith("-");
  const unsigned = negative ? quantity.slice(1) : quantity;
  if (!positive(unsigned)) return null;
  return negative ? "buy" : "sell";
}

export function stillPending(orderId: string | null, orders: readonly { orderId: string }[]): boolean {
  if (!orderId) return true;
  return !orders.some((order) => order.orderId === orderId);
}

export function lineTitle(type: string): string {
  if (type === "stop") return "Stop";
  if (type === "limit") return "Limite";
  if (type === "market") return "Mercado";
  return type;
}

export function priceScale(value: string): number {
  const fraction = value.split(".")[1];
  return fraction ? Math.min(fraction.length, 8) : 2;
}

/** Coordenada do gráfico. O servidor devolve o preço e o tipo. */
export function coordinateDraft(value: number, scale: number): string | null {
  if (!Number.isFinite(value) || value <= 0) return null;
  const places = Math.min(8, Math.max(2, scale));
  return value.toFixed(places);
}

export function resting(status: string): boolean {
  return status === "created" || status === "accepted" || status === "partially_filled";
}

function positive(value: string): boolean {
  return /^\d+(?:\.\d+)?$/.test(value) && !/^0+(?:\.0+)?$/.test(value);
}
