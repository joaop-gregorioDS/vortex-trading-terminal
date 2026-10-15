import { describe, expect, it } from "vitest";
import { HISTORY_INTERVALS, SESSION_INTERVALS } from "./market";

describe("intervalos", () => {
  it("separa o pregão do histórico", () => {
    expect(HISTORY_INTERVALS).toEqual(["1d", "1h", "15m", "5m", "1m"]);
    expect(SESSION_INTERVALS).toContain("60m");
    expect(HISTORY_INTERVALS).not.toContain("60m");
  });
});
