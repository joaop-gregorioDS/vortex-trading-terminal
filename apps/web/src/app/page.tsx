import { connection } from "next/server";
import { HistoricalChart } from "@/components/historical-chart";
import { TradingPanel } from "@/components/trading-panel";
import { fetchLiveSnapshot } from "@/lib/charts/fetch-live-snapshot";

export default async function Home() {
  // Exclude API acquisition from build-time prerendering.
  await connection();
  const result = await fetchLiveSnapshot()
    .then((snapshot) => ({ snapshot, failed: false }))
    .catch(() => ({ snapshot: null, failed: true }));
  const { snapshot, failed } = result;

  return (
    <main className="min-h-screen bg-[#070d17] px-4 py-6 text-slate-100 sm:px-8 sm:py-8">
      <div className="mx-auto max-w-7xl">
        <header className="mb-8 flex flex-wrap items-center justify-between gap-3 border-b border-slate-800 pb-5">
          <div>
            <p className="text-xl font-semibold tracking-[0.18em]">VORTEX</p>
            <p className="mt-1 text-[10px] tracking-[0.25em] text-slate-500">TRADING TERMINAL</p>
          </div>
          <span className="rounded-full border border-amber-400/20 bg-amber-400/5 px-3 py-1 text-[11px] tracking-wider text-amber-300">
            PAPER TRADING · DADOS SIMULADOS
          </span>
        </header>

        <div className="mb-6">
          <p className="text-xs tracking-[0.16em] text-slate-500">MESA DE OPERAÇÃO</p>
          <h1 className="mt-2 text-2xl font-medium sm:text-3xl">Sessão simulada</h1>
          <p className="mt-2 max-w-3xl text-sm text-slate-400">
            Cotações geradas por simulação determinística, execução exclusivamente em Paper Trading. Sem
            recomendação de investimento e sem dado real de mercado.
          </p>
        </div>

        <TradingPanel />

        <section className="mt-10">
          <div className="mb-4">
            <p className="text-xs tracking-[0.16em] text-slate-500">VISUALIZAÇÃO DE MERCADO</p>
            <h2 className="mt-2 text-xl font-medium">Histórico da sessão</h2>
            <p className="mt-1 text-sm text-slate-400">
              Preços que a própria sessão publicou, com SMA(20) e MACD. Sem intervalo de barra
              fabricado: cada ponto é um preço efetivamente observado.
            </p>
          </div>
          {snapshot ? (
            <HistoricalChart snapshot={snapshot} />
          ) : (
            <section role="alert" className="rounded-xl border border-slate-800 bg-[#0a111e] p-8">
              <h3 className="font-medium">
                {failed ? "Histórico indisponível" : "Aguardando as primeiras cotações"}
              </h3>
              <p className="mt-2 text-sm text-slate-400">
                {failed
                  ? "Não foi possível ler os preços da sessão. A mesa de operação continua funcional."
                  : "O mercado simulado ainda não publicou preços suficientes para desenhar o gráfico."}
              </p>
              <form action="/" method="get">
                <button className="mt-5 rounded-lg border border-slate-700 px-4 py-2 text-sm">
                  Tentar novamente
                </button>
              </form>
            </section>
          )}
        </section>

        <footer className="mt-8 flex flex-wrap justify-between gap-2 border-t border-slate-800 pt-5 text-xs text-slate-600">
          <span>Vortex Software</span>
          <span>Ambiente simulado de Paper Trading</span>
        </footer>
      </div>
    </main>
  );
}
