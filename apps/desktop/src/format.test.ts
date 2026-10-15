import { describe, expect, it } from "vitest";
import { formatDateTime, formatMoney, formatPercentRatio, formatSignedMoney, groupDecimal } from "./format";

describe("groupDecimal", () => {
  it("agrupa sem converter para number", () => {
    expect(groupDecimal("1000")).toBe("1.000");
    expect(groupDecimal("872.01")).toBe("872,01");
    expect(groupDecimal("1.000000000000000000000000001")).toBe("1,000000000000000000000000001");
    expect(groupDecimal("-1234567.89")).toBe("-1.234.567,89");
    expect(formatMoney("100000.00")).toBe("100.000");
    expect(formatSignedMoney("-1234567.89")).toBe("-1.234.567,89");
    expect(formatDateTime("2026-10-14T13:05:00Z")).toBe("14/10 13:05:00");
  });

  it("mostra a razão do servidor como percentual, sem Number", () => {
    expect(formatPercentRatio("0.12")).toBe("12 %");
    expect(formatPercentRatio("0.125")).toBe("12,5 %");
    expect(formatPercentRatio("-0.02")).toBe("-2 %");
    expect(formatPercentRatio("")).toBe("—");
  });
});
