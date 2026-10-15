import { readFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";

describe("logo de atribuição", () => {
  it("fica no pacote e não aponta para um site", async () => {
    const file = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../public/tradingview.svg");
    const text = await readFile(file, "utf8");
    const networked = text.replaceAll("http://www.w3.org/2000/svg", "");
    expect(text).toContain("M2 0H0v10");
    expect(networked).not.toMatch(/https?:\/\//);
    expect(text.toLowerCase()).not.toContain("tradingview.com");
  });
});
