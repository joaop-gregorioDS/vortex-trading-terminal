import { connection } from "next/server";
import { HistoricalChart } from "@/components/historical-chart";
import { fetchDemoSnapshot } from "@/lib/charts/fetch-snapshot";

export default async function Home() {
  // Exclude API acquisition from build-time prerendering.
  await connection();
  const snapshot = await fetchDemoSnapshot().catch(() => null);
  return (
    <main className="min-h-screen bg-[#070d17] px-4 py-6 text-slate-100 sm:px-8 sm:py-8">
      <div className="mx-auto max-w-7xl">
        <header className="mb-10 flex items-center justify-between border-b border-slate-800 pb-5">
          <div><p className="text-xl font-semibold tracking-[0.18em]">VORTEX</p><p className="mt-1 text-[10px] tracking-[0.25em] text-slate-500">TRADING TERMINAL</p></div>
          <span className="rounded-full border border-amber-400/20 bg-amber-400/5 px-3 py-1 text-[11px] tracking-wider text-amber-300">DADOS DEMO</span>
        </header>
        <div className="mb-6"><p className="text-xs tracking-[0.16em] text-slate-500">VISUALIZAÇÃO DE MERCADO</p>
          <h1 className="mt-2 text-2xl font-medium sm:text-3xl">Histórico de preço</h1>
          <p className="mt-2 text-sm text-slate-400">Candles de um minuto com SMA e MACD. Uma leitura estática de dados demonstrativos.</p>
        </div>
        {snapshot ? <HistoricalChart snapshot={snapshot} /> : <section role="alert" className="rounded-xl border border-slate-800 bg-[#0a111e] p-8">
          <h2 className="font-medium">Histórico indisponível</h2><p className="mt-2 text-sm text-slate-400">Não foi possível carregar os dados demonstrativos.</p>
          <form action="/" method="get"><button className="mt-5 rounded-lg border border-slate-700 px-4 py-2 text-sm">Tentar novamente</button></form>
        </section>}
        <footer className="mt-5 flex flex-wrap justify-between gap-2 text-xs text-slate-600"><span>Vortex Software</span><span>Histórico determinístico de demonstração</span></footer>
      </div>
    </main>
  );
}
