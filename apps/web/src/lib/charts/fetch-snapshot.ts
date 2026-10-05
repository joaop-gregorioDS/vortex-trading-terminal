import "server-only";
import { demoRequest, type ChartSnapshot } from "./transport";

export async function fetchDemoSnapshot(): Promise<ChartSnapshot> {
  const baseUrl = process.env.VORTEX_API_BASE_URL
    ?? (process.env.NODE_ENV === "development" ? "http://127.0.0.1:5187" : undefined);
  if (!baseUrl) throw new Error("Chart API configuration is required.");
  const response = await fetch(new URL("/api/chart-snapshots", baseUrl), {
    method: "POST",
    headers: { "Content-Type": "application/json", Accept: "application/json" },
    body: JSON.stringify(demoRequest),
    cache: "no-store",
    signal: AbortSignal.timeout(5000),
  });
  if (!response.ok) throw new Error("Historical snapshot is unavailable.");
  return response.json() as Promise<ChartSnapshot>;
}
