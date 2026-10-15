import { describe, expect, it } from "vitest";
import { adaptCandles, closeTimeForDrawing, decimalForDrawing } from "./drawing";

const candle = {
  openTime: "2026-10-14T13:00:00.0000000Z",
  closeTime: "2026-10-14T13:05:00.0000000Z",
  open: "36.10",
  high: "36.80",
  low: "36.00",
  close: "36.50",
};

describe("candles do servidor", () => {
  it("aceita o minuto fechado com fração zerada e recusa segundo diferente de zero", () => {
    expect(closeTimeForDrawing("2026-10-14T13:05:00.0000000Z")).toBeTypeOf("number");
    expect(() => closeTimeForDrawing("2026-10-14T13:05:01.0000000Z")).toThrow(/timestamp/);
    expect(() => decimalForDrawing("36.50abc")).toThrow(/decimal/i);
    const drawing = adaptCandles("5m", 1, [candle]);
    expect(drawing[0].open).toBe(36.1);
    expect(drawing[0].close).toBe(36.5);
    expect(() => adaptCandles("5m", 2, [candle])).toThrow(/context/);
  });

  it("aceita o candle diário fechado em minuto cheio", () => {
    const day = {
      openTime: "2026-10-13T00:00:00Z",
      closeTime: "2026-10-14T00:00:00Z",
      open: "36.10",
      high: "36.80",
      low: "36.00",
      close: "36.50",
    };
    const drawing = adaptCandles("1d", 1, [day]);
    expect(drawing[0].time - closeTimeForDrawing(day.openTime)).toBe(86400);
  });
});
