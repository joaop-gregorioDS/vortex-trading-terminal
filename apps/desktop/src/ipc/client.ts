import { invoke } from "@tauri-apps/api/core";
import type { ChartBody } from "../bindings/ChartBody";
import type { ClosedTrade } from "../bindings/ClosedTrade";
import type { CsvExport } from "../bindings/CsvExport";
import type { DesktopConfig } from "../bindings/DesktopConfig";
import type { HealthText } from "../bindings/HealthText";
import type { StatusResponse } from "../bindings/StatusResponse";
import type { ChartPreview } from "../bindings/ChartPreview";
import type { ChartPreviewRequest } from "../bindings/ChartPreviewRequest";
import type { DangerAction } from "../bindings/DangerAction";
import type { DangerToken } from "../bindings/DangerToken";
import type { EmergencyFlat } from "../bindings/EmergencyFlat";
import type { Order } from "../bindings/Order";
import type { PerformanceReport } from "../bindings/PerformanceReport";
import type { OrderRequest } from "../bindings/OrderRequest";
import type { PositionAction } from "../bindings/PositionAction";
import type { ProtectionLevelRequest } from "../bindings/ProtectionLevelRequest";
import type { ResetAck } from "../bindings/ResetAck";
import type { TerminalSnapshot } from "../bindings/TerminalSnapshot";
import type { Theme } from "../bindings/Theme";
import type { YahooHistory } from "../bindings/YahooHistory";

export function insideDesktop(): boolean {
  return typeof window !== "undefined" && "__TAURI_INTERNALS__" in window;
}

export function loadTerminal(): Promise<TerminalSnapshot> {
  return invoke<TerminalSnapshot>("get_terminal");
}

export function loadChart(ticker: string, interval: string): Promise<ChartBody> {
  return invoke<ChartBody>("get_chart", { ticker, interval, maxBars: 200 });
}

export function loadHistory(ticker: string, interval: string): Promise<YahooHistory> {
  return invoke<YahooHistory>("get_history", { ticker, interval, maxBars: 180 });
}

export function submitOrder(request: OrderRequest): Promise<Order> {
  return invoke<Order>("submit_order", { request });
}

export function previewChart(request: ChartPreviewRequest): Promise<ChartPreview> {
  return invoke<ChartPreview>("chart_preview", { request });
}

export function reviseOrder(orderId: string, price: string): Promise<Order> {
  return invoke<Order>("revise_order", { orderId, price });
}

export function cancelOrder(orderId: string): Promise<Order> {
  return invoke<Order>("cancel_order", { orderId });
}

export function placeProtection(request: ProtectionLevelRequest): Promise<Order> {
  return invoke<Order>("place_protection", { request });
}

export function flattenPosition(instrumentId: string): Promise<PositionAction> {
  return invoke<PositionAction>("flatten", { instrumentId });
}

export function reversePosition(instrumentId: string, dangerToken: string): Promise<PositionAction> {
  return invoke<PositionAction>("reverse", { instrumentId, dangerToken });
}

export function emergencyFlat(dangerToken: string): Promise<EmergencyFlat> {
  return invoke<EmergencyFlat>("emergency_flat", { dangerToken });
}

export function resetSession(dangerToken: string): Promise<ResetAck> {
  return invoke<ResetAck>("reset_session", { dangerToken });
}

export function loadPerformance(): Promise<PerformanceReport> {
  return invoke<PerformanceReport>("get_performance");
}

export function loadTrades(ticker: string | null): Promise<ClosedTrade[]> {
  return invoke<ClosedTrade[]>("get_trades", { ticker });
}

export function exportTrades(ticker: string | null): Promise<CsvExport> {
  return invoke<CsvExport>("export_trades_csv", { ticker });
}

export function confirmDanger(action: DangerAction): Promise<DangerToken> {
  return invoke<DangerToken>("confirm_danger", { action });
}

export function loadConfig(): Promise<DesktopConfig> {
  return invoke<DesktopConfig>("get_config");
}

export function saveTheme(theme: Theme): Promise<DesktopConfig> {
  return invoke<DesktopConfig>("set_theme", { theme });
}

export function loadHealth(): Promise<HealthText> {
  return invoke<HealthText>("health_live");
}

export function loadReady(): Promise<HealthText> {
  return invoke<HealthText>("health_ready");
}

export function loadStatus(): Promise<StatusResponse> {
  return invoke<StatusResponse>("get_status");
}

export function readError(error: unknown): string {
  if (typeof error === "string" && error.trim()) return error;
  if (error && typeof error === "object" && "title" in error) {
    const title = (error as { title?: unknown }).title;
    if (typeof title === "string" && title.trim()) return title;
  }
  return "A operação não foi concluída.";
}
