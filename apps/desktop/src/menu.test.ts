import { describe, expect, it } from "vitest";
import { viewForMenu, wantsExport } from "./menu";

describe("menu da estação", () => {
  it("abre a visão e só exporta pelo item de arquivo", () => {
    expect(viewForMenu("view-overview")).toBe("overview");
    expect(viewForMenu("view-desk")).toBe("desk");
    expect(viewForMenu("view-reports")).toBe("reports");
    expect(viewForMenu("export-trades")).toBe("reports");
    expect(viewForMenu("danger-reset")).toBeNull();
    expect(wantsExport("export-trades")).toBe(true);
    expect(wantsExport("view-reports")).toBe(false);
    expect(wantsExport("danger-reverse")).toBe(false);
  });
});
