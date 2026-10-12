"use client";

import { enterDemo } from "@/lib/page-preferences";
import { PageTools } from "@/components/page-tools";
import { IconShield } from "@/components/ui/icons";

export function DemoAccountScreen() {
  return (
    <div className="relative flex h-full min-h-full items-center justify-center bg-[#f6f3ec] px-4 py-16 font-sans text-stone-900">
      <div className="pointer-events-none absolute inset-0 bg-[radial-gradient(circle_at_top,_#f4e7d4,_transparent_58%)]" />
      <div className="absolute right-4 top-4 z-10 sm:right-6 sm:top-5">
        <PageTools />
      </div>

      <section className="relative w-full max-w-[420px] rounded-[28px] bg-white px-8 py-9 shadow-[0_24px_70px_-36px_rgba(80,50,20,0.45)]">
        <div className="mx-auto flex h-14 w-14 items-center justify-center rounded-2xl bg-[#9a3412] text-white shadow-xs">
          <IconShield className="h-7 w-7" />
        </div>

        <h1 className="mt-5 text-center text-[28px] font-black tracking-tight text-[#1c1917]">
          Investimentos <span className="text-[#9a3412]">Pro</span>
        </h1>
        <p className="mt-2 text-center text-xs leading-relaxed text-stone-500">
          Pregão simulado de day trade. Uma conta, o mesmo livro para quem entra.
        </p>

        <div className="mt-4 flex justify-center">
          <span className="rounded-full border border-amber-200 bg-amber-50 px-3 py-1 text-[10px] font-bold tracking-[0.14em] text-amber-900">
            CONTA DE DEMONSTRAÇÃO
          </span>
        </div>

        <p className="mb-3 mt-7 text-center text-xs text-stone-500">Conta de um clique</p>
        <button
          type="button"
          onClick={enterDemo}
          className="flex w-full items-center gap-3 rounded-full border border-[#e7e1d6] bg-white px-3 py-2.5 text-left transition hover:border-amber-400 hover:bg-amber-50/40"
        >
          <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-[#1c1917] text-[11px] font-bold text-white">
            TD
          </span>
          <span className="min-w-0">
            <span className="block text-sm font-bold text-stone-900">Conta demo</span>
            <span className="block text-[11px] text-stone-500">Trader Demo · caixa inicial de R$ 100.000</span>
          </span>
        </button>

        <button
          type="button"
          onClick={enterDemo}
          className="mt-6 w-full rounded-full bg-[#9a3412] py-3 text-sm font-bold text-white shadow-xs transition hover:bg-[#7c2d12]"
        >
          Entrar na conta demo →
        </button>
        <p className="mt-4 text-center text-[10px] text-stone-400">Vortex Trading Terminal · demonstração</p>
      </section>
    </div>
  );
}
