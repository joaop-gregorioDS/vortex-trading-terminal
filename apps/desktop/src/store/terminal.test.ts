import { beforeEach, describe, expect, it } from "vitest";
import type { TerminalSnapshot } from "../bindings/TerminalSnapshot";
import { useDesk } from "./terminal";

function snapshot(quotes: Array<{ ticker: string; instrumentId: string }>): TerminalSnapshot {
  return { quotes } as TerminalSnapshot;
}

describe("seleção do ativo", () => {
  beforeEach(() => {
    useDesk.setState({ snapshot: null, ticker: null, instrumentId: null });
  });

  it("guarda o instrumento do snapshot e ignora ticker ausente", () => {
    useDesk.getState().setSnapshot(
      snapshot([
        { ticker: "VALE3", instrumentId: "id-vale" },
        { ticker: "ITUB4", instrumentId: "id-itub" },
      ]),
    );
    expect(useDesk.getState().ticker).toBe("VALE3");
    expect(useDesk.getState().instrumentId).toBe("id-vale");

    useDesk.getState().setTicker("PETR4");
    expect(useDesk.getState().ticker).toBe("VALE3");
    expect(useDesk.getState().instrumentId).toBe("id-vale");

    useDesk.getState().setTicker("ITUB4");
    expect(useDesk.getState().instrumentId).toBe("id-itub");

    useDesk.getState().setSnapshot(
      snapshot([
        { ticker: "VALE3", instrumentId: "id-vale-2" },
        { ticker: "ITUB4", instrumentId: "id-itub-2" },
      ]),
    );
    expect(useDesk.getState().ticker).toBe("ITUB4");
    expect(useDesk.getState().instrumentId).toBe("id-itub-2");
  });
});
