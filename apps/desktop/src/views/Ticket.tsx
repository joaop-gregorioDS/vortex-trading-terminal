import { useEffect, useState } from "react";
import type { Order } from "../bindings/Order";
import type { Position } from "../bindings/Position";
import { formatMoney, formatSignedMoney } from "../format";
import {
  cancelOrder,
  confirmDanger,
  emergencyFlat,
  flattenPosition,
  placeProtection,
  previewChart,
  resetSession,
  reversePosition,
  reviseOrder,
  submitOrder,
  readError,
} from "../ipc/client";
import {
  closingSide,
  idleProtection,
  lineTitle,
  resting,
  sessionOrder,
  stillPending,
  type ProtectionDraft,
  type ProtectionUnit,
  type Side,
} from "../trading";

function roleLabel(role: string | null): string {
  if (!role) return "";
  if (role === "target") return " · Alvo";
  if (role === "stop") return " · Stop";
  return ` · ${role}`;
}

const UNITS: Array<[ProtectionUnit, string]> = [
  ["ticks", "Ticks"],
  ["points", "Pontos"],
  ["percent", "Percentual"],
];

type Pending = { localId: string; orderId: string | null; text: string };

export function Ticket({
  ticker,
  instrumentId,
  orders,
  positions,
  busy,
  protectArmed,
  run,
  onNotice,
  onArm,
  onRevised,
  onProtected,
}: {
  ticker: string | null;
  instrumentId: string | null;
  orders: Order[];
  positions: Position[];
  busy: boolean;
  protectArmed: boolean;
  run: <T>(task: () => Promise<T>) => Promise<T | undefined>;
  onNotice: (message: string | null) => void;
  onArm: (armed: boolean) => void;
  onRevised: (order: Order) => void;
  onProtected: (orderId: string | null, price: string, title: string) => void;
}) {
  const [side, setSide] = useState<Side>("buy");
  const [kind, setKind] = useState<"market" | "limit">("market");
  const [quantity, setQuantity] = useState("100");
  const [limitPrice, setLimitPrice] = useState("");
  const [protection, setProtection] = useState<ProtectionDraft>(idleProtection);
  const [guardPrice, setGuardPrice] = useState("");
  const [pending, setPending] = useState<Pending[]>([]);
  const [drafts, setDrafts] = useState<Record<string, string>>({});

  useEffect(() => {
    setPending((current) => current.filter((item) => stillPending(item.orderId, orders)));
  }, [orders]);

  const mine = instrumentId ? orders.filter((order) => order.instrumentId === instrumentId) : [];
  const position = instrumentId
    ? positions.find((item) => item.instrumentId === instrumentId && !item.isClosed)
    : undefined;

  async function send() {
    if (!instrumentId) return;
    const built = sessionOrder({
      instrumentId,
      side,
      type: kind,
      quantity,
      limitPrice,
      protection,
    });
    if (built.error || !built.body) {
      onNotice(built.error ?? "A ordem não foi aceita.");
      return;
    }
    const localId = `local-${pending.length + 1}-${quantity}`;
    setPending((current) => [...current, { localId, orderId: null, text: "Enviando…" }]);
    try {
      const order = await run(() => submitOrder(built.body!));
      if (!order) {
        setPending((current) => current.filter((item) => item.localId !== localId));
        return;
      }
      setPending((current) =>
        current.map((item) =>
          item.localId === localId
            ? { localId, orderId: order.orderId, text: `${lineTitle(order.type)} · ${order.status}` }
            : item,
        ),
      );
      onNotice(null);
    } catch (error) {
      setPending((current) => current.filter((item) => item.localId !== localId));
      onNotice(readError(error));
    }
  }

  async function guard() {
    if (!instrumentId || !ticker) return;
    const sideToClose = position ? closingSide(position.quantity) : null;
    if (!sideToClose) {
      onNotice("Não há posição aberta neste ativo.");
      return;
    }
    const price = guardPrice.trim();
    try {
      const placed = await run(async () => {
        const preview = await previewChart({ ticker, side: sideToClose, price, quantity: null });
        const title = lineTitle(preview.type);
        onProtected(null, preview.price, title);
        const order = await placeProtection({
          instrumentId,
          price: preview.price,
          origin: "session",
        });
        return { order, price: preview.price, title };
      });
      if (!placed) {
        onProtected(null, "", "");
        return;
      }
      onProtected(placed.order.orderId, placed.price, placed.title);
      onNotice(`Proteção ${placed.title.toLowerCase()} enviada.`);
    } catch (error) {
      onProtected(null, "", "");
      onNotice(readError(error));
    }
  }

  async function danger(
    action: "reverse" | "emergency_flat" | "reset",
    next: (token: string) => Promise<unknown>,
  ) {
    try {
      const done = await run(async () => {
        const token = await confirmDanger(action);
        await next(token.token);
        return true;
      });
      if (done) onNotice(null);
    } catch (error) {
      onNotice(readError(error));
    }
  }

  return (
    <section className="ticket" aria-label="Boleta">
      <p className="brand">Boleta</p>
      {!instrumentId ? <p className="muted">Escolha um ativo.</p> : null}
      {instrumentId ? (
        <>
          <div className="modes" role="group" aria-label="Lado">
            <button type="button" className={side === "buy" ? "selected" : ""} onClick={() => setSide("buy")}>
              Comprar
            </button>
            <button type="button" className={side === "sell" ? "selected" : ""} onClick={() => setSide("sell")}>
              Vender
            </button>
          </div>
          <div className="modes" role="group" aria-label="Tipo">
            <button type="button" className={kind === "market" ? "selected" : ""} onClick={() => setKind("market")}>
              Mercado
            </button>
            <button type="button" className={kind === "limit" ? "selected" : ""} onClick={() => setKind("limit")}>
              Limite
            </button>
          </div>
          <label>
            Quantidade
            <input value={quantity} onChange={(event) => setQuantity(event.target.value)} inputMode="decimal" />
          </label>
          {kind === "limit" ? (
            <label>
              Preço
              <input value={limitPrice} onChange={(event) => setLimitPrice(event.target.value)} inputMode="decimal" />
            </label>
          ) : null}
          <label className="check">
            <input
              type="checkbox"
              checked={protection.enabled}
              onChange={(event) => setProtection({ ...protection, enabled: event.target.checked })}
            />
            Proteção
          </label>
          {protection.enabled ? (
            <>
              <div className="modes" role="group" aria-label="Unidade">
                {UNITS.map(([unit, label]) => (
                  <button
                    key={unit}
                    type="button"
                    className={protection.unit === unit ? "selected" : ""}
                    onClick={() => setProtection({ ...protection, unit })}
                  >
                    {label}
                  </button>
                ))}
              </div>
              <label>
                Ganho
                <input value={protection.gain} onChange={(event) => setProtection({ ...protection, gain: event.target.value })} inputMode="decimal" />
              </label>
              <label>
                Perda
                <input value={protection.loss} onChange={(event) => setProtection({ ...protection, loss: event.target.value })} inputMode="decimal" />
              </label>
              <label>
                Trilha
                <input value={protection.trail} onChange={(event) => setProtection({ ...protection, trail: event.target.value })} inputMode="decimal" />
              </label>
              <label>
                Empate
                <input value={protection.breakEven} onChange={(event) => setProtection({ ...protection, breakEven: event.target.value })} inputMode="decimal" />
              </label>
            </>
          ) : null}
          <button type="button" disabled={busy} onClick={() => void send()}>
            Enviar ordem
          </button>
          <div className="book">
            <p className="brand">Posição</p>
            {position ? (
              <>
                <p>
                  {formatMoney(position.quantity)} · médio {formatMoney(position.averageCost)} ·{" "}
                  {formatSignedMoney(position.totalProfitAndLoss)}
                </p>
                <div className="modes">
                  <button
                    type="button"
                    disabled={busy}
                    onClick={() => {
                      void run(() => flattenPosition(instrumentId))
                        .then(() => onNotice(null))
                        .catch((error: unknown) => onNotice(readError(error)));
                    }}
                  >
                    Zerar
                  </button>
                  <button
                    type="button"
                    disabled={busy}
                    onClick={() => {
                      void danger("reverse", (token) => reversePosition(instrumentId, token));
                    }}
                  >
                    Inverter
                  </button>
                </div>
              </>
            ) : (
              <p className="muted">Sem posição aberta.</p>
            )}
          </div>
          <div className="book">
            <p className="brand">Ordens</p>
            {mine.map((order) => (
              <article key={order.orderId} className="order">
                <p>
                  {order.side === "buy" ? "Compra" : order.side === "sell" ? "Venda" : order.side} · {lineTitle(order.type)} · {order.status}
                  {roleLabel(order.role)}
                </p>
                <p>
                  {formatMoney(order.quantity)}
                  {order.limitPrice ? ` · ${formatMoney(order.limitPrice)}` : ""}
                </p>
                {resting(order.status) ? (
                  <div className="modes">
                    <button
                      type="button"
                      disabled={busy}
                      onClick={() => {
                        void run(() => cancelOrder(order.orderId))
                          .then(() => onNotice(null))
                          .catch((error: unknown) => onNotice(readError(error)));
                      }}
                    >
                      Cancelar
                    </button>
                    {order.limitPrice ? (
                      <>
                        <input
                          value={drafts[order.orderId] ?? order.limitPrice}
                          onChange={(event) =>
                            setDrafts((current) => ({ ...current, [order.orderId]: event.target.value }))
                          }
                          inputMode="decimal"
                          aria-label={`Preço de ${order.orderId}`}
                        />
                        <button
                          type="button"
                          disabled={busy}
                          onClick={() => {
                            void run(() => reviseOrder(order.orderId, drafts[order.orderId] ?? order.limitPrice ?? ""))
                              .then((revised) => {
                                if (revised) onRevised(revised);
                                onNotice(null);
                              })
                              .catch((error: unknown) => onNotice(readError(error)));
                          }}
                        >
                          Revisar
                        </button>
                      </>
                    ) : null}
                  </div>
                ) : null}
              </article>
            ))}
            {pending.filter((item) => stillPending(item.orderId, orders)).map((item) => (
              <article key={item.localId} className="order">
                <p>{item.text}</p>
              </article>
            ))}
            {mine.length === 0 && pending.length === 0 ? <p className="muted">Sem ordens deste ativo.</p> : null}
          </div>
          <label>
            Preço da proteção
            <input value={guardPrice} onChange={(event) => setGuardPrice(event.target.value)} inputMode="decimal" />
          </label>
          <button type="button" disabled={busy} onClick={() => void guard()}>
            Enviar proteção
          </button>
          <button type="button" className={protectArmed ? "selected" : ""} onClick={() => onArm(!protectArmed)}>
            Soltar proteção no gráfico
          </button>
          <div className="modes danger">
            <button
              type="button"
              disabled={busy}
              onClick={() => {
                void danger("emergency_flat", (token) => emergencyFlat(token));
              }}
            >
              Zerar tudo
            </button>
            <button
              type="button"
              disabled={busy}
              onClick={() => {
                void danger("reset", (token) => resetSession(token));
              }}
            >
              Resetar sessão
            </button>
          </div>
        </>
      ) : null}
    </section>
  );
}
