# Testes do Vortex Desktop

A API real não é dependência do `cargo test` no CI. Fixture JSON copiada de uma resposta real entra em `src-tauri/tests/fixtures/`. O teste que fala com `127.0.0.1:5187` existe, fica atrás de `#[ignore]` e só roda quando alguém passa `--ignored` com a API no ar.

## Rust

`cargo test` no crate `src-tauri`.

| Teste | O que prova |
| --- | --- |
| `wiremock` 0.6.5 serve a fixture de `GET /api/terminal` | O DTO aceita o JSON de produção, inclusive campo extra desconhecido |
| Decimal de 27 casas na ida e na volta | A string que entra é a string que sai. Sem notação científica |
| Decimal inválido e decimal com escala acima de 28 | `ApiError` `contract`, sem float |
| HTTP 201 com `status: "rejected"` | `kind: rejected`, não `Ok` |
| ProblemDetails com `code` e `traceId` | Os dois chegam no `ApiError` e o `title` é o da API |
| POST | O mock conta uma única tentativa mesmo quando o cliente estoura o timeout depois de a conexão cair |
| GET | Segunda tentativa só quando não houve resposta HTTP |
| `ChartBody` | `{ "status": "empty" }` e um snapshot completo deserializam no variant certo |
| `DangerGate` | Token de outra ação, token gasto e token vencido devolvem `danger` |
| Poller | Duas respostas fora de ordem: a geração menor não substitui a maior |
| CSV | Os campos decimais do `ClosedTrade` saem intactos |

O `ApiClient` recebe a URL do mock. Nenhum teste de unidade usa a VPS.

## Bindings

Um teste de build gera o TypeScript com `ts-rs` para um diretório temporário e compara com `apps/desktop/src/bindings/`. Diff falha. Isso impede o DTO Rust e o tipo da UI de andarem separados.

## Front

Vitest nos componentes de view com o snapshot em fixture. Casos mínimos:

- formatar decimal sem converter para `number` (o teste do web em `trading-format.test.mjs` é a referência; se `format.ts` for para `packages/trading-core`, o mesmo arquivo de teste roda nos dois consumidores);
- cabeçalho zera os saldos em `disconnected` e mostra o carimbo;
- boleta não chama `submit_order` duas vezes no duplo clique;
- estado otimista recebe o snapshot seguinte e não soma quantidade local.

O adapter do gráfico, se extraído, continua coberto por `apps/web/tests/charts-adapter.test.mjs` ou pelo teste movido junto com o pacote. O web não perde esse teste.

## E2E

`tauri-driver` 2.1.0 no Linux, job noturno do `desktop.yml`, não no macOS. A documentação do ecossistema não oferece driver WebDriver para macOS. No macOS a garantia é o teste de command mais o Vitest, e uma passagem manual no checklist da F5 (abre, notarização, bandeja, uma ordem na conta demo).

O E2E Linux cobre três fluxos, contra `wiremock`, não contra a VPS:

1. Abrir, ver o snapshot na visão geral.
2. Enviar ordem e ver a recusa 201.
3. Pedir reset e cancelar o diálogo nativo. O mock não recebe `POST /api/session/reset`.

## O que não é teste do desktop

A suíte .NET não muda. O lint e o build do Next não mudam, exceto quando uma fatia de `packages/*` for extraída: aí o web tem de continuar verde, e isso é critério da fase, não um teste novo de regra de negócio.
