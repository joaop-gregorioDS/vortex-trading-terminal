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

Página temporária contendo apenas o nome do projeto, a confirmação de fundação operacional e Vortex Software. Tailwind fornece somente espaçamento, cores e tipografia básicos. O layout possui metadata básica, idioma inglês compatível com o conteúdo e CSS global.

TypeScript mantém `strict: true`, com alias `@/*` apontando para `./src/*`. Não há terminal, dashboard, gráficos, autenticação, comunicação com backend ou biblioteca adicional de produto. Nenhuma URL de API ou secret foi configurado.

## Referências

- [create-next-app](https://nextjs.org/docs/app/api-reference/cli/create-next-app)
- [ESLint no Next.js](https://nextjs.org/docs/app/api-reference/config/eslint)
- [Suporte de versões ESLint](https://eslint.org/version-support/)
