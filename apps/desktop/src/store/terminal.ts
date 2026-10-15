import { create } from "zustand";
import type { ConnectionState } from "../bindings/ConnectionState";
import type { DesktopConfig } from "../bindings/DesktopConfig";
import type { TerminalSnapshot } from "../bindings/TerminalSnapshot";
import { instrumentFor, pickTicker } from "../header";

type Desk = {
  snapshot: TerminalSnapshot | null;
  connection: ConnectionState;
  config: DesktopConfig | null;
  ticker: string | null;
  instrumentId: string | null;
  booted: boolean;
  bootError: string | null;
  setSnapshot: (snapshot: TerminalSnapshot) => void;
  setConnection: (connection: ConnectionState) => void;
  setConfig: (config: DesktopConfig) => void;
  setTicker: (ticker: string) => void;
  setBootError: (message: string | null) => void;
};

const idleConnection: ConnectionState = {
  state: "degraded",
  sessionOpen: null,
  lastSuccessAt: null,
  retryInMs: null,
};

export const useDesk = create<Desk>((set) => ({
  snapshot: null,
  connection: idleConnection,
  config: null,
  ticker: null,
  instrumentId: null,
  booted: false,
  bootError: null,
  setSnapshot: (snapshot) =>
    set((current) => {
      const ticker = pickTicker(current.ticker, snapshot.quotes);
      return {
        snapshot,
        ticker,
        instrumentId: instrumentFor(ticker, snapshot.quotes),
      };
    }),
  setConnection: (connection) => set({ connection, booted: true }),
  setConfig: (config) => set({ config }),
  setTicker: (ticker) =>
    set((current) => {
      const quotes = current.snapshot?.quotes ?? [];
      if (!quotes.some((quote) => quote.ticker === ticker)) return current;
      return { ticker, instrumentId: instrumentFor(ticker, quotes) };
    }),
  setBootError: (bootError) => set({ bootError }),
}));
