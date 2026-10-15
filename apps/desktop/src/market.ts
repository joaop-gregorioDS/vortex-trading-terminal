import type { HistoryInterval } from "./bindings/HistoryInterval";

export const SESSION_INTERVALS = ["1m", "5m", "15m", "60m", "1h"] as const;
export const HISTORY_INTERVALS = ["1d", "1h", "15m", "5m", "1m"] as const satisfies readonly HistoryInterval[];

export type SessionInterval = (typeof SESSION_INTERVALS)[number];
