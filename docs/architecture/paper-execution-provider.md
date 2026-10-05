# Paper Execution Provider — TASK 2.0

Primeiro provider de execução concreto, sobre o checkpoint `494c246`. Fecha o adiamento
"Paper provider/matching" registrado no ADR-034 e dá ao `OrderExecutionWorkflow` um produtor real de
fatos, sem alterar Application, Domain ou a fronteira congelada do ADR-033.

## Onde vive

| Tipo | Namespace |
| --- | --- |
| `PaperMarketObservation` | `VortexTrading.Application.Trading.Paper` |
| `PaperExecutionProvider` | `VortexTrading.Infrastructure.Trading.Paper` |

O módulo proprietário é **Trading**, não Market Data: o paper exchange consome observações de
mercado, mas pertence ao lado de execução. Nenhum tipo novo foi adicionado a
`Application.Trading.Execution`, cuja guarda do ADR-033 continua afirmando exatamente os seis
contratos mais `OrderExecutionWorkflow`. Um teste novo cobre o namespace `Trading.Paper` para
garantir que ele não arraste tipos de market data, da fronteira de execução ou de infraestrutura.

## Fluxo

```text
chamador                    provider                     consumidor único
   |  SubmitAsync(submission, submittedAt)  ->  [Accepted]
   |  Observe(observation)                  ->  matching -> [Fill...]
   |  RequestCancelAsync(orderId, at)       ->  [Cancelled]
   |                                            ReadReportsAsync -> OrderExecutionWorkflow.ApplyReport
```

O provider **nunca** lê o stream; o dono do escopo é o chamador, conforme o ADR-034. `Accepted` é
publicado antes de qualquer fill, então o consumidor nunca vê um fill de uma ordem cuja aceitação
não passou pelo stream.

## Regras de matching

| Aspecto | Regra |
| --- | --- |
| Tempo | `submittedAt`/`requestedAt` vêm do chamador; nenhum relógio é lido |
| Elegibilidade | Somente observações com tempo **estritamente maior** que a submissão |
| Ordem a mercado | Preenche ao preço de referência da observação |
| Ordem limite | Preenche somente quando a referência alcança o limite, e **no preço do limite** |
| Compra | Cruza quando referência `<=` limite |
| Venda | Cruza quando referência `>=` limite |
| Liquidez | Volume da observação é finito e consumido em ordem de chegada (FIFO) |
| Fill parcial | Quantidade = mínimo entre o restante exato e o volume disponível |
| Restante | Permanece em repouso e continua tentando nas próximas observações |
| `ExecutionId` | Derivado de `(ProviderKey, OrderId, sequência)` via SHA-256; nunca aleatório |
| `ExecutedAt` | Tempo do evento da observação que produziu o fill |

O tempo do evento do fill é o tempo da observação, e é o mesmo valor entregue como `at` ao workflow
nos testes de integração. `ObservedAt` do relatório é o tempo da observação; na aceitação e no
cancelamento, é o tempo de submissão/pedido fornecido pelo chamador.

## Semântica de falha

Toda operação é em memória, então nenhuma falha tem efeito externo possível e **todas** usam
`OutcomeUncertain = false`, o que autoriza retry explícito seguro pelo workflow.

| Situação | Classificação |
| --- | --- |
| `OrderId` já ativo no escopo | `InvalidProviderResponse` |
| Cancelar ordem desconhecida, preenchida ou já cancelada | `CancellationRefused` |
| Stream completado durante operação | `Unavailable` |
| Instrumento diferente do configurado | `ArgumentException` (erro de programação) |
| Observação fora de ordem temporal | `ArgumentException` (erro de programação) |

Nenhuma falha fabrica `Rejected` ou `Cancelled`. Cancelamento recusado nunca altera a ordem.

## Limitações explícitas

O modelo é direcional e conservador, **não** um book real. Não há side de bid/ask, então uma compra
cruza comparando o preço de referência com o limite, e não contra um ask. Consequências:

- Sem slippage, spread, taxas, impacto de mercado ou melhora de preço.
- Como as observações vêm de candles, o preço de referência é o fechamento e a avaliação ocorre após
  a candle fechar; a ponte candle → observação é do chamador, mantendo a resolução de preço
  auditável.
- Um instrumento por provider. Múltiplos instrumentos exigem decisão própria.
- Prioridade é por chegada, não por atratividade de preço.
- Estado vive na instância: reiniciar o processo perde o livro. Não há persistência do livro,
  stream durável, reconexão ou exatamente-uma-vez.
- Reenviar a mesma observação oferece o volume novamente; o chamador decide quando o volume é novo.

## Validação

18 testes em `tests/backend/VortexTrading.Infrastructure.Tests/Trading/Paper` cobrem: aceitação
antes de qualquer fill, preenchimento a mercado no preço de referência, ausência de look-ahead,
limite de compra e de venda cruzando apenas quando alcançado e sempre no preço do limite, fill
parcial por volume insuficiente, restante atravessando observações, soma dos fills nunca excedendo a
quantidade submetida, FIFO entre ordens, identidade de execução determinística e sem colisão,
cancelamento efetivo, cancelamento recusado sem fabricar fato, identidade duplicada recusada,
instrumento estrangeiro, observação fora de ordem, tempo de submissão anterior à criação, validação
da `ProviderKey` e dois testes de integração que dirigem o `OrderExecutionWorkflow` real com o
provider real até `Filled` e até `Cancelled`.

Um defeito real foi encontrado e corrigido durante a validação: uma ordem a mercado preenchia
instantaneamente contra uma observação publicada **antes** da submissão, porque faltava comparar o
tempo do evento com o tempo de submissão. A regra causal agora é aplicada e coberta por teste.

## Referências

- ADR-036 — Deterministic Paper Execution Provider.
- ADR-033 — Execution Provider Boundary Contracts.
- ADR-034 — Single-Order Execution Workflow.
- [single-order-execution-workflow.md](single-order-execution-workflow.md).
