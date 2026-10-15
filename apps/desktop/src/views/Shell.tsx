import { useEffect, useRef, useState } from "react";
import type { DesktopConfig } from "../bindings/DesktopConfig";
import type { DiagnosticReport } from "../bindings/DiagnosticReport";
import type { Theme } from "../bindings/Theme";
import { diagnosticBody, themeAttribute } from "../diagnostic";
import { connectionLabel, headerModel, sessionLabel, type HeaderLink } from "../header";
import {
  insideDesktop,
  loadConfig,
  loadHealth,
  loadReady,
  loadStatus,
  loadTerminal,
  readError,
  saveTheme,
} from "../ipc/client";
import { viewForMenu, wantsExport } from "../menu";
import { useDesk } from "../store/terminal";
import { Desk } from "./Desk";
import { Overview } from "./Overview";
import { Reports } from "./Reports";

const THEMES: Array<[Theme, string]> = [
  ["light", "Claro"],
  ["dark", "Escuro"],
  ["system", "Sistema"],
];

export function Shell() {
  const snapshot = useDesk((state) => state.snapshot);
  const connection = useDesk((state) => state.connection);
  const config = useDesk((state) => state.config);
  const ticker = useDesk((state) => state.ticker);
  const instrumentId = useDesk((state) => state.instrumentId);
  const booted = useDesk((state) => state.booted);
  const bootError = useDesk((state) => state.bootError);
  const setSnapshot = useDesk((state) => state.setSnapshot);
  const setConnection = useDesk((state) => state.setConnection);
  const setConfig = useDesk((state) => state.setConfig);
  const setTicker = useDesk((state) => state.setTicker);
  const setBootError = useDesk((state) => state.setBootError);
  const [view, setView] = useState<"overview" | "desk" | "reports">("desk");
  const [diagnostic, setDiagnostic] = useState<DiagnosticReport | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [exportRequest, setExportRequest] = useState(0);
  const exportSeen = useRef(0);

  useEffect(() => {
    if (!insideDesktop()) return;
    let stopSnapshot = () => {};
    let stopConnection = () => {};
    let stopConfig = () => {};
    let stopDiagnostic = () => {};
    let stopMenu = () => {};
    let stopNotice = () => {};
    let cancelled = false;

    void (async () => {
      const { listen } = await import("@tauri-apps/api/event");
      if (cancelled) return;
      stopSnapshot = await listen<Parameters<typeof setSnapshot>[0]>("terminal://snapshot", (event) => {
        setSnapshot(event.payload);
      });
      stopConnection = await listen<Parameters<typeof setConnection>[0]>("terminal://connection", (event) => {
        setConnection(event.payload);
      });
      stopConfig = await listen<DesktopConfig>("desktop://config", (event) => {
        setConfig(event.payload);
      });
      stopDiagnostic = await listen<DiagnosticReport>("desktop://diagnostic", (event) => {
        setDiagnostic(event.payload);
      });
      stopMenu = await listen<string>("desktop://menu", (event) => {
        const next = viewForMenu(event.payload);
        if (next) setView(next);
        if (wantsExport(event.payload)) setExportRequest((value) => value + 1);
      });
      stopNotice = await listen<string>("desktop://notice", (event) => {
        if (event.payload) setNotice(event.payload);
      });
      try {
        const [nextConfig, nextSnapshot] = await Promise.all([loadConfig(), loadTerminal()]);
        if (cancelled) return;
        setConfig(nextConfig);
        setSnapshot(nextSnapshot);
        setConnection({
          state: "connected",
          sessionOpen: nextSnapshot.sessionOpen,
          lastSuccessAt: null,
          retryInMs: null,
        });
      } catch (error) {
        if (cancelled) return;
        setBootError(readError(error));
        setConnection({
          state: "disconnected",
          sessionOpen: null,
          lastSuccessAt: null,
          retryInMs: null,
        });
      }
    })();

    return () => {
      cancelled = true;
      stopSnapshot();
      stopConnection();
      stopConfig();
      stopDiagnostic();
      stopMenu();
      stopNotice();
    };
  }, [setBootError, setConfig, setConnection, setSnapshot]);

  const theme = themeAttribute(config?.theme);
  if (theme) document.documentElement.dataset.theme = theme;
  else document.documentElement.removeAttribute("data-theme");

  async function publish(report: Promise<DiagnosticReport>) {
    try {
      setDiagnostic(await report);
    } catch (error) {
      setDiagnostic({ title: readError(error), health: null, status: null });
    }
  }

  if (!insideDesktop()) {
    return (
      <main className="gate">
        <p className="brand">Vortex Desktop</p>
        <h1>Abra pelo Vortex Desktop.</h1>
        <p>Esta tela é o cliente da mesa. Ela não consulta a API pelo navegador.</p>
      </main>
    );
  }

  const link: HeaderLink = !snapshot && !booted ? "idle" : connection.state;
  const report = diagnostic ? diagnosticBody(diagnostic) : "";
  const figures = headerModel({
    cash: snapshot?.portfolio.cash,
    equity: snapshot?.portfolio.equity,
    pnl: snapshot?.portfolio.totalProfitAndLoss,
    state: link,
    lastSuccessAt: connection.lastSuccessAt,
  });
  const quotes = snapshot ? snapshot.quotes : null;
  const themeName = config?.theme ?? "system";

  return (
    <div className="desk">
      <header className="top">
        <div>
          <p className="brand">Vortex Desktop</p>
          <p className="paper">Paper trading</p>
        </div>
        <div className="views" role="group" aria-label="Visão">
          <button type="button" className={view === "overview" ? "selected" : ""} onClick={() => setView("overview")}>
            Visão Geral
          </button>
          <button type="button" className={view === "desk" ? "selected" : ""} onClick={() => setView("desk")}>
            Mesa
          </button>
          <button type="button" className={view === "reports" ? "selected" : ""} onClick={() => setView("reports")}>
            Relatórios
          </button>
        </div>
        <p className={`pill ${snapshot?.sessionOpen === true ? "open" : ""}`}>
          {sessionLabel(snapshot?.sessionOpen)}
        </p>
        <dl className="figures">
          <div>
            <dt>Caixa</dt>
            <dd>{figures.cash}</dd>
          </div>
          <div>
            <dt>Patrimônio</dt>
            <dd>{figures.equity}</dd>
          </div>
          <div>
            <dt>P&L</dt>
            <dd>{figures.pnl}</dd>
          </div>
        </dl>
        <div className="themes" role="group" aria-label="Tema">
          {THEMES.map(([value, label]) => (
            <button
              key={value}
              type="button"
              className={config?.theme === value ? "selected" : ""}
              onClick={() => {
                void saveTheme(value).then(setConfig).catch((error: unknown) => {
                  setBootError(readError(error));
                });
              }}
            >
              {label}
            </button>
          ))}
        </div>
        <p className="host">{config ? `${config.scheme} · ${config.host}` : "Host da API"}</p>
      </header>
      {bootError ? <p className="banner">{bootError}</p> : null}
      {notice ? <p className="notice" role="status">{notice}</p> : null}
      {link === "degraded" && snapshot ? <p className="banner">Desatualizado</p> : null}
      {view === "overview" ? (
        <Overview quotes={quotes} ticker={ticker} instrumentId={instrumentId} onTicker={setTicker} />
      ) : view === "reports" ? (
        <Reports quotes={quotes} exportRequest={exportRequest} exportSeen={exportSeen} />
      ) : (
        <Desk
          quotes={quotes}
          orders={snapshot?.orders ?? []}
          positions={snapshot?.portfolio.positions ?? []}
          ticker={ticker}
          instrumentId={instrumentId}
          theme={themeName}
          onTicker={setTicker}
        />
      )}
      <section className="diagnostic" aria-label="Diagnóstico">
        <div className="chart-tools">
          <strong>Diagnóstico</strong>
          <button
            type="button"
            onClick={() => {
              void publish(loadHealth().then((health) => ({ title: "Saúde da API", health, status: null })));
            }}
          >
            Saúde da API
          </button>
          <button
            type="button"
            onClick={() => {
              void publish(
                loadReady().then((health) => ({ title: "Prontidão da API", health, status: null })),
              );
            }}
          >
            Prontidão da API
          </button>
          <button
            type="button"
            onClick={() => {
              void publish(
                loadStatus().then((status) => ({ title: "Estado da sessão", health: null, status })),
              );
            }}
          >
            Estado da sessão
          </button>
        </div>
        {diagnostic ? (
          <>
            <p>{diagnostic.title}</p>
            {report ? <pre>{report}</pre> : null}
          </>
        ) : (
          <p className="muted">O menu Diagnóstico da bandeja também atualiza este painel.</p>
        )}
      </section>
      <footer>
        <span>{connectionLabel(link)}</span>
        <span>Última leitura {figures.stamp}</span>
      </footer>
    </div>
  );
}
