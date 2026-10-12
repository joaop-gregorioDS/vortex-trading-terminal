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
  // This slice accepts exact whole-minute UTC values; non-zero subsecond timestamps are rejected.
  if (typeof value !== "string" || !/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:00(?:\.0+)?Z$/.test(value)) {
    throw new Error("Unsupported chart timestamp precision.");
  }
  const milliseconds = Date.parse(value);
  if (!Number.isFinite(milliseconds)) {
    throw new Error("Invalid chart timestamp.");
  }
  const iso = new Date(milliseconds).toISOString();
  const normalizedValue = value.replace(/\.0+Z$/, "Z");
  if (iso.replace(/\.0+Z$/, "Z") !== normalizedValue) {
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

const INTERVAL_SECONDS: Record<string, number> = {
  "1m": 60,
  "5m": 300,
  "15m": 900,
  "30m": 1800,
  "60m": 3600,
  "1h": 3600,
  "4h": 14400,
  "1d": 86400,
  "1D": 86400,
};

export function adaptSnapshot(snapshot: ChartSnapshot) {
  const expectedSeconds = INTERVAL_SECONDS[snapshot.context.interval];
  if (!expectedSeconds || snapshot.context.barCount !== snapshot.candles.length) {
    throw new Error("Invalid chart context.");
  }
  const candles: CandlestickData<UTCTimestamp>[] = snapshot.candles.map((candle) => {
    const time = closeTimeForDrawing(candle.closeTime);
    if (time - closeTimeForDrawing(candle.openTime) !== expectedSeconds) throw new Error("Invalid candle interval.");
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
  const bollinger = snapshot.indicators.find((indicator) => indicator.id === "bollinger-bands");
  const rsi = snapshot.indicators.find((indicator) => indicator.id === "rsi");
  const vwap = snapshot.indicators.find((indicator) => indicator.id === "vwap");

  const bollingerUpper = bollinger?.series.find((s) => s.name === "Upper");
  const bollingerMiddle = bollinger?.series.find((s) => s.name === "Middle");
  const bollingerLower = bollinger?.series.find((s) => s.name === "Lower");
  const rsiSeries = rsi?.series.find((s) => s.name === "Value");
  const vwapSeries = vwap?.series.find((s) => s.name === "Value");

  return {
    candles,
    sma: convertSeries(sma.series[0].points),
    macd: convertSeries(macd.series[0].points),
    signal: convertSeries(macd.series[1].points),
    histogram: convertSeries(macd.series[2].points),
    bollinger: {
      upper: bollingerUpper ? convertSeries(bollingerUpper.points) : [],
      middle: bollingerMiddle ? convertSeries(bollingerMiddle.points) : [],
      lower: bollingerLower ? convertSeries(bollingerLower.points) : [],
    },
    rsi: rsiSeries ? convertSeries(rsiSeries.points) : [],
    vwap: vwapSeries ? convertSeries(vwapSeries.points) : [],
  };
}
