"use client";

import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from "react";
import { formatBrasilia } from "./format";
import {
  emergencyFlatten,
  fetchTerminal,
  flattenPosition,
  resetSession,
  type ClosedTradeTransport,
  type MarketQuoteTransport,
  type OrderTransport,
  type PerformanceReportTransport,
  type PortfolioTransport,
} from "./transport";

export type NavTab = "visao-geral" | "operar" | "relatorios" | "historico";

export type ChartTab = {
  id: string;
  ticker: string;
  timeframe: string;
};

export type IndicatorSettings = {
  sma: boolean;
  bollinger: boolean;
  vwap: boolean;
  rsi: boolean;
};

type TerminalContextType = {
  activeNav: NavTab;
  setActiveNav: (tab: NavTab) => void;
  activeTicker: string;
  setActiveTicker: (ticker: string) => void;
  chartTabs: ChartTab[];
  activeChartTabId: string;
  selectChartTab: (id: string) => void;
  closeChartTab: (id: string) => void;
  addChartTab: (ticker: string, timeframe?: string) => void;
  setTimeframeForActiveTab: (tf: string) => void;
  marketDrawerOpen: boolean;
  setMarketDrawerOpen: (open: boolean | ((prev: boolean) => boolean)) => void;
  bookDrawerOpen: boolean;
  setBookDrawerOpen: (open: boolean | ((prev: boolean) => boolean)) => void;
  simulatedTime: string | null;
  sessionOpen: boolean | null;
  currentTimeStr: string;
  currentDateStr: string;
  connectionError: string | null;
  marketQuotes: MarketQuoteTransport[];
  orders: OrderTransport[];
  portfolio: PortfolioTransport | null;
  performance: PerformanceReportTransport | null;
  closedTrades: ClosedTradeTransport[];
  activeQuote: MarketQuoteTransport | null;
  filterTicker: string;
  setFilterTicker: (ticker: string) => void;
  indicatorSettings: IndicatorSettings;
  toggleIndicator: (indicator: keyof IndicatorSettings) => void;
  emergencyFlattenPositions: () => Promise<{ ok: boolean; message: string }>;
  closePositionByTicker: (ticker: string) => Promise<boolean>;
  resetDemonstration: () => Promise<boolean>;
  refreshAll: () => Promise<void>;
  /** Price the chart click asked the ticket to show, already a decimal string. */
  chartPriceDraft: { ticker: string; price: string } | null;
  setChartPriceDraft: (ticker: string, price: string) => void;
};

const TerminalContext = createContext<TerminalContextType | null>(null);

const DEFAULT_CHART_TABS: ChartTab[] = [
  { id: "tab-1", ticker: "WING26", timeframe: "15m" },
  { id: "tab-2", ticker: "WDOU26", timeframe: "15m" },
  { id: "tab-3", ticker: "PETR4", timeframe: "5m" },
];

export function TerminalProvider({ children }: { children: ReactNode }) {
  const [activeNav, setActiveNav] = useState<NavTab>("visao-geral");
  const [chartTabs, setChartTabs] = useState<ChartTab[]>(DEFAULT_CHART_TABS);
  const [activeChartTabId, setActiveChartTabId] = useState<string>("tab-3");
  const [marketDrawerOpen, setMarketDrawerOpen] = useState(false);
  const [bookDrawerOpen, setBookDrawerOpen] = useState(false);
  const [filterTicker, setFilterTicker] = useState("all");
  const [indicatorSettings, setIndicatorSettings] = useState<IndicatorSettings>({
    sma: true,
    bollinger: false,
    vwap: true,
    rsi: true,
  });

  const [simulatedTime, setSimulatedTime] = useState<string | null>(null);
  const [sessionOpen, setSessionOpen] = useState<boolean | null>(null);
  const [connectionError, setConnectionError] = useState<string | null>(null);
  const [marketQuotes, setMarketQuotes] = useState<MarketQuoteTransport[]>([]);
  const [orders, setOrders] = useState<OrderTransport[]>([]);
  const [portfolio, setPortfolio] = useState<PortfolioTransport | null>(null);
  const [performance, setPerformance] = useState<PerformanceReportTransport | null>(null);
  const [closedTrades, setClosedTrades] = useState<ClosedTradeTransport[]>([]);
  const [chartPriceDraft, setChartPriceDraftState] = useState<{ ticker: string; price: string } | null>(null);
  const setChartPriceDraft = useCallback((ticker: string, price: string) => {
    setChartPriceDraftState({ ticker, price });
  }, []);

  const activeTab = useMemo(
    () => chartTabs.find((tab) => tab.id === activeChartTabId) ?? chartTabs[0],
    [chartTabs, activeChartTabId],
  );
  const activeTicker = activeTab ? activeTab.ticker : "PETR4";
  const activeQuote = useMemo(
    () => marketQuotes.find((quote) => quote.ticker === activeTicker) ?? null,
    [marketQuotes, activeTicker],
  );
  const brasilia = formatBrasilia(simulatedTime);

  const clearSession = useCallback((error: string) => {
    setConnectionError(error);
    setSimulatedTime(null);
    setSessionOpen(null);
    setMarketQuotes([]);
    setOrders([]);
    setPortfolio(null);
    setPerformance(null);
    setClosedTrades([]);
  }, []);

  const refreshAll = useCallback(async () => {
    const snapshot = await fetchTerminal();
    if (!snapshot.ok) {
      clearSession(snapshot.error);
      return;
    }

    setConnectionError(null);
    setSimulatedTime(snapshot.value.simulatedTime);
    setSessionOpen(snapshot.value.sessionOpen ?? null);
    setMarketQuotes(snapshot.value.quotes);
    setOrders(snapshot.value.orders);
    setPortfolio(snapshot.value.portfolio);
    setPerformance(snapshot.value.performance);
    setClosedTrades(snapshot.value.closedTrades);
  }, [clearSession]);

  useEffect(() => {
    let active = true;
    const poll = async () => {
      if (active) await refreshAll();
    };
    void poll();
    const timer = setInterval(poll, 1500);
    return () => {
      active = false;
      clearInterval(timer);
    };
  }, [refreshAll]);

  const toggleIndicator = useCallback((indicator: keyof IndicatorSettings) => {
    setIndicatorSettings((prev) => ({ ...prev, [indicator]: !prev[indicator] }));
  }, []);

  const setActiveTicker = useCallback((ticker: string) => {
    setChartTabs((prev) => {
      const existing = prev.find((tab) => tab.ticker === ticker);
      if (existing) {
        setActiveChartTabId(existing.id);
        return prev;
      }
      const newTab: ChartTab = { id: `tab-${Date.now()}`, ticker, timeframe: "5m" };
      setActiveChartTabId(newTab.id);
      return [...prev, newTab];
    });
  }, []);

  const selectChartTab = useCallback((id: string) => {
    setActiveChartTabId(id);
  }, []);

  const closeChartTab = useCallback(
    (id: string) => {
      setChartTabs((prev) => {
        if (prev.length <= 1) return prev;
        const next = prev.filter((tab) => tab.id !== id);
        if (activeChartTabId === id && next.length > 0) {
          setActiveChartTabId(next[0].id);
        }
        return next;
      });
    },
    [activeChartTabId],
  );

  const addChartTab = useCallback((ticker: string, timeframe = "5m") => {
    const id = `tab-${Date.now()}`;
    setChartTabs((prev) => [...prev, { id, ticker, timeframe }]);
    setActiveChartTabId(id);
  }, []);

  const setTimeframeForActiveTab = useCallback(
    (timeframe: string) => {
      setChartTabs((prev) =>
        prev.map((tab) => (tab.id === activeChartTabId ? { ...tab, timeframe } : tab)),
      );
    },
    [activeChartTabId],
  );

  const emergencyFlattenPositions = useCallback(async () => {
    const result = await emergencyFlatten();
    await refreshAll();
    if (result.ok) {
      return {
        ok: true,
        message: `Cancelamento e zeragem enviados: ${result.value.flattenedPositions} posições e ${result.value.cancelledOrders} ordens canceladas. A zeragem executa no próximo print.`,
      };
    }
    return { ok: false, message: result.error };
  }, [refreshAll]);

  const resetDemonstration = useCallback(async () => {
    const result = await resetSession();
    if (!result.ok) {
      clearSession(result.error);
      return false;
    }

    await refreshAll();
    return true;
  }, [clearSession, refreshAll]);

  const closePositionByTicker = useCallback(
    async (ticker: string) => {
      const quote = marketQuotes.find((item) => item.ticker === ticker);
      const position = portfolio?.positions.find(
        (item) => item.instrumentId === quote?.instrumentId && !item.isClosed,
      );
      if (!quote || !position) return false;

      const result = await flattenPosition(position.instrumentId);
      await refreshAll();
      return result.ok && result.value.status === "sent" && result.value.order?.status !== "rejected";
    },
    [marketQuotes, portfolio, refreshAll],
  );

  return (
    <TerminalContext.Provider
      value={{
        activeNav,
        setActiveNav,
        activeTicker,
        setActiveTicker,
        chartTabs,
        activeChartTabId,
        selectChartTab,
        closeChartTab,
        addChartTab,
        setTimeframeForActiveTab,
        marketDrawerOpen,
        setMarketDrawerOpen,
        bookDrawerOpen,
        setBookDrawerOpen,
        simulatedTime,
        sessionOpen,
        currentTimeStr: brasilia.time,
        currentDateStr: brasilia.date,
        connectionError,
        marketQuotes,
        orders,
        portfolio,
        performance,
        closedTrades,
        activeQuote,
        filterTicker,
        setFilterTicker,
        indicatorSettings,
        toggleIndicator,
        emergencyFlattenPositions,
        closePositionByTicker,
        resetDemonstration,
        refreshAll,
        chartPriceDraft,
        setChartPriceDraft,
      }}
    >
      {children}
    </TerminalContext.Provider>
  );
}

export function useTerminal() {
  const context = useContext(TerminalContext);
  if (!context) {
    throw new Error("useTerminal must be used within a TerminalProvider");
  }
  return context;
}
