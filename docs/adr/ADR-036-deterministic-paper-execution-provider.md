# ADR-036 — Deterministic Paper Execution Provider

- Status: Accepted
- Date: 2026-10-06
- Scope: TASK 2.0. Paper Exchange sobre a fronteira de execução do ADR-033 e o workflow do ADR-034.

## Context

O ADR-034 implementou o workflow de ordem única e adiou explicitamente "Paper provider/matching".
Sem um provider concreto, nada executa: o workflow sabe aplicar fatos, mas ninguém os produz.

O ADR-033 fixa o que o provider pode ser: identidade estável, duas commands com tempo explícito
fornecido pelo chamador, um stream de fatos normalizados, nenhum relógio autônomo, nenhuma geração
de identidade pelo contrato, nenhuma promessa de exatamente-uma-vez. `SubmitAsync` completa quando
o fato `Accepted` ou `Rejected` está **disponível no stream**, não quando a ordem é aceita. Uma
ordem GTC pode permanecer ativa indefinidamente.

O ADR-002 exige aritmética exata em valores financeiros. O ADR-035 proíbe persistir valores exatos
com escala fixa. O dataset de desenvolvimento já fornece 200 candles de um minuto com volume, o que
permite liquidez finita real sem inventar dados.

## Decision

Implementar `PaperExecutionProvider` em `VortexTrading.Infrastructure.Trading.Paper`. Ele é um
`IExecutionProvider` determinístico, sem relógio e sem aleatoriedade: todo tempo vem do chamador e
todo preço vem de observações de mercado entregues pelo chamador.

**Tempo explícito, nenhum relógio.** `submittedAt` e `requestedAt` são fornecidos pelo chamador e
usados apenas como fatos do relatório/time de tentativa. O provider nunca lê o relógio do sistema.

**Observações como entrada.** `PaperMarketObservation` carrega tempo do evento, instrumento, preço
de referência e volume disponível. A ordem só cruza com observações cujo tempo é **estritamente
maior** que o tempo de submissão, o que impede usar preço anterior à própria intenção — o mesmo
princípio causal do replay. Consequência direta: uma ordem a mercado submetida **depois** de uma
observação não preenche contra ela, e permanece em repouso até a próxima. Observações devem chegar
em tempo não decrescente; violar isso é erro de programação do chamador, não fato comercial.

**Fonte de preço: preço de referência da observação.** Uma ordem a mercado preenche ao preço de
referência observado. Uma ordem limite preenche **no preço do limite**, nunca melhor, e apenas
quando o preço de referência alcança o limite: compra quando a referência é menor ou igual ao
limite, venda quando é maior ou igual. O preço de referência é a única fonte de preço; o provider
não consulta candles, book ou qualquer outro dado.

**Liquidez finita com fills parciais.** Cada observação oferece um volume disponível. O consumo é
por ordem de chegada (FIFO), e cada fill consome o mínimo entre o restante da ordem e o volume
disponível. Ordem a mercado com volume insuficiente preenche parcialmente e o restante permanece
no livro para a próxima observação. Isso é o que torna fills parciais, cancelamento e corrida
cancel/fill exercitáveis de verdade, em vez de toda ordem fechar cheia.

**`ExecutionId` determinístico.** O contrato exige identidade estável de execução, e o ADR-033 diz
que o produtor é dono da geração determinística. A identidade é derivada de `OrderId` e do número
de sequência do fill (SHA-256 truncado), nunca de `Guid.NewGuid()`. A mesma ordem com a mesma
sequência produz sempre o mesmo fill, o que torna qualquer cenário reprodutível e permite ao
domínio deduplicar corretamente.

**Ciclo de vida por ordem.** Submissão válida enfileira `Accepted` e registra a ordem. Cancelamento
é permitido para ordem em repouso: enfileira `Cancelled` e remove a ordem do livro. Cancelar ordem
desconhecida, já preenchida ou já cancelada é `CancellationRefused`, nunca um `Cancelled` fabricado.
Falhas de submissão e cancelamento são sempre `OutcomeUncertain = false`, porque toda operação é
em memória e nunca teve efeito externo possível — o que autoriza retry explícito seguro.

**Ausência de promessa de robustez.** Não há persistência, fila durável, replay de stream, reconexão
ou exatamente-uma-vez. O estado vive na instância; perder o processo perde o livro. O ADR-034 já
registra essas pendências.

**Política de preço documentada como limitação explícita.** O modelo de matching é direcional e
conservador, não um book real: uma compra cruza se o preço observado for maior ou igual ao preço de
referência, porque não há lado de bid/ask modelado. Como as observações vêm de candles, o preço de
referência é o fechamento da candle e a avaliação ocorre após a candle fechar. Sem slippage,
spread, taxas ou impacto de mercado. Referenciar o preço de referência é diferente de reivindicar
execução ao melhor preço de um book inexistente.

## Namespace e guarda de fronteira

O teste de contrato do ADR-033 (`IExecutionProviderContractTests`) afirma que o namespace
`VortexTrading.Application.Trading.Execution` contém exatamente os seis contratos mais
`OrderExecutionWorkflow`. Esta decisão **não** afrouxa essa guarda nem contrabandeia tipos por
brechas dela.

`PaperMarketObservation` e o provider vivem em namespaces próprios: `Trading.Paper` em Application
e `Infrastructure.Trading.Paper` em Infrastructure. O módulo proprietário é **Trading**, não Market
Data: o paper exchange consome observações de mercado, mas é o lado de execução do Trading. Colocar
o tipo em `Trading.Execution` misturaria a fronteira congelada com uma implementação de matching, e
colocá-lo em `MarketData` atribuiria ao módulo errado.

A guarda existente é estendida por esta decisão para permitir explicitamente o namespace
`Application.Trading.Paper`, e continua proibindo qualquer tipo público novo em `Trading.Execution`.

## Consequences

- O workflow do ADR-034 passa a ter um produtor concreto de fatos, sem alterar Application.
- O matching é determinístico e reproduzível: mesmas entradas produzem exatamente os mesmos
  relatórios, o que permite testar corridas e duplicatas sem flakiness.
- Sem persistência do livro, reiniciar o processo perde ordens em repouso; um coordinator durável
  é decisão futura.
- O consumo de volume é estado de memória por observação; observe a mesma observação duas vezes e
  o volume é contado duas vezes, o que é responsabilidade do chamador.
- `PaperMarketObservation` cria um segundo contrato de entrada além de `IMarketDataProvider`. A
  ponte entre candles e observações é do chamador, mantendo a resolução de preço explícita e
  auditável em vez de escondida no provider.
- Slippage, spread, taxas, múltiplos instrumentos simultâneos e prioridade por preço não são
  modelados. Cada um exige decisão própria.
- O preço de referência de uma observação publicada antes de uma submissão é inutilizável para ela.
  O chamador precisa publicar observações novas para que ordens em repouso progridam; reenviar a
  mesma observação oferece o volume novamente e é decisão do chamador.

## Alternatives

**Reutilizar `IMarketDataProvider` diretamente dentro do provider.** Rejeitada: acoplaria a
execução a um stream assíncrono e a resolução de preço/volume ficaria implícita no provider,
contrariando a exigência de tempo explícito do ADR-033 e dificultando o determinismo.

**Matching imediato e integral, sem volume.** Rejeitada pelo usuário: tornaria fills parciais,
cancelamento em repouso e corrida cancel/fill inatingíveis na prática.

**Preencher limite no preço observado quando melhor que o limite.** Rejeitada: simularia melhora de
preço sem modelar o book que a produziria.

**Colocar o provider em `Trading.Execution`.** Rejeitada: enfraqueceria a guarda de fronteira
congelada e misturaria contrato com implementação.

## References

- ADR-002 — Financial Numeric Types.
- ADR-032 — Minimal Order Domain Foundation.
- ADR-033 — Execution Provider Boundary Contracts.
- ADR-034 — Single-Order Execution Workflow.
- ADR-035 — Order Aggregate Persistence.
- docs/architecture/paper-execution-provider.md.
