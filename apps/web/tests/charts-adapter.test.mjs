import test from "node:test";
import assert from "node:assert/strict";
import { adaptSnapshot, decimalForDrawing, closeTimeForDrawing } from "../src/lib/charts/adapter.ts";

const time = (minute) => `2026-10-01T10:${String(minute).padStart(2, "0")}:00.0000000Z`;
const snapshot = () => ({
  context: { interval: "1m", barCount: 2 },
  candles: [0, 1].map((i) => ({ openTime: time(i), closeTime: time(i + 1), open: "10.000000000000000000000000001", high: "12", low: "9", close: "11", volume: "100" })),
  indicators: [
    { id: "sma", parameters: { period: 2 }, series: [{ name: "Value", points: [{ time: time(2), value: "10.5" }] }] },
    { id: "macd", parameters: { fastPeriod: 1, slowPeriod: 2, signalPeriod: 2 }, series: [
      { name: "MACD", points: [{ time: time(2), value: "0.25" }] }, { name: "Signal", points: [] }, { name: "Histogram", points: [] },
    ] },
  ],
});

test("drawing keeps original decimals and independent warm-ups without moving close times", () => {
  const original = snapshot();
  const before = structuredClone(original);
  const drawn = adaptSnapshot(original);
  assert.equal(drawn.candles.length, 2);
  assert.equal(drawn.sma.length, 1);
  assert.equal(drawn.signal.length, 0);
  assert.equal(drawn.histogram.length, 0);
  assert.equal(drawn.candles[1].time, drawn.sma[0].time);
  assert.equal(drawn.sma[0].time, Date.parse(time(2)) / 1000);
  assert.deepEqual(original, before);
});
test("canonical decimals are converted explicitly and malformed values rejected", () => {
  assert.equal(decimalForDrawing("-0.125"), -0.125);
  for (const value of ["NaN", "Infinity", "1e2", "1,2", "", 12]) assert.throws(() => decimalForDrawing(value));
});
test("subsecond and invalid dates are rejected rather than truncated", () => {
  for (const value of [time(1).replace(".0000000", ".0000001"), "2026-02-30T10:01:00.0000000Z", time(1).replace("Z", "+00:00")]) {
    assert.throws(() => closeTimeForDrawing(value));
  }
});
test("time collisions and out-of-order points never get deduplicated", () => {
  const input = snapshot();
  input.candles[1] = input.candles[0];
  assert.throws(() => adaptSnapshot(input));
  const points = snapshot();
  points.indicators[0].series[0].points.push(points.indicators[0].series[0].points[0]);
  assert.throws(() => adaptSnapshot(points));
});
test("indicator points outside the returned batch are rejected", () => {
  const input = snapshot();
  input.indicators[0].series[0].points[0].time = time(3);
  assert.throws(() => adaptSnapshot(input));
});
test("backend series names are checked without renaming", () => {
  const input = snapshot();
  input.indicators[0].series[0].name = "SMA";
  assert.throws(() => adaptSnapshot(input));
});
test("multi-timeframe intervals (5m, 15m, 1h, 1d) are adapted correctly", () => {
  const fiveMinTime = (idx) => `2026-10-01T10:${String(idx * 5).padStart(2, "0")}:00.0000000Z`;
  const fiveMinSnapshot = {
    context: { interval: "5m", barCount: 2 },
    candles: [0, 1].map((i) => ({
      openTime: fiveMinTime(i),
      closeTime: fiveMinTime(i + 1),
      open: "54.80",
      high: "55.10",
      low: "54.70",
      close: "55.00",
      volume: "5000",
    })),
    indicators: [
      { id: "sma", parameters: { period: 2 }, series: [{ name: "Value", points: [{ time: fiveMinTime(2), value: "54.90" }] }] },
      { id: "macd", parameters: { fastPeriod: 1, slowPeriod: 2, signalPeriod: 2 }, series: [
        { name: "MACD", points: [{ time: fiveMinTime(2), value: "0.10" }] }, { name: "Signal", points: [] }, { name: "Histogram", points: [] },
      ] },
    ],
  };
  const adapted = adaptSnapshot(fiveMinSnapshot);
  assert.equal(adapted.candles.length, 2);
  assert.equal(adapted.candles[1].time - adapted.candles[0].time, 300);
});

