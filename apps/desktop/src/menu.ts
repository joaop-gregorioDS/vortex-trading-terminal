export type StationView = "overview" | "desk" | "reports";

export function viewForMenu(id: string): StationView | null {
  if (id === "view-overview") return "overview";
  if (id === "view-desk") return "desk";
  if (id === "view-reports" || id === "export-trades") return "reports";
  return null;
}

export function wantsExport(id: string): boolean {
  return id === "export-trades";
}
