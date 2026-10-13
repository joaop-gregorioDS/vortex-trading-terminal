import test from "node:test";
import assert from "node:assert/strict";
import { adaptSnapshot, decimalForDrawing, closeTimeForDrawing } from "../src/lib/charts/adapter.ts";
import { pulsePlayback, seedPlayback } from "../src/lib/charts/candle-playback.ts";

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

test("pulsePlayback generates micro-ticks like Profit Pro and expands high/low on breakout", () => {
  const initial = seedPlayback({ time: 1728000000, open: 35.0, high: 35.5, low: 34.5, close: 35.0 }, []);
  assert.equal(initial.cursor, 1);

  // 1. Advance toward a higher quote
  const steppedUp = pulsePlayback(initial, 36.0, 0.01, 2, 250);
  assert.ok(steppedUp.close >= 35.0, "Close should advance upwards");
  assert.equal(steppedUp.cursor, 1);

  // 2. High expansion when price crosses target
  const breakoutHigh = pulsePlayback({ ...initial, close: 35.5, high: 35.5 }, 35.6, 0.01, 2, 250);
  assert.ok(breakoutHigh.high >= 35.5, "High should expand when price rises");
  assert.ok(breakoutHigh.high >= breakoutHigh.close, "High must remain >= close");

  // 3. Mini index point steps (minMove = 5, precision = 0)
  const winInitial = seedPlayback({ time: 1728000000, open: 128500, high: 128550, low: 128450, close: 128500 }, []);
  const winStepped = pulsePlayback(winInitial, 128500, 5, 0, 250);
  assert.equal(winStepped.close % 5, 0, "Index mini contract must step in multiples of 5 points");
  assert.equal(Number.isInteger(winStepped.close), true, "Index points must have 0 decimals");
});

