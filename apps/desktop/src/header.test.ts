import { describe, expect, it } from "vitest";
import { catalogTickers, headerModel, instrumentFor, pickTicker, sessionLabel } from "./header";

describe("cabeçalho", () => {
  it("zera caixa, patrimônio e P&L quando a conexão cai e mantém o carimbo", () => {
    const hidden = headerModel({
      cash: "100000.00",
      equity: "100010.50",
      pnl: "-1234567.89",
      state: "disconnected",
      lastSuccessAt: "2026-10-14T13:05:00Z",
    });
    expect(hidden).toEqual({ cash: "—", equity: "—", pnl: "—", stamp: "14/10 13:05:00" });

    const visible = headerModel({
      cash: "100000.00",
      equity: "100010.50",
      pnl: "-1234567.89",
      state: "degraded",
      lastSuccessAt: "2026-10-14T13:05:00Z",
    });
    expect(visible.cash).toBe("100.000");
    expect(visible.pnl).toBe("-1.234.567,89");
    expect(visible.stamp).toBe("14/10 13:05:00");
  });

  it("escolhe PETR4 quando a grade traz esse ticker", () => {
    expect(pickTicker(null, [{ ticker: "VALE3" }, { ticker: "PETR4" }])).toBe("PETR4");
    expect(pickTicker("VALE3", [{ ticker: "VALE3" }, { ticker: "PETR4" }])).toBe("VALE3");
    expect(pickTicker(null, [{ ticker: "VALE3" }])).toBe("VALE3");
    expect(sessionLabel(null)).toBe("Aguardando pregão");
    expect(sessionLabel(false)).toBe("Pregão fechado");
  });

  it("lista o catálogo na ordem do snapshot e resolve o instrumento do ticker", () => {
    const quotes = [
      { ticker: "VALE3", instrumentId: "id-vale" },
      { ticker: "ITUB4", instrumentId: "id-itub" },
    ];
    expect(catalogTickers(quotes)).toEqual(["VALE3", "ITUB4"]);
    expect(instrumentFor("VALE3", quotes)).toBe("id-vale");
    expect(instrumentFor("PETR4", quotes)).toBeNull();
    expect(instrumentFor(null, quotes)).toBeNull();
  });
});
