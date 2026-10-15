import { describe, expect, it } from "vitest";
import type { DiagnosticReport } from "./bindings/DiagnosticReport";
import { diagnosticBody, themeAttribute } from "./diagnostic";

describe("tema e diagnóstico", () => {
  it("aplica claro e escuro e deixa o sistema seguir o computador", () => {
    expect(themeAttribute("light")).toBe("light");
    expect(themeAttribute("dark")).toBe("dark");
    expect(themeAttribute("system")).toBeNull();
    expect(themeAttribute(undefined)).toBeNull();
  });

  it("mostra o texto de saúde e o decimal do estado sem perder casas", () => {
    const health: DiagnosticReport = {
      title: "Saúde da API",
      health: { status: 503, body: "Unhealthy" },
      status: null,
    };
    expect(diagnosticBody(health)).toBe("Unhealthy · HTTP 503");

    const status: DiagnosticReport = {
      title: "Estado da sessão",
      health: null,
      status: {
        instrumentId: "11111111-1111-1111-1111-111111111111",
        simulatedTime: "2026-10-14T13:00:00Z",
        lastPrice: "36.50",
        restingOrderCount: 1,
        orderCount: 2,
        filledOrderCount: 1,
        openOrderCount: 1,
        rejectedOrderCount: 0,
        fillCount: 1,
        tradedQuantity: "1.000000000000000000000000001",
        restoredOrderCount: 0,
      },
    };
    const text = diagnosticBody(status);
    expect(text).toContain("1,000000000000000000000000001");
    expect(text).toContain("36,50");
    expect(text).toContain("14/10 13:00:00");
    expect(text.includes("e+") || text.includes("E+")).toBe(false);
  });
});
