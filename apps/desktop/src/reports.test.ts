import { describe, expect, it } from "vitest";
import { equityLine, tradeQuery } from "./reports";

describe("relatórios", () => {
  it("omite todos e um ticker que não está no catálogo", () => {
    expect(tradeQuery("todos", ["PETR4"])).toBeNull();
    expect(tradeQuery("VALE3", ["PETR4"])).toBeNull();
    expect(tradeQuery("PETR4", ["PETR4", "VALE3"])).toBe("PETR4");
  });

  it("desenha a curva sem trocar o texto do servidor", () => {
    expect(equityLine([])).toBeNull();
    expect(equityLine(["100000.50"])).toBeNull();
    expect(equityLine(["100000.50", "100000.50"])).toBe("0.00,0.00 100.00,0.00");
    expect(equityLine(["1", "2"])).toBe("0.00,100.00 100.00,0.00");
    expect(equityLine(["1", "nope"])).toBeNull();
  });
});
