# Vortex Trading Terminal — Frontend Foundation

Bootstrap técnico da TASK 0.2, gerado com `create-next-app@16.3.8`, template oficial `app-tw-empty`, npm, TypeScript, ESLint, Tailwind, App Router e diretório `src`. React Compiler e geração de instruções para agentes foram desabilitados. Turbopack permanece na configuração padrão do Next.js.

## Requisitos e versões

Node 24.x e npm 11.x. Versões consultadas no registry npm em 2026-10-01 e fixadas no manifest e no lockfile:

| Dependência | Versão |
| --- | --- |
| Next.js / eslint-config-next | 16.3.8 |
| React / React DOM | 19.3.0 |
| TypeScript | 6.0.3 |
| ESLint | 9.39.5 |
| Tailwind CSS / @tailwindcss/postcss | 4.3.3 |
| @types/node | 24.19.0 |
| @types/react / @types/react-dom | 19.3.0 |

As versões `latest` de TypeScript (7.0.2) e ESLint (10.11.0) não foram selecionadas: `typescript-eslint@8.71.0` declara TypeScript `>=4.8.4 <6.1.0`, e os plugins React, import e JSX accessibility usados pelo Next aceitam ESLint 9, mas ainda não ESLint 10. Foram escolhidas as versões estáveis mais recentes das faixas compatíveis, sem forçar peer dependencies.

Dívida técnica de tooling: npm sinaliza `eslint@9.39.5` como deprecated, e a linha ESLint 9 está fora de manutenção desde 2026-08-06. Na TASK 0.2-B, o arquiteto aceitou temporariamente essa versão. A migração para ESLint 10 deverá ser reavaliada quando a cadeia utilizada oferecer compatibilidade oficial adequada.

Todas as dependências diretas selecionadas são releases estáveis. O grafo oficial do tooling inclui `gensync@1.0.0-beta.2` via Babel/React Hooks e `resolve@2.0.0-next.7` via plugins de imports, exclusivamente como transitivas. Essas observações foram aceitas temporariamente pelo arquiteto na TASK 0.2-B: **APPROVED WITH DOCUMENTED OBSERVATIONS**. Permanecem como dívida técnica a reavaliar, sem overrides, `--force`, downgrade arbitrário ou manipulação manual do lockfile.

A instalação também emitiu aviso `allow-scripts` para o postinstall de `unrs-resolver@1.12.2`; nenhuma aprovação adicional de scripts foi executada. Lint e build passaram com a configuração instalada.

## Comandos

Execute neste diretório:

```sh
npm ci
npm run dev
npm run lint
npm run build
npm run start
npm audit
```

`dev` e `start` iniciam servidores; `start` exige build prévio. `lint` executa `eslint .`, seguindo a integração oficial do Next.js com o ESLint CLI. `package-lock.json` deve ser versionado; `node_modules`, `.next` e arquivos gerados de build devem permanecer ignorados.

## Escopo

Na TASK 0.36, a página inicial passa a apresentar um snapshot histórico de demonstração: 200 Candles de um minuto, SMA(20) sobre o preço e MACD(12,26,9) em painel separado. O idioma do layout é `pt-BR`; TypeScript mantém `strict: true` e o alias `@/*` aponta para `./src/*`.

O Server Component busca `POST /api/chart-snapshots` em tempo de requisição, com `connection()` e `cache: "no-store"`. O build não depende de uma API disponível. Configure `VORTEX_API_BASE_URL` no ambiente do servidor Next.js; em desenvolvimento há fallback local para `http://127.0.0.1:5187`. Em produção a variável é obrigatória e a API de demonstração não é registrada. Nenhum secret é necessário para essa variável de URL, que não usa prefixo `NEXT_PUBLIC_`.

Inicie a API no ambiente Development, mantendo a configuração de persistência já prevista pelo backend, e execute `npm run dev` neste diretório. A API não possui launch profile: da raiz, use `dotnet run --project src/VortexTrading.Api --no-launch-profile -- --environment Development --urls http://127.0.0.1:5187`. O gráfico funciona com PostgreSQL e Redis indisponíveis; seus readiness checks permanecem independentes. As configurações de conexão exigidas pelo startup do backend continuam aplicáveis.

O Client Component cria e remove o gráfico no navegador. A aproximação de strings decimais para `number` e a conversão de UTC para segundos Unix ficam restritas ao adaptador gráfico. Não há cálculo financeiro no frontend, replay animado, streaming ou cache de resultados.

Os seis checks determinísticos do adaptador usam o runner nativo do Node, sem framework adicional:

```sh
node --experimental-strip-types --test tests/charts-adapter.test.mjs
```

O Node instalado pode emitir `MODULE_TYPELESS_PACKAGE_JSON` ao carregar o módulo TypeScript do teste; isso não afeta o lint/build da aplicação. F-05 permanece dívida de tooling aceita: cinco pacotes da cadeia ESLint afetados pelo advisory `GHSA-vfj7-8cjw-p6xm`, sem vulnerabilidades no audit de produção após a adição do gráfico. Nenhum override ou audit fix foi aplicado nesta tarefa.

## Biblioteca de gráficos e atribuição

Única nova dependência direta de visualização: `lightweight-charts@5.2.1`, licença Apache-2.0, sem wrapper React. A logo de atribuição permanece habilitada, e o rodapé do gráfico preserva a ligação à TradingView e o aviso da versão utilizada:

> TradingView Lightweight Charts™
> Copyright (с) 2025 TradingView, Inc. https://www.tradingview.com/

Fonte: [NOTICE da versão 5.2.1](https://github.com/tradingview/lightweight-charts/blob/v5.2.1/NOTICE). A arquitetura e as limitações da demonstração estão em [Historical Chart Vertical Slice](../../docs/architecture/historical-chart-vertical-slice.md).

## Referências

- [create-next-app](https://nextjs.org/docs/app/api-reference/cli/create-next-app)
- [ESLint no Next.js](https://nextjs.org/docs/app/api-reference/config/eslint)
- [Suporte de versões ESLint](https://eslint.org/version-support/)
