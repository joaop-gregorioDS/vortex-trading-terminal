import type { CandlestickData, LineData, UTCTimestamp } from "lightweight-charts";
import type { ChartSnapshot, PointTransport } from "./transport.ts";

// The original snapshot retains authoritative strings. Numbers exist only in drawing data.
export function decimalForDrawing(value: string): number {
  if (typeof value !== "string" || !/^-?\d+(?:\.\d+)?$/.test(value)) {
    throw new Error("Invalid decimal transport value.");
  }
  const converted = Number(value);
  if (!Number.isFinite(converted) || (converted === 0 && /[1-9]/.test(value))) {
    throw new Error("Decimal cannot be represented for drawing.");
  }
  return converted;
}

export function closeTimeForDrawing(value: string): UTCTimestamp {
  // This slice accepts exact whole-minute UTC values; subsecond timestamps are never truncated.
  if (typeof value !== "string" || !/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:00\.0000000Z$/.test(value)) {
    throw new Error("Unsupported chart timestamp precision.");
  }
  const milliseconds = Date.parse(value);
  if (!Number.isFinite(milliseconds) || new Date(milliseconds).toISOString() !== value.replace(".0000000Z", ".000Z")) {
    throw new Error("Invalid chart timestamp.");
  }
  const seconds = milliseconds / 1000;
  if (!Number.isSafeInteger(seconds)) throw new Error("Invalid chart timestamp range.");
  return seconds as UTCTimestamp;
}

function strictlyIncreasing(times: number[]) {
  for (let index = 1; index < times.length; index++) {
    if (times[index] <= times[index - 1]) throw new Error("Chart timestamps collide or are unordered.");
  }
}

export function adaptSnapshot(snapshot: ChartSnapshot) {
  if (snapshot.context.interval !== "1m" || snapshot.context.barCount !== snapshot.candles.length) {
    throw new Error("Invalid chart context.");
  }
  const candles: CandlestickData<UTCTimestamp>[] = snapshot.candles.map((candle) => {
    const time = closeTimeForDrawing(candle.closeTime);
    if (time - closeTimeForDrawing(candle.openTime) !== 60) throw new Error("Invalid candle interval.");
    return {
      time,
      open: decimalForDrawing(candle.open),
      high: decimalForDrawing(candle.high),
      low: decimalForDrawing(candle.low),
      close: decimalForDrawing(candle.close),
    };
  });
  strictlyIncreasing(candles.map((candle) => candle.time));
  const times = new Set(candles.map((candle) => candle.time));
  const convertSeries = (points: PointTransport[]): LineData<UTCTimestamp>[] => {
    const converted = points.map((point) => ({ time: closeTimeForDrawing(point.time), value: decimalForDrawing(point.value) }));
    strictlyIncreasing(converted.map((point) => point.time));
    if (converted.some((point) => !times.has(point.time))) throw new Error("Indicator time is outside the candle batch.");
    return converted;
  };
  const sma = snapshot.indicators.find((indicator) => indicator.id === "sma");
  const macd = snapshot.indicators.find((indicator) => indicator.id === "macd");
  if (!sma || !macd || sma.series.length !== 1 || sma.series[0].name !== "Value"
    || macd.series.map((series) => series.name).join("|") !== "MACD|Signal|Histogram") {
    throw new Error("Unsupported chart series.");
  }
  return {
    candles,
    sma: convertSeries(sma.series[0].points),
    macd: convertSeries(macd.series[0].points),
    signal: convertSeries(macd.series[1].points),
    histogram: convertSeries(macd.series[2].points),
  };
}
