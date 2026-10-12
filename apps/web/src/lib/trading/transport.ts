/**
 * Transport shapes for the paper trading API.
 *
 * Every exact financial value arrives as a decimal string, because a JSON number would be parsed as a
 * binary floating point value and would stop being authoritative for money. The client keeps those
 * strings intact for display and only compares them as strings; it never does arithmetic on money.
 */

export type OrderSide = "buy" | "sell";
export type OrderType = "market" | "limit" | "stop";
export type OrderOrigin = "session" | "chart";
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
  origin: OrderOrigin;
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
  /** "entry", "target" or "stop" when the order belongs to a bracket. */
  role?: "entry" | "target" | "stop" | null;
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
  /** Absent keeps the simulated ticket. "chart" rests on a later chart close. */
  origin?: OrderOrigin;
  /** Bracket armed by the server after this order fills. Omit when the strategy is off. */
  protection?: {
    gain: string;
    loss: string;
    unit: "ticks" | "points" | "percent";
    trail?: string;
    breakEven?: string;
  };
};

export type PositionActionTransport = {
  status: string;
  order?: OrderTransport;
};

export type ChartPreviewTransport = {
  ticker: string;
  side: OrderSide;
  type: "limit" | "stop";
  price: string;
  referenceClose: string;
  notional: string;
};

/**
 * Reads the API under the same origin. Returns a discriminated result instead of throwing so the UI
 * can show a message the operator can act on rather than a blank panel.
 */
export type ApiResult<T> = { ok: true; value: T } | { ok: false; error: string };

async function request<T>(path: string, init?: RequestInit, timeoutMs = 5000): Promise<ApiResult<T>> {
  try {
    const response = await fetch(path, {
      ...init,
      headers: { Accept: "application/json", ...(init?.headers ?? {}) },
      cache: "no-store",
      signal: AbortSignal.timeout(timeoutMs),
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

export type YahooCandleTransport = {
  openTime: string;
  closeTime: string;
  open: string;
  high: string;
  low: string;
  close: string;
  volume: string;
};

export type YahooHistoryTransport = {
  ticker: string;
  yahooSymbol: string;
  interval: string;
  currency: string;
  delayed: boolean;
  decimals: number;
  tickSize: string;
  seriesName: string;
  note: string;
  candles: YahooCandleTransport[];
};

export const fetchYahooHistory = (ticker: string, interval: string) =>
  request<YahooHistoryTransport>(
    `/api/market/history?ticker=${encodeURIComponent(ticker)}&interval=${encodeURIComponent(interval)}&maxBars=180`,
    undefined,
    12000,
  );

export const fetchMarket = () => request<MarketTransport>("/api/trading/market");

export const fetchOrders = () => request<OrderTransport[]>("/api/orders");

export const fetchPortfolio = () => request<PortfolioTransport>("/api/portfolio");

export async function submitOrder(body: NewOrderRequest): Promise<ApiResult<OrderTransport>> {
  const result = await request<OrderTransport>("/api/orders", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
  if (!result.ok) return result;
  // A refusal is still HTTP 201. Treating every success status as "sent" would hide it.
  if (result.value.status === "rejected") {
    return { ok: false, error: "A ordem foi recusada antes de entrar no livro." };
  }
  return result;
}

export const previewChartOrder = (body: {
  ticker: string;
  side: OrderSide;
  price: string;
  quantity?: string;
}) =>
  request<ChartPreviewTransport>(
    "/api/orders/chart-preview",
    {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
    },
    12000,
  );

export const cancelOrder = (orderId: string) =>
  request<OrderTransport>(`/api/orders/${encodeURIComponent(orderId)}/cancel`, { method: "POST" });

function rejectedAsFailure(result: ApiResult<OrderTransport>): ApiResult<OrderTransport> {
  if (result.ok && result.value.status === "rejected") {
    return { ok: false, error: "A ordem foi recusada antes de entrar no livro." };
  }
  return result;
}

const jsonPost = (body: unknown): RequestInit => ({
  method: "POST",
  headers: { "Content-Type": "application/json" },
  body: JSON.stringify(body),
});

/** Closes the open quantity at market and leaves resting orders in the book. */
export const flattenPosition = (instrumentId: string) =>
  request<PositionActionTransport>("/api/orders/flatten", jsonPost({ instrumentId }));

/** Sends the opposite market quantity twice the open position. */
export const reversePosition = (instrumentId: string) =>
  request<PositionActionTransport>("/api/orders/reverse", jsonPost({ instrumentId }));

export async function reviseOrder(orderId: string, price: string): Promise<ApiResult<OrderTransport>> {
  const result = await request<OrderTransport>(
    `/api/orders/${encodeURIComponent(orderId)}/revise`,
    jsonPost({ price }),
  );
  return rejectedAsFailure(result);
}

export async function placeProtection(body: {
  instrumentId: string;
  price: string;
  origin?: OrderOrigin;
}): Promise<ApiResult<OrderTransport>> {
  const result = await request<OrderTransport>("/api/orders/protection", jsonPost(body), 12000);
  return rejectedAsFailure(result);
}

export type MarketQuoteTransport = {
  ticker: string;
  name: string;
  category: string;
  instrumentId: string;
  lastPrice: string;
  changePercent: string;
  changePoints: string;
  min: string;
  max: string;
  volume: string;
  updatedAt: string;
};

export type PerformanceReportTransport = {
  startingBalance: string;
  currentBalance: string;
  equity: string;
  realizedPnl: string;
  dailyPnl: string;
  winRate: string;
  winsCount: number;
  lossesCount: number;
  totalTrades: number;
  profitFactor: string;
  payoff: string;
  maxDrawdown: string;
  bestTrade: { ticker: string; pnl: string } | null;
  worstTrade: { ticker: string; pnl: string } | null;
  equityCurve: Array<{ time: string; equity: string; realizedPnl: string }>;
};

export type ClosedTradeTransport = {
  id: string;
  timestamp: string;
  ticker: string;
  market: string;
  type: string;
  quantity: string;
  entryPrice: string;
  exitPrice: string;
  pnl: string;
};

export type TerminalSnapshotTransport = {
  simulatedTime: string | null;
  quotes: MarketQuoteTransport[];
  portfolio: PortfolioTransport;
  orders: OrderTransport[];
  performance: PerformanceReportTransport;
  closedTrades: ClosedTradeTransport[];
};

export const fetchTerminal = () => request<TerminalSnapshotTransport>("/api/terminal");

export const resetSession = () =>
  request<{ status: string }>("/api/session/reset", { method: "POST" });

export const fetchMarketQuotes = () => request<MarketQuoteTransport[]>("/api/market/quotes");

export const fetchPerformanceReport = () => request<PerformanceReportTransport>("/api/reports/performance");

export const fetchClosedTrades = (ticker?: string) =>
  request<ClosedTradeTransport[]>(`/api/reports/trades${ticker ? `?ticker=${encodeURIComponent(ticker)}` : ""}`);

export const emergencyFlatten = () =>
  request<{ status: string; cancelledOrders: number; flattenedPositions: number }>("/api/orders/emergency-flat", {
    method: "POST",
  });

export type RestingOrderTransport = {
  orderId: string;
  side: "buy" | "sell";
  type: "market" | "limit";
  limitPrice: string | null;
  remainingQuantity: string;
  status: string;
};

export type MarketDepthTransport = {
  ticker: string;
  instrumentId: string;
  lastPrice: string;
  availableVolume: string;
  tickSize: string;
  restingOrders: RestingOrderTransport[];
};

export const fetchMarketDepth = (ticker?: string) =>
  request<MarketDepthTransport>(`/api/market/depth${ticker ? `?ticker=${encodeURIComponent(ticker)}` : ""}`);


