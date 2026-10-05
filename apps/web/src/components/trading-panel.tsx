"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import {
  cancelOrder,
  fetchMarket,
  fetchOrders,
  fetchPortfolio,
  submitOrder,
  type MarketTransport,
  type OrderSide,
  type OrderTransport,
  type OrderType,
  type PortfolioTransport,
} from "@/lib/trading/transport";
import {
  formatDateTime,
  formatInstant,
  formatMoney,
  formatSignedMoney,
  isCancellable,
  sideLabels,
  statusLabels,
  statusTone,
  toneForSignedValue,
  typeLabels,
} from "@/lib/trading/format";

const DEMO_INSTRUMENT = "11111111-1111-1111-1111-111111111111";
const POLL_INTERVAL_MS = 1500;

type Notice = { tone: "ok" | "error"; text: string } | null;

export function TradingPanel({ instrumentId = DEMO_INSTRUMENT }: { instrumentId?: string }) {
  const [market, setMarket] = useState<MarketTransport | null>(null);
  const [orders, setOrders] = useState<OrderTransport[]>([]);
  const [portfolio, setPortfolio] = useState<PortfolioTransport | null>(null);
  const [side, setSide] = useState<OrderSide>("buy");
  const [type, setType] = useState<OrderType>("market");
  const [quantity, setQuantity] = useState("10");
  const [limitPrice, setLimitPrice] = useState("");
  const [notice, setNotice] = useState<Notice>(null);
  const [busy, setBusy] = useState(false);

  // Fills are the only thing that changes a position, so the portfolio is refetched when the set of
  // executions grows rather than on every poll.
  const fillSignature = useRef("");

  const refresh = useCallback(async () => {
    const [marketResult, ordersResult] = await Promise.all([fetchMarket(), fetchOrders()]);

    if (marketResult.ok) setMarket(marketResult.value);
    if (ordersResult.ok) setOrders(ordersResult.value);

    // Surface whichever read failed, preferring the market because an operator who cannot see the
    // price cannot trade safely. Success clears the notice so a recovered poll stops showing a stale
    // error.
    const failure = !marketResult.ok ? marketResult.error : !ordersResult.ok ? ordersResult.error : null;
    setNotice(failure === null ? null : { tone: "error", text: failure });

    const signature = ordersResult.ok
      ? ordersResult.value.map((order) => `${order.orderId}:${order.fills.length}`).join("|")
      : "";
    if (signature !== fillSignature.current) {
      const portfolioResult = await fetchPortfolio();
      if (portfolioResult.ok) {
        setPortfolio(portfolioResult.value);
        fillSignature.current = signature;
      }
    }
  }, []);

  useEffect(() => {
    let active = true;
    const tick = () => {
      if (active) void refresh();
    };
    tick();
    const timer = window.setInterval(tick, POLL_INTERVAL_MS);
    return () => {
      active = false;
      window.clearInterval(timer);
    };
  }, [refresh]);

  const validation = useMemo(() => {
    if (!/^\d+(\.\d+)?$/.test(quantity) || Number(quantity) <= 0) {
      return "Informe uma quantidade positiva com ponto decimal.";
    }
    if (type === "limit" && !/^\d+(\.\d+)?$/.test(limitPrice)) {
      return "Ordem limite exige um preço positivo.";
    }
    return null;
  }, [quantity, limitPrice, type]);

  const onSellAll = useCallback(
    (positionQuantity: string) => {
      setSide("sell");
      setQuantity(positionQuantity);
      setNotice({ tone: "ok", text: "Quantidade da posição preenchida para venda." });
    },
    [],
  );

  const onSubmit = useCallback(
    async (event: React.FormEvent<HTMLFormElement>) => {
      event.preventDefault();
      if (validation) {
        setNotice({ tone: "error", text: validation });
        return;
      }

      setBusy(true);
      const result = await submitOrder({
        instrumentId,
        side,
        type,
        quantity,
        ...(type === "limit" ? { limitPrice } : {}),
      });
      setBusy(false);

      setNotice(
        result.ok
          ? { tone: "ok", text: `Ordem ${result.value.orderId.slice(0, 8)} enviada como ${statusLabels[result.value.status].toLowerCase()}.` }
          : { tone: "error", text: result.error },
      );
      await refresh();
    },
    [instrumentId, side, type, quantity, limitPrice, validation, refresh],
  );

  const onCancel = useCallback(
    async (orderId: string) => {
      setBusy(true);
      const result = await cancelOrder(orderId);
      setBusy(false);
      setNotice(
        result.ok
          ? { tone: "ok", text: `Cancelamento aplicado: ${statusLabels[result.value.status].toLowerCase()}.` }
          : { tone: "error", text: result.error },
      );
      await refresh();
    },
    [refresh],
  );

  const openPosition = portfolio?.positions.find((position) => !position.isClosed);

  return (
    <div className="grid gap-6 lg:grid-cols-[320px_1fr]">
      <section className="space-y-4">
        <MarketCard market={market} connected={market !== null} />

        <form
          onSubmit={onSubmit}
          className="rounded-xl border border-slate-800 bg-[#0a111e] p-5"
          aria-label="Enviar ordem"
        >
          <h2 className="text-sm font-medium tracking-[0.14em] text-slate-400">ENVIAR ORDEM</h2>

          <Field label="Lado">
            <div className="grid grid-cols-2 gap-2">
              {(["buy", "sell"] as const).map((option) => (
                <button
                  key={option}
                  type="button"
                  onClick={() => setSide(option)}
                  aria-pressed={side === option}
                  className={`rounded-lg border px-3 py-2 text-sm transition ${
                    side === option
                      ? option === "buy"
                        ? "border-emerald-400/40 bg-emerald-400/10 text-emerald-300"
                        : "border-rose-400/40 bg-rose-400/10 text-rose-300"
                      : "border-slate-700 text-slate-400 hover:border-slate-600"
                  }`}
                >
                  {sideLabels[option]}
                </button>
              ))}
            </div>
          </Field>

          <Field label="Tipo">
            <div className="grid grid-cols-2 gap-2">
              {(["market", "limit"] as const).map((option) => (
                <button
                  key={option}
                  type="button"
                  onClick={() => setType(option)}
                  aria-pressed={type === option}
                  className={`rounded-lg border px-3 py-2 text-sm transition ${
                    type === option
                      ? "border-sky-400/40 bg-sky-400/10 text-sky-300"
                      : "border-slate-700 text-slate-400 hover:border-slate-600"
                  }`}
                >
                  {typeLabels[option]}
                </button>
              ))}
            </div>
          </Field>

          <Field label="Quantidade">
            <input
              value={quantity}
              onChange={(event) => setQuantity(event.target.value)}
              inputMode="decimal"
              className="w-full rounded-lg border border-slate-700 bg-[#070d17] px-3 py-2 text-sm text-slate-100 outline-none focus:border-sky-500"
            />
          </Field>

          {type === "limit" && (
            <Field label="Preço limite">
              <input
                value={limitPrice}
                onChange={(event) => setLimitPrice(event.target.value)}
                inputMode="decimal"
                placeholder="0.00"
                className="w-full rounded-lg border border-slate-700 bg-[#070d17] px-3 py-2 text-sm text-slate-100 outline-none focus:border-sky-500"
              />
            </Field>
          )}

          {validation && <p className="mb-3 text-xs text-amber-300">{validation}</p>}

          <button
            type="submit"
            disabled={busy || validation !== null}
            className="w-full rounded-lg border border-sky-500/40 bg-sky-500/10 px-4 py-2 text-sm text-sky-200 transition hover:bg-sky-500/20 disabled:cursor-not-allowed disabled:border-slate-700 disabled:bg-transparent disabled:text-slate-500"
          >
            {busy ? "Enviando..." : "Enviar ordem"}
          </button>

          {notice && (
            <p
              role="status"
              className={`mt-3 text-xs ${notice.tone === "ok" ? "text-emerald-300" : "text-rose-300"}`}
            >
              {notice.text}
            </p>
          )}
        </form>

        <PortfolioCard portfolio={portfolio} />
      </section>

      <section className="space-y-6">
        <OrdersTable orders={orders} busy={busy} onCancel={onCancel} />
        {openPosition && (
          <PositionCard
            position={openPosition}
            onSellAll={() => onSellAll(openPosition.quantity)}
          />
        )}
      </section>
    </div>
  );
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="mb-3">
      <span className="mb-1 block text-[11px] tracking-[0.12em] text-slate-500">{label.toUpperCase()}</span>
      {children}
    </div>
  );
}

function MarketCard({ market, connected }: { market: MarketTransport | null; connected: boolean }) {
  return (
    <div className="rounded-xl border border-slate-800 bg-[#0a111e] p-5">
      <div className="flex items-center justify-between">
        <h2 className="text-sm font-medium tracking-[0.14em] text-slate-400">MERCADO SIMULADO</h2>
        <span
          className={`rounded-full border px-2 py-0.5 text-[10px] ${
            connected
              ? "border-emerald-400/30 bg-emerald-400/10 text-emerald-300"
              : "border-amber-400/30 bg-amber-400/10 text-amber-300"
          }`}
        >
          {connected ? "CONECTADO" : "SEM DADOS"}
        </span>
      </div>

      <p className="mt-4 text-3xl font-medium tabular-nums text-slate-100">
        {market?.lastPrice ? formatMoney(market.lastPrice) : "—"}
      </p>
      <dl className="mt-4 space-y-1 text-xs text-slate-500">
        <Row label="Tempo simulado" value={formatInstant(market?.simulatedTime ?? null)} />
        <Row label="Ordens em repouso" value={String(market?.restingOrderCount ?? 0)} />
        <Row label="Instrumento" value={(market?.instrumentId ?? "—").slice(0, 8)} />
      </dl>

      <p className="mt-4 border-t border-slate-800 pt-3 text-[11px] leading-relaxed text-slate-600">
        Preços gerados por simulação determinística, sem dado real de mercado. Ordens cruzam com a
        próxima observação posterior ao envio.
      </p>
    </div>
  );
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex justify-between gap-3">
      <dt>{label}</dt>
      <dd className="tabular-nums text-slate-300">{value}</dd>
    </div>
  );
}

function PortfolioCard({ portfolio }: { portfolio: PortfolioTransport | null }) {
  return (
    <div className="rounded-xl border border-slate-800 bg-[#0a111e] p-5">
      <h2 className="text-sm font-medium tracking-[0.14em] text-slate-400">CONTA SIMULADA</h2>
      <dl className="mt-4 space-y-1 text-xs">
        <Row label="Caixa" value={portfolio ? formatMoney(portfolio.cash) : "—"} />
        <Row label="Patrimônio" value={portfolio ? formatMoney(portfolio.equity) : "—"} />
        <Row label="Custo das posições" value={portfolio ? formatMoney(portfolio.totalCost) : "—"} />
      </dl>
      <div className="mt-4 grid grid-cols-2 gap-3 border-t border-slate-800 pt-3 text-xs">
        <div>
          <p className="text-slate-500">Realizado</p>
          <p className={`tabular-nums ${toneForSignedValue(portfolio?.realizedProfitAndLoss ?? "0")}`}>
            {portfolio ? formatSignedMoney(portfolio.realizedProfitAndLoss) : "—"}
          </p>
        </div>
        <div>
          <p className="text-slate-500">Não realizado</p>
          <p className={`tabular-nums ${toneForSignedValue(portfolio?.unrealizedProfitAndLoss ?? "0")}`}>
            {portfolio ? formatSignedMoney(portfolio.unrealizedProfitAndLoss) : "—"}
          </p>
        </div>
      </div>
    </div>
  );
}

function PositionCard({
  position,
  onSellAll,
}: {
  position: PortfolioTransport["positions"][number];
  onSellAll: () => void;
}) {
  return (
    <div className="rounded-xl border border-slate-800 bg-[#0a111e] p-5">
      <div className="flex items-center justify-between">
        <h2 className="text-sm font-medium tracking-[0.14em] text-slate-400">POSIÇÃO ABERTA</h2>
        <button
          type="button"
          onClick={onSellAll}
          className="rounded-lg border border-slate-700 px-3 py-1 text-xs text-slate-300 transition hover:border-slate-600"
        >
          Vender tudo
        </button>
      </div>
      <dl className="mt-4 grid grid-cols-2 gap-x-6 gap-y-2 text-xs sm:grid-cols-4">
        <div>
          <dt className="text-slate-500">Quantidade</dt>
          <dd className="tabular-nums text-slate-200">{formatMoney(position.quantity)}</dd>
        </div>
        <div>
          <dt className="text-slate-500">Custo médio</dt>
          <dd className="tabular-nums text-slate-200">{formatMoney(position.averageCost)}</dd>
        </div>
        <div>
          <dt className="text-slate-500">Custo total</dt>
          <dd className="tabular-nums text-slate-200">{formatMoney(position.totalCost)}</dd>
        </div>
        <div>
          <dt className="text-slate-500">Resultado</dt>
          <dd className={`tabular-nums ${toneForSignedValue(position.totalProfitAndLoss)}`}>
            {formatSignedMoney(position.totalProfitAndLoss)}
          </dd>
        </div>
      </dl>
      <p className="mt-3 text-[11px] text-slate-600">
        Atualizada em {formatDateTime(position.updatedAt)} · custo médio, não FIFO.
      </p>
    </div>
  );
}

function OrdersTable({
  orders,
  busy,
  onCancel,
}: {
  orders: OrderTransport[];
  busy: boolean;
  onCancel: (orderId: string) => void;
}) {
  const newestFirst = [...orders].reverse();

  return (
    <div className="rounded-xl border border-slate-800 bg-[#0a111e]">
      <div className="flex items-center justify-between border-b border-slate-800 px-5 py-4">
        <h2 className="text-sm font-medium tracking-[0.14em] text-slate-400">ORDENS DA SESSÃO</h2>
        <span className="text-xs text-slate-500">{orders.length} no total</span>
      </div>

      {newestFirst.length === 0 ? (
        <p className="px-5 py-8 text-sm text-slate-500">
          Nenhuma ordem ainda. Envie uma ordem para começar a operar a sessão simulada.
        </p>
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full text-left text-xs">
            <thead className="text-[11px] tracking-[0.1em] text-slate-500">
              <tr className="border-b border-slate-800">
                <th className="px-5 py-2 font-normal">ENVIADA</th>
                <th className="px-3 py-2 font-normal">LADO</th>
                <th className="px-3 py-2 font-normal">TIPO</th>
                <th className="px-3 py-2 text-right font-normal">QUANTIDADE</th>
                <th className="px-3 py-2 text-right font-normal">EXECUTADO</th>
                <th className="px-3 py-2 font-normal">PREÇO MÉDIO</th>
                <th className="px-3 py-2 font-normal">SITUAÇÃO</th>
                <th className="px-5 py-2" />
              </tr>
            </thead>
            <tbody>
              {newestFirst.map((order) => (
                <tr key={order.orderId} className="border-b border-slate-900 last:border-0">
                  <td className="px-5 py-2 tabular-nums text-slate-400">{formatInstant(order.createdAt)}</td>
                  <td className={`px-3 py-2 ${order.side === "buy" ? "text-emerald-300" : "text-rose-300"}`}>
                    {sideLabels[order.side]}
                  </td>
                  <td className="px-3 py-2 text-slate-400">
                    {typeLabels[order.type]}
                    {order.limitPrice ? ` ${formatMoney(order.limitPrice)}` : ""}
                  </td>
                  <td className="px-3 py-2 text-right tabular-nums text-slate-200">
                    {formatMoney(order.quantity)}
                  </td>
                  <td className="px-3 py-2 text-right tabular-nums text-slate-200">
                    {formatMoney(order.filledQuantity)}
                  </td>
                  <td className="px-3 py-2 tabular-nums text-slate-300">
                    {averageFillPrice(order)}
                  </td>
                  <td className="px-3 py-2">
                    <span className={`rounded-full border px-2 py-0.5 text-[10px] ${statusTone(order.status)}`}>
                      {statusLabels[order.status]}
                    </span>
                  </td>
                  <td className="px-5 py-2 text-right">
                    {isCancellable(order.status) && (
                      <button
                        type="button"
                        disabled={busy}
                        onClick={() => onCancel(order.orderId)}
                        className="rounded-lg border border-slate-700 px-3 py-1 text-[11px] text-slate-300 transition hover:border-rose-400/40 hover:text-rose-300 disabled:opacity-50"
                      >
                        Cancelar
                      </button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

/**
 * Shows the price the fills actually executed at, or the limit price while nothing filled.
 *
 * The client does not compute a weighted average: that is money arithmetic, and it belongs to the
 * backend whose exactness guarantees the number. When several fills exist at different prices the
 * cell shows the range, which is honest and needs no division here.
 */
function averageFillPrice(order: OrderTransport): string {
  if (order.fills.length === 0) {
    return order.limitPrice ? `${formatMoney(order.limitPrice)} (limite)` : "—";
  }
  const prices = Array.from(new Set(order.fills.map((fill) => fill.price)));
  if (prices.length === 1) return formatMoney(prices[0]);
  const lowest = prices.reduce((min, price) => (compareDecimal(price, min) < 0 ? price : min));
  const highest = prices.reduce((max, price) => (compareDecimal(price, max) > 0 ? price : max));
  return `${formatMoney(lowest)}–${formatMoney(highest)}`;
}

/** Compares two non-negative decimal strings without converting them to a float. */
function compareDecimal(left: string, right: string): number {
  const [leftWhole, leftFraction = ""] = left.split(".");
  const [rightWhole, rightFraction = ""] = right.split(".");
  const leftPadded = leftWhole.padStart(Math.max(leftWhole.length, rightWhole.length), "0");
  const rightPadded = rightWhole.padStart(Math.max(leftWhole.length, rightWhole.length), "0");
  if (leftPadded !== rightPadded) return leftPadded < rightPadded ? -1 : 1;

  const width = Math.max(leftFraction.length, rightFraction.length);
  const leftTail = leftFraction.padEnd(width, "0");
  const rightTail = rightFraction.padEnd(width, "0");
  if (leftTail === rightTail) return 0;
  return leftTail < rightTail ? -1 : 1;
}
