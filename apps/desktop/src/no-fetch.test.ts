import { readdir, readFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";

const sourceRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)));
const needles = [
  ["fe", "tch("].join(""),
  ["XMLHttp", "Request"].join(""),
  ["vortexsoftware", ".tech"].join(""),
  ["127.0.0.1", ":5187"].join(""),
  ["VITE", "_"].join(""),
];

async function files(dir: string): Promise<string[]> {
  const entries = await readdir(dir, { withFileTypes: true });
  const found: string[] = [];
  for (const entry of entries) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) found.push(...(await files(full)));
    else if (/\.(ts|tsx)$/.test(entry.name) && !entry.name.endsWith(".test.ts")) found.push(full);
  }
  return found;
}

describe("webview", () => {
  it("não chama a API e não carrega a URL base", async () => {
    const paths = await files(sourceRoot);
    expect(paths.length).toBeGreaterThan(0);
    for (const file of paths) {
      const text = await readFile(file, "utf8");
      for (const needle of needles) {
        expect(text.includes(needle), `${path.basename(file)} contém ${needle}`).toBe(false);
      }
    }
  });
});
