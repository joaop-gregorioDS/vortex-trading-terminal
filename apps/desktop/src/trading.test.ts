import { describe, expect, it } from "vitest";
import {
  closingSide,
  coordinateDraft,
  protectionBody,
  sessionOrder,
  stillPending,
  idleProtection,
} from "./trading";

describe("boleta", () => {
  it("recusa mercado com preço e limite sem preço", () => {
    const market = sessionOrder({
      instrumentId: "11111111-1111-1111-1111-111111111111",
      side: "buy",
      type: "market",
      quantity: "10",
      limitPrice: "36.50",
      protection: idleProtection,
    });
    expect(market.body?.limitPrice).toBeNull();
    expect(market.body?.type).toBe("market");

    const limit = sessionOrder({
      instrumentId: "11111111-1111-1111-1111-111111111111",
      side: "sell",
      type: "limit",
      quantity: "0",
      limitPrice: "36.50",
      protection: idleProtection,
    });
    expect(limit.error).toMatch(/quantidade/);
  });

  it("exige ganho e perda quando a proteção está ligada", () => {
    expect(protectionBody({ ...idleProtection, enabled: true, gain: "1", loss: "" }).error).toMatch(/proteção/);
    const armed = protectionBody({ ...idleProtection, enabled: true, gain: "1.5", loss: "0.5", unit: "ticks" });
    expect(armed.body).toEqual({ gain: "1.5", loss: "0.5", unit: "ticks", trail: null, breakEven: null });
  });

  it("tira a linha otimista quando o snapshot traz a ordem", () => {
    expect(stillPending(null, [])).toBe(true);
    expect(stillPending("abc", [{ orderId: "abc" }])).toBe(false);
    expect(stillPending("abc", [{ orderId: "xyz" }])).toBe(true);
  });

  it("escolhe o lado de saída pelo sinal da quantidade", () => {
    expect(closingSide("100")).toBe("sell");
    expect(closingSide("-2")).toBe("buy");
    expect(closingSide("0.00")).toBeNull();
    expect(coordinateDraft(36.5, 2)).toBe("36.50");
    expect(coordinateDraft(0, 2)).toBeNull();
  });
});
