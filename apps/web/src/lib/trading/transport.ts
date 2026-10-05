/**
 * Transport shapes for the paper trading API.
 *
 * Every exact financial value arrives as a decimal string, because a JSON number would be parsed as a
 * binary floating point value and would stop being authoritative for money. The client keeps those
 * strings intact for display and only compares them as strings; it never does arithmetic on money.
 */

export type OrderSide = "buy" | "sell";
export type OrderType = "market" | "limit";
export type OrderStatus =
  | "created"
  | "accepted"
  | "partially_filled"
  | "filled"
  | "cancelled"
  | "rejected";

export type FillTransport = {
  executionId: string;
  quantity: string;
  price: string;
  executedAt: string;
};

export type OrderTransport = {
  orderId: string;
  instrumentId: string;
  side: OrderSide;
  type: OrderType;
  quantity: string;
  limitPrice: string | null;
  timeInForce: string;
  status: OrderStatus;
  filledQuantity: string;
  remainingQuantity: string;
  rejectionReason: string | null;
  createdAt: string;
  lastUpdatedAt: string;
  completedAt: string | null;
  fills: FillTransport[];
};

export type PositionTransport = {
  instrumentId: string;
  quantity: string;
  totalCost: string;
  averageCost: string;
  realizedProfitAndLoss: string;
  unrealizedProfitAndLoss: string;
  totalProfitAndLoss: string;
  isClosed: boolean;
  updatedAt: string | null;
};

export type PortfolioTransport = {
  cash: string;
  equity: string;
  totalCost: string;
  realizedProfitAndLoss: string;
  unrealizedProfitAndLoss: string;
  totalProfitAndLoss: string;
  positions: PositionTransport[];
};

export type MarketTransport = {
  instrumentId: string;
  lastPrice: string | null;
  simulatedTime: string | null;
  restingOrderCount: number;
};

export type NewOrderRequest = {
  instrumentId: string;
  side: OrderSide;
  type: OrderType;
  quantity: string;
  limitPrice?: string;
};

/**
 * Reads the API under the same origin. Returns a discriminated result instead of throwing so the UI
 * can show a message the operator can act on rather than a blank panel.
 */
export type ApiResult<T> = { ok: true; value: T } | { ok: false; error: string };

async function request<T>(path: string, init?: RequestInit): Promise<ApiResult<T>> {
  try {
    const response = await fetch(path, {
      ...init,
      headers: { Accept: "application/json", ...(init?.headers ?? {}) },
      cache: "no-store",
      signal: AbortSignal.timeout(5000),
    });

    if (!response.ok) {
      return { ok: false, error: await describeFailure(response) };
    }

    return { ok: true, value: (await response.json()) as T };
  } catch (error) {
    // Network failures and timeouts land here. The operator sees that the terminal is unreachable,
    // which is different from the API refusing the request.
    return {
      ok: false,
      error:
        error instanceof DOMException && error.name === "TimeoutError"
          ? "A API não respondeu a tempo."
          : "Não foi possível alcançar a API.",
    };
  }
}

async function describeFailure(response: Response): Promise<string> {
  try {
    const problem = (await response.json()) as { title?: string; code?: string };
    if (typeof problem.title === "string" && problem.title.length > 0) return problem.title;
    if (typeof problem.code === "string" && problem.code.length > 0) return problem.code;
  } catch {
    // A failure body that is not JSON tells the operator nothing useful, so fall through.
  }

  return `A API recusou a operação (${response.status}).`;
}

export const fetchMarket = () => request<MarketTransport>("/api/trading/market");

export const fetchOrders = () => request<OrderTransport[]>("/api/orders");

export const fetchPortfolio = () => request<PortfolioTransport>("/api/portfolio");

export const submitOrder = (body: NewOrderRequest) =>
  request<OrderTransport>("/api/orders", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });

export const cancelOrder = (orderId: string) =>
  request<OrderTransport>(`/api/orders/${encodeURIComponent(orderId)}`, { method: "POST" });
