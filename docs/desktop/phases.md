# Fases do Vortex Desktop

Unidade relativa para uma pessoa que já conhece `apps/web` e a API. Não é prazo de calendário. A fase seguinte começa quando os critérios da anterior estão verdes. Nenhuma fase altera `src/VortexTrading.*` nem o comportamento do web.

| Fase | Tamanho relativo | Depende de |
| --- | --- | --- |
| F0 Spike | 1 | — |
| F1 Shell | 2 | F0 |
| F2 Mercado e gráfico | 3 | F1 |
| F3 Boleta e posições | 3 | F2 |
| F4 Relatórios e CSV | 1 | F1. Pode seguir em paralelo com F3 depois do snapshot |
| F5 Pacote, assinatura, updater, CI | 3 | F1 no Linux. Notarização precisa de uma tela que abra. Idealmente depois da F4 |
| F6 Polimento nativo | 2 | F3, para a notificação de fill ter o que observar |

Soma relativa: 15. F4 em paralelo com o fim da F3 reduz o caminho crítico para cerca de 14.

## F0 — Spike

Entrega: `apps/desktop` abre uma janela, desenha um candle com `lightweight-charts` 5.2.1 e um command Rust chama `GET /api/terminal` em `http://127.0.0.1:5187` ou na produção, conforme a config.

Aceite:

- O mesmo código sobe no WebKitGTK 4.1 e no WKWebView.
- Relatório curto em `docs/desktop/spike-webkit.md` com FPS, arrasto de uma linha de preço, HiDPI, X11, Wayland e o resultado de `WEBKIT_DISABLE_DMABUF_RENDERER` em Intel, AMD e NVIDIA. Máquina que não existir fica marcada como não medida, não como aprovada.
- O JSON do terminal deserializa com decimal em string. Um teste `cargo test` prova ida e volta de um decimal de 27 casas, o mesmo caso do web.
- A WebView não executa `fetch` para o host da API. Dá para ver no log do `ApiClient` a única chamada.

## F1 — Shell, config, saúde, poller, bandeja

Aceite:

- Menu de aplicação no macOS e bandeja nos dois sistemas com pregão aberto, fechado ou desconectado, mais o P&L que veio no snapshot.
- Janela restaura tamanho e posição. Tema claro, escuro e sistema.
- Poller nos intervalos da arquitetura. Uma requisição em voo. Resposta velha descartada.
- Cabeçalho limpa caixa, patrimônio e P&L no estado `disconnected` e mantém a grade com carimbo.
- `health_live` e `get_status` acessíveis pelo menu de diagnóstico.
- URL base visível. Padrão `https://vortexsoftware.tech`.

## F2 — Cotações, gráfico, histórico

Aceite:

- Visão Geral e a mesa mostram o catálogo que o snapshot trouxer, sem lista de tickers fixa no código.
- Trocar de ativo resolve `instrumentId` pelo snapshot.
- Gráfico usa `GET /api/chart`, inclusive o corpo `{ "status": "empty" }`.
- Histórico usa `GET /api/market/history` com os intervalos `1d,1h,15m,5m,1m`.
- A logo da TradingView está no bundle.
- Se a extração de `packages/trading-ui` começar nesta fase, `apps/web` passa nos testes `tests/charts-adapter.test.mjs` e `tests/trading-format.test.mjs` e no `npm run build` sem mudança de comportamento. Se a extração não couber, o desktop tem view própria e a extração não fica pela metade no web.

## F3 — Boleta, ordens, posições, proteção

Aceite:

- Enviar, cancelar, revisar, preview de chart, proteção, flatten.
- Reverse, emergency-flat e reset só com o diálogo nativo e o token.
- HTTP 201 com `rejected` aparece como erro, não como ordem aceita.
- POST não é repetido num teste que derruba a conexão depois do servidor ter respondido.
- Linha otimista some ou casa com o snapshot seguinte.
- Arrastar linha chama `revise_order`. Soltar proteção nova chama `place_protection`. O `type` limit ou stop desenhado é o que o preview devolveu.

## F4 — Relatórios e CSV

Aceite:

- KPIs e curva de equity vêm de `PerformanceReport`, sem conta no cliente.
- CSV abre o diálogo nativo, grava no caminho escolhido e preserva o decimal. Cancelar não cria arquivo.
- Filtro por ticker usa `GET /api/reports/trades?ticker=`.

## F5 — Empacotamento

Aceite:

- AppImage e `.deb` produzidos em `ubuntu-22.04`. O AppImage abre numa instalação limpa de 22.04 com as libs do documento de pacote.
- `.dmg` universal notarizado, com staple, abre no macOS 12 ou superior sem aviso de desenvolvedor não identificado.
- Updater contra um release de teste do GitHub, chave pública no app, privada só no secret.
- `desktop.yml` verde sem alterar os jobs do `ci.yml`.
- `cargo deny check` verde.

## F6 — Polimento nativo

Aceite:

- Menu macOS completo da estação: Arquivo, Exibir, Operar, Ajuda. Atalhos só com a janela em foco.
- Notificação de fill quando um `executionId` novo aparece no snapshot, com o app em segundo plano.
- Segunda instância foca a primeira.
- Tema e bandeja continuam corretos com o pregão fechado.

## Fora das fases

Autenticação, OpenAPI no CI do backend, SSE, manifesto no Nginx e correção dos textos do README sobre stop/OCO. Estão em `backend-requests.md` ou no resumo de decisões do dono.
