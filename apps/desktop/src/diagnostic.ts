import type { DiagnosticReport } from "./bindings/DiagnosticReport";
import type { Theme } from "./bindings/Theme";
import { formatDateTime, groupDecimal } from "./format";

export function themeAttribute(theme: Theme | undefined): "light" | "dark" | null {
  if (theme === "light" || theme === "dark") return theme;
  return null;
}

export function diagnosticBody(report: DiagnosticReport): string {
  if (report.health) return `${report.health.body} · HTTP ${report.health.status}`;
  const status = report.status;
  if (!status) return "";
  const price = status.lastPrice ? groupDecimal(status.lastPrice) : "—";
  return [
    `Instrumento ${status.instrumentId}`,
    `Hora simulada ${formatDateTime(status.simulatedTime)}`,
    `Último preço ${price}`,
    `Ordens em espera ${status.restingOrderCount}`,
    `Ordens ${status.orderCount}`,
    `Cheias ${status.filledOrderCount}`,
    `Abertas ${status.openOrderCount}`,
    `Recusadas ${status.rejectedOrderCount}`,
    `Execuções ${status.fillCount}`,
    `Quantidade negociada ${groupDecimal(status.tradedQuantity)}`,
    `Restauradas ${status.restoredOrderCount}`,
  ].join("\n");
}
