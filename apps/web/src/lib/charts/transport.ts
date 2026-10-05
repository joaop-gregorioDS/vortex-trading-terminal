export type SmaRequest = { id: "sma"; parameters: { period: number } };
export type MacdRequest = {
  id: "macd";
  parameters: { fastPeriod: number; slowPeriod: number; signalPeriod: number };
};
export type IndicatorRequest = SmaRequest | MacdRequest;
export type ChartSnapshotRequest = {
  instrumentId: string;
  interval: "1m";
  maxBars: number;
  indicators: IndicatorRequest[];
};
export type CandleTransport = {
  openTime: string;
  closeTime: string;
  open: string;
  high: string;
  low: string;
  close: string;
  volume: string;
};
export type PointTransport = { time: string; value: string };
export type SeriesTransport = { name: string; points: PointTransport[] };
export type IndicatorTransport = IndicatorRequest & { series: SeriesTransport[] };
export type ChartSnapshot = {
  context: {
    instrumentId: string;
    interval: string;
    sourceId: string;
    isDemo: boolean;
    barCount: number;
  };
  candles: CandleTransport[];
  indicators: IndicatorTransport[];
};

export const demoRequest: ChartSnapshotRequest = {
  instrumentId: "11111111-1111-1111-1111-111111111111",
  interval: "1m",
  maxBars: 200,
  indicators: [
    { id: "sma", parameters: { period: 20 } },
    { id: "macd", parameters: { fastPeriod: 12, slowPeriod: 26, signalPeriod: 9 } },
  ],
};
