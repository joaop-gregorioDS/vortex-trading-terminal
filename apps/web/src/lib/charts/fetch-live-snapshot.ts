import "server-only";
import type { ChartSnapshot } from "./transport";

/**
 * Loads the session's own price history.
 *
 * The demonstration fixture only exists in Development, so the hosted terminal reads the live
 * simulated market here instead. Returns null while the session has not produced prices yet, which is
 * an empty chart rather than a failure.
 */
export async function fetchLiveSnapshot(): Promise<ChartSnapshot | null> {
  const baseUrl = process.env.VORTEX_API_BASE_URL
    ?? (process.env.NODE_ENV === "development" ? "http://127.0.0.1:5187" : undefined);
  if (!baseUrl) throw new Error("Trading API configuration is required.");

  const response = await fetch(new URL("/api/chart", baseUrl), {
    headers: { Accept: "application/json" },
    cache: "no-store",
    signal: AbortSignal.timeout(5000),
  });
  if (!response.ok) throw new Error("Live chart is unavailable.");

  const payload: unknown = await response.json();
  if (!isChartSnapshot(payload)) return null;

  // The API also carries its source, so a client can tell the live session's chart from the
  // demonstration fixture when both exist.
  return payload;
}

/** Narrows the payload structurally: a snapshot has candles, an empty marker does not. */
function isChartSnapshot(value: unknown): value is ChartSnapshot {
  if (typeof value !== "object" || value === null) return false;
  const candidate = value as { candles?: unknown; context?: { sourceId?: unknown } };
  return Array.isArray(candidate.candles) && typeof candidate.context?.sourceId === "string";
}
