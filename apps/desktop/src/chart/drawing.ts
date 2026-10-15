import type { CandlestickData, UTCTimestamp } from "lightweight-charts";

export type DrawingCandle = {
  openTime: string;
  closeTime: string;
  open: string;
  high: string;
  low: string;
  close: string;
};

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

const INTERVAL_SECONDS: Record<string, number> = {
  "1m": 60,
  "5m": 300,
  "15m": 900,
  "60m": 3600,
  "1h": 3600,
  "1d": 86400,
};

export function adaptCandles(interval: string, barCount: number, candles: DrawingCandle[]): CandlestickData<UTCTimestamp>[] {
  const expectedSeconds = INTERVAL_SECONDS[interval];
  if (!expectedSeconds || barCount !== candles.length) {
    throw new Error("Invalid chart context.");
  }
  const drawing = candles.map((candle) => {
    const time = closeTimeForDrawing(candle.closeTime);
    if (time - closeTimeForDrawing(candle.openTime) !== expectedSeconds) {
      throw new Error("Invalid candle interval.");
    }
    return {
      time,
      open: decimalForDrawing(candle.open),
      high: decimalForDrawing(candle.high),
      low: decimalForDrawing(candle.low),
      close: decimalForDrawing(candle.close),
    };
  });
  for (let index = 1; index < drawing.length; index += 1) {
    if (drawing[index].time <= drawing[index - 1].time) {
      throw new Error("Chart timestamps collide or are unordered.");
    }
  }
  return drawing;
}

export function chartMessage(error: unknown): string {
  const text = error instanceof Error ? error.message : "";
  if (text.includes("timestamp")) return "O horário do candle não cabe no gráfico.";
  if (text.includes("decimal") || text.includes("Decimal")) return "Um preço do candle não cabe no gráfico.";
  return "A série do gráfico veio incompleta.";
}
