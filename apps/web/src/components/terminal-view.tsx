"use client";

import { TerminalProvider, useTerminal } from "@/lib/trading/terminal-context";
import { useDemoEntered } from "@/lib/page-preferences";
import { AppShell } from "@/components/shell/app-shell";
import { DemoAccountScreen } from "@/components/demo-account-screen";
import { VisaoGeralView } from "@/components/views/visao-geral-view";
import { OperarView } from "@/components/views/operar-view";
import { RelatoriosView } from "@/components/views/relatorios-view";
import { HistoricoView } from "@/components/views/historico-view";

function TerminalContent() {
  const { activeNav } = useTerminal();

  switch (activeNav) {
    case "visao-geral":
      return <VisaoGeralView />;
    case "operar":
      return <OperarView />;
    case "relatorios":
      return <RelatoriosView />;
    case "historico":
      return <HistoricoView />;
    default:
      return <VisaoGeralView />;
  }
}

export function TerminalView() {
  const entered = useDemoEntered();
  if (!entered) return <DemoAccountScreen />;

  return (
    <TerminalProvider>
      <AppShell>
        <TerminalContent />
      </AppShell>
    </TerminalProvider>
  );
}
