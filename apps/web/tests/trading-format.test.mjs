import test from "node:test";
import assert from "node:assert/strict";
import {
  brasiliaFromUnix,
  formatChartCrosshair,
  formatChartPrice,
  formatChartTick,
  formatInstant,
  formatDateTime,
  formatMoney,
  formatRatioPercent,
  formatSignedMoney,
  groupDecimal,
  isCancellable,
  statusLabels,
  toneForSignedValue,
  trimDecimal,
} from "../src/lib/trading/format.ts";

test("grouping separates thousands without touching the exact fractional digits", () => {
  assert.equal(groupDecimal("1000"), "1.000");
  assert.equal(groupDecimal("9983"), "9.983");
  assert.equal(groupDecimal("1234567"), "1.234.567");
  assert.equal(groupDecimal("872.01"), "872,01");
  assert.equal(groupDecimal("96.89"), "96,89");
  assert.equal(groupDecimal("0"), "0");
});

test("grouping keeps the sign and every digit a high precision value carries", () => {
  assert.equal(groupDecimal("-134"), "-134");
  assert.equal(groupDecimal("-1234567.89"), "-1.234.567,89");
  // 27 fractional digits must survive display untouched: the terminal never rounds money.
  assert.equal(
    groupDecimal("1.000000000000000000000000001"),
    "1,000000000000000000000000001",
  );
});

test("trailing zeros are trimmed only when they are insignificant", () => {
  assert.equal(trimDecimal("99.830"), "99.83");
  assert.equal(trimDecimal("99.835"), "99.835");
  assert.equal(trimDecimal("100"), "100");
  assert.equal(trimDecimal("0.000"), "0");
  assert.equal(trimDecimal("0.500"), "0.5");
});

test("money formatting composes grouping and trimming", () => {
  assert.equal(formatMoney("100000"), "100.000");
  assert.equal(formatMoney("872.010"), "872,01");
  assert.equal(formatMoney("1000", "BRL"), "1.000 BRL");
});

test("signed formatting always shows direction, including gains", () => {
  assert.equal(formatSignedMoney("335"), "+335");
  assert.equal(formatSignedMoney("-134"), "-134");
  assert.equal(formatSignedMoney("-1234.5"), "-1.234,5");
});

test("tone reflects the sign so a loss cannot look like a gain", () => {
  assert.equal(toneForSignedValue("-1"), "text-rose-300");
  assert.equal(toneForSignedValue("0"), "text-slate-300");
  assert.equal(toneForSignedValue("0.000"), "text-slate-300");
  assert.equal(toneForSignedValue("12.5"), "text-emerald-300");
});

test("instants are shown as a clock and as a date without timezone drift", () => {
  assert.equal(formatInstant("2026-10-01T10:12:00.0000000Z"), "10:12:00");
  assert.equal(formatInstant(null), "—");
  assert.equal(formatDateTime("2026-10-01T10:12:00.0000000Z"), "01/10 10:12:00");
  assert.equal(formatDateTime(null), "—");
});

test("a session ratio is shown as a short percent and money digits stay intact", () => {
  assert.equal(formatRatioPercent("0.5"), "50%");
  assert.equal(formatRatioPercent("0.125"), "12,5%");
  assert.equal(formatRatioPercent("1"), "100%");
  assert.equal(formatRatioPercent("0"), "0%");
  assert.equal(formatRatioPercent(null), "—");
  assert.equal(formatRatioPercent("0.0002155275635806312562862206"), "0,02%");
  assert.equal(formatRatioPercent("-0.021555"), "-2,16%");
  assert.equal(formatRatioPercent("0.01996"), "2%");
  assert.equal(
    groupDecimal("1.000000000000000000000000001"),
    "1,000000000000000000000000001",
  );
});

test("chart prices use the same grouping as a quote", () => {
  assert.equal(formatChartPrice(208835, 0), "208.835");
  assert.equal(formatChartPrice(54.74, 2), "54,74");
  assert.equal(formatChartPrice(5432.5, 1), "5.432,5");
  assert.equal(formatChartPrice(-134, 0), "-134");
});

test("chart clocks are Brasília and do not follow the machine zone", () => {
  const eighteenUtc = Date.parse("2026-10-01T18:00:00Z") / 1000;
  assert.deepEqual(brasiliaFromUnix(eighteenUtc), { time: "15:00:00", date: "01/10/2026" });
  assert.equal(formatChartCrosshair(eighteenUtc), "01/10/2026 15:00");
  assert.equal(formatChartTick(eighteenUtc, 3), "15:00");
  assert.equal(formatChartTick(eighteenUtc, 4), "15:00:00");
  assert.equal(formatChartTick(eighteenUtc, 2), "01/10");

  const afterMidnightUtc = Date.parse("2026-10-01T02:15:00Z") / 1000;
  assert.equal(formatChartCrosshair(afterMidnightUtc), "30/09/2026 23:15");
  assert.equal(formatChartTick(afterMidnightUtc, 0), "2026");
  assert.equal(formatChartTick(afterMidnightUtc, 1), "09/2026");
});

test("only live statuses are cancellable", () => {
  assert.equal(isCancellable("created"), true);
  assert.equal(isCancellable("accepted"), true);
  assert.equal(isCancellable("partially_filled"), true);
  assert.equal(isCancellable("filled"), false);
  assert.equal(isCancellable("cancelled"), false);
  assert.equal(isCancellable("rejected"), false);
  assert.equal(statusLabels.partially_filled, "Parcial");
});
