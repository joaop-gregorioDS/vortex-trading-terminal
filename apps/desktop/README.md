# Vortex Desktop

Cliente fino, em Tauri, da API de paper trading. A janela não chama a API. O HTTP fica no Rust.

A conta de demonstração é compartilhada. Esta fatia lê `GET /api/terminal`, `GET /api/chart`, `GET /api/market/history`, `GET /api/reports/performance`, `GET /api/reports/trades`, `GET /health/live`, `GET /health/ready` e `GET /api/status`. Também envia ordem, cancelamento, revisão, preview de gráfico, proteção e flatten. Reverse, zeragem de emergência e `POST /api/session/reset` só seguem depois do diálogo nativo. O reset reabre a sessão para todos. O CSV de negócios abre o diálogo de salvar no núcleo; cancelar não cria arquivo.

A Visão Geral e a mesa listam o catálogo que o snapshot trouxer. A mesa desenha o pregão ou o histórico, um de cada vez, e no pregão mostra a boleta do ativo escolhido. Relatórios mostra o desempenho e os negócios fechados, com filtro pelo catálogo. A logo de atribuição do gráfico fica em `public/tradingview.svg`.

O tema claro, escuro ou do sistema fica na store do núcleo. O tamanho e a posição da janela também. A WebView não recebe permissão desses plugins. O menu Diagnóstico da bandeja, e no macOS o menu da aplicação, chama a saúde e o estado.

O corte da F6 acrescenta, no macOS, o menu Arquivo, Editar, Exibir, Operar e Ajuda. O atalho é o do menu, com a janela em foco. Operar abre o diálogo nativo e não envia reset, zeragem nem inversão: o token é gasto na hora e a página avisa para usar a boleta. Arquivo abre Relatórios e o CSV que já existe. Um `executionId` novo no snapshot gera o aviso no núcleo, com o plugin `notification` 2.6.0. O primeiro snapshot só memoriza os ids. A segunda execução foca a janela `main`, com o plugin `single-instance` 2.5.3, e não cria outro poller. A bandeja mantém o P&L com o pregão fechado. A barra de menu, a notificação do sistema e um segundo processo não foram vistos nesta máquina.

O `cargo deny check` em `src-tauri` lê `deny.toml`. O workflow `.github/workflows/desktop.yml` roda esse check no pull request e, na tag `desktop-v*`, monta AppImage, `.deb` e `.dmg` como release em rascunho. Esse workflow ainda não rodou. O AppImage não foi aberto numa Ubuntu 22.04 limpa. O `.dmg` notarizado e o updater ficam de fora: o repositório é `joaop-gregorioDS/vortex-trading-terminal` e não há chave de atualização no Git. No macOS o job para antes do build se faltar `APPLE_SIGNING_IDENTITY`, `APPLE_CERTIFICATE`, `APPLE_CERTIFICATE_PASSWORD`, `APPLE_API_ISSUER`, `APPLE_API_KEY`, `APPLE_API_KEY_P8` ou `APPLE_TEAM_ID`. `APPLE_API_KEY` é o Key ID. `APPLE_API_KEY_P8` é o conteúdo do `.p8`, gravado no runner em `APPLE_API_KEY_PATH`.

## Rodar

Na pasta `apps/desktop`:

```powershell
npm install
npm test
cd src-tauri
cargo test
```

Para abrir a janela:

```powershell
$env:VORTEX_DESKTOP_API_BASE = "http://127.0.0.1:5187"
npm run tauri -- dev
```

Sem a variável, a base é `https://vortexsoftware.tech`. O cabeçalho mostra só o esquema e o host. Os tipos em `src/bindings` saem do `ts-rs`. Para gravá-los de novo: `$env:VORTEX_WRITE_BINDINGS = "1"; cargo test bindings_match_committed_sources`.
