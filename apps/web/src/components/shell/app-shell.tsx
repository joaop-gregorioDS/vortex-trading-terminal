"use client";

import { useState, type ReactNode } from "react";
import { useTerminal } from "@/lib/trading/terminal-context";
import { leaveDemo } from "@/lib/page-preferences";
import { formatBrl } from "@/lib/trading/format";
import { PageTools } from "@/components/page-tools";
import {
  IconBarChart,
  IconCalendar,
  IconExit,
  IconHome,
  IconMarket,
  IconShield,
  IconTrade,
  IconUser,
} from "@/components/ui/icons";

export function AppShell({ children }: { children: ReactNode }) {
  const {
    activeNav,
    setActiveNav,
    activeTicker,
    setMarketDrawerOpen,
    performance,
    connectionError,
    currentTimeStr,
    currentDateStr,
    resetDemonstration,
  } = useTerminal();

  const [activeMenu, setActiveMenu] = useState<string | null>(null);
  const saldoDaytrade = performance ? formatBrl(performance.currentBalance) : "—";
  const patrimonioDaytrade = performance ? formatBrl(performance.equity) : "—";

  return (
    <div className="flex h-full w-full flex-col overflow-hidden bg-[#f8f6f0] font-sans text-stone-900 antialiased select-none">
      {/* 1. Desktop Application Menu Bar */}
      <div className="flex h-7 w-full shrink-0 items-center justify-between border-b border-[#e5e1d7] bg-[#f2efe7] px-3 text-[11px] text-[#4d483f]">
        <div className="flex items-center gap-1">
          {["Arquivo", "Exibir", "Inserir", "Gráficos", "Ferramentas", "Janela", "Ajuda"].map(
            (item) => (
              <button
                key={item}
                type="button"
                onClick={() => setActiveMenu(activeMenu === item ? null : item)}
                className={`rounded px-2 py-0.5 transition-colors ${
                  activeMenu === item ? "bg-[#ded8cb] text-stone-900 font-medium" : "hover:bg-[#e6e2d8]"
                }`}
              >
                {item}
              </button>
            )
          )}
        </div>
        <div className="flex items-center gap-1.5 text-[#5e574c]">
          <IconUser className="h-3.5 w-3.5" />
          <span className="font-medium text-[11px]">Convidado</span>
          <span className="text-[10px] text-stone-500">(Trader Demo)</span>
        </div>
      </div>

      {/* 2. Top Header Bar */}
      <header className="flex h-14 w-full shrink-0 items-center justify-between border-b border-[#e5e1d7] bg-white px-4">
        {/* Left: Brand + Active Asset */}
        <div className="flex items-center gap-3">
          <div className="flex items-baseline gap-2">
            <span className="text-base font-bold tracking-tight text-[#1e1b18]">
              Investimentos Pro
            </span>
            <span className="text-[11px] font-normal text-stone-400">v4.0.26</span>
          </div>

          <button
            type="button"
            onClick={() => {
              setActiveNav("operar");
              setMarketDrawerOpen((prev) => !prev);
            }}
            title="Clique para alternar ativo na Observação do Mercado"
            className="flex items-center gap-1.5 rounded-md border border-[#e2ddd1] bg-[#f9f8f5] px-2.5 py-1 text-xs font-semibold text-[#3d3830] transition hover:border-amber-400 hover:bg-amber-50/50"
          >
            <span className="text-stone-500 font-normal">Ativo:</span>
            <span className="text-stone-900 font-bold">{activeTicker}</span>
          </button>
        </div>

        {/* Right: Session clock + Balances + Badges */}
        <div className="flex items-center gap-5">
          <div className="flex flex-col text-right leading-tight">
            <span className="text-[9px] font-semibold tracking-wider text-stone-500">
              PREGÃO SIMULADO
            </span>
            <span className="text-xs font-bold text-stone-900">
              {currentTimeStr} <span className="font-normal text-stone-500">{currentDateStr}</span>
            </span>
          </div>

          {/* Saldo Daytrade */}
          <div className="flex flex-col text-right leading-tight">
            <span className="text-[9px] font-semibold tracking-wider text-stone-500">
              SALDO DAY TRADE
            </span>
            <span className="text-xs font-bold text-stone-900">{saldoDaytrade}</span>
          </div>

          {/* Patrimônio Daytrade */}
          <div className="flex flex-col text-right leading-tight">
            <span className="text-[9px] font-semibold tracking-wider text-stone-500">
              PATRIMÔNIO DAY TRADE
            </span>
            <span className="text-xs font-bold text-[#0d6838]">{patrimonioDaytrade}</span>
          </div>

          {/* Simulação Badge */}
          <div className="flex items-center gap-1 rounded-full border border-emerald-400/40 bg-emerald-50 px-2.5 py-0.5 text-[11px] font-medium text-emerald-800">
            <IconShield className="h-3 w-3 text-emerald-600" />
            <span>Simulação</span>
          </div>
          <PageTools />
        </div>
      </header>

      {/* 3. Main Workspace: Sidebar + Content */}
      <div className="flex min-h-0 flex-1 overflow-hidden">
        {/* Left Sidebar */}
        <aside className="flex w-36 shrink-0 flex-col justify-between border-r border-[#e5e1d7] bg-[#f5f2e9] p-2 text-xs">
          <nav className="flex flex-col gap-1">
            <button
              type="button"
              onClick={() => setActiveNav("visao-geral")}
              className={`flex items-center gap-2.5 rounded-lg px-3 py-2 text-left font-medium transition ${
                activeNav === "visao-geral"
                  ? "bg-[#e5dcce] text-stone-900 shadow-xs"
                  : "text-stone-600 hover:bg-[#ebe5d8] hover:text-stone-900"
              }`}
            >
              <IconHome className="h-4 w-4 shrink-0 text-stone-700" />
              <span>Visão Geral</span>
            </button>

            <button
              type="button"
              onClick={() => setActiveNav("operar")}
              className={`flex items-center gap-2.5 rounded-lg px-3 py-2 text-left font-medium transition ${
                activeNav === "operar"
                  ? "bg-[#e5dcce] text-stone-900 shadow-xs"
                  : "text-stone-600 hover:bg-[#ebe5d8] hover:text-stone-900"
              }`}
            >
              <IconTrade className="h-4 w-4 shrink-0 text-stone-700" />
              <span>Operar</span>
            </button>

            <button
              type="button"
              onClick={() => setActiveNav("relatorios")}
              className={`flex items-center gap-2.5 rounded-lg px-3 py-2 text-left font-medium transition ${
                activeNav === "relatorios"
                  ? "bg-[#e5dcce] text-stone-900 shadow-xs"
                  : "text-stone-600 hover:bg-[#ebe5d8] hover:text-stone-900"
              }`}
            >
              <IconBarChart className="h-4 w-4 shrink-0 text-stone-700" />
              <span>Relatórios</span>
            </button>

            <button
              type="button"
              onClick={() => setActiveNav("historico")}
              className={`flex items-center gap-2.5 rounded-lg px-3 py-2 text-left font-medium transition ${
                activeNav === "historico"
                  ? "bg-[#e5dcce] text-stone-900 shadow-xs"
                  : "text-stone-600 hover:bg-[#ebe5d8] hover:text-stone-900"
              }`}
            >
              <IconCalendar className="h-4 w-4 shrink-0 text-stone-700" />
              <span>Histórico</span>
            </button>

            <button
              type="button"
              onClick={() => {
                setActiveNav("operar");
                setMarketDrawerOpen(true);
              }}
              className="flex items-center gap-2.5 rounded-lg px-3 py-2 text-left font-medium text-stone-600 transition hover:bg-[#ebe5d8] hover:text-stone-900"
            >
              <IconMarket className="h-4 w-4 shrink-0 text-stone-700" />
              <span>Mercado</span>
            </button>
          </nav>

          <div className="border-t border-[#e5e1d7] pt-2">
            <button
              type="button"
              onClick={() => {
                if (
                  window.confirm(
                    "Reiniciar a demonstração apaga as ordens e posições desta conta compartilhada. Continuar?",
                  )
                ) {
                  void resetDemonstration();
                }
              }}
              className="flex w-full items-center justify-center gap-1.5 rounded-md border border-rose-200 bg-white px-2 py-1.5 text-center text-[11px] font-semibold leading-tight text-rose-600 transition hover:bg-rose-50"
            >
              <IconExit className="h-3.5 w-3.5 shrink-0" />
              <span>Reiniciar demonstração</span>
            </button>
            <button
              type="button"
              onClick={leaveDemo}
              className="mt-1 flex w-full items-center justify-center rounded-md px-2 py-1.5 text-[11px] font-semibold text-stone-600 transition hover:bg-white hover:text-stone-900"
            >
              Sair
            </button>
          </div>
        </aside>

        {/* Content View */}
        <main className="flex min-h-0 flex-1 flex-col overflow-hidden bg-[#f8f6f0]">
          <div className="shrink-0 border-b border-amber-200 bg-amber-50 px-4 py-1.5 text-[11px] font-medium text-amber-950">
            Conta compartilhada de demonstração. O pregão é simulado e as ordens são as mesmas para todos os visitantes.
          </div>
          {connectionError && (
            <div className="shrink-0 border-b border-rose-200 bg-rose-50 px-4 py-2 text-xs font-medium text-rose-800">
              API indisponível: {connectionError} O saldo não é estimado enquanto a sessão não responde.
            </div>
          )}
          <div className="min-h-0 flex-1 overflow-y-auto p-3 sm:p-6">{children}</div>
        </main>
      </div>
    </div>
  );
}
