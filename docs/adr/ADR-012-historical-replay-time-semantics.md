# ADR-012 — Historical Replay Time Semantics

- Status: Accepted
- Date: 2026-10-02.
- Scope: Dataset histórico lógico e timeline virtual em Application, sem provider de Replay.

## Context

Domain define os modelos canônicos, Application define a porta e o orchestrator, e Infrastructure possui SimulationMarketDataProvider. Antes de uma segunda implementação da porta baseada em histórico, precisamos definir representação heterogênea, ordenação e avanço temporal reproduzíveis, sem acoplar essas decisões a armazenamento ou relógio de parede.

ReceivedTime não é o instante de ocorrência de mercado. Empates em EventTime são legítimos e precisam de política explícita. Duplicatas históricas podem representar ocorrências distintas e não devem desaparecer por igualdade estrutural.

## Decision

Historical replay is driven by deterministic event time and a virtual monotonic timeline, not by wall-clock time.

Replay datasets preserve canonical market-data payloads, retain duplicate occurrences and define deterministic ordering without introducing provider sequence or event identity into Domain.

Colocar ReplayEvent, ReplayEventKind, HistoricalReplayDataset e ReplayTimeline em Application/MarketData/Replay. Não alterar Domain ou os contratos/componentes existentes, criar provider, adicionar pacotes ou registrar DI.

## Replay Event and ReplayTime

Usar ReplayEvent sealed e imutável, com factories tipadas e construtores privados que rejeitam payload null. Cada envelope representa exatamente um Quote, Trade ou Candle original. Kind, InstrumentId e ReplayTime são derivados desse único payload; não expor object público nem estado independente de ID/tempo.

Quote e Trade usam EventTime. Candle usa OpenTime, explicitamente não CloseTime. ReceivedTime permanece intacto e não controla replay. OHLC/Volume não são recalculados ou reescritos. A posição de replay de um candle não estabelece disponibilidade antecipada de dados de fechamento; essa política para consumers e estratégias permanece adiada.

## Dataset, Ordering and Tie-Breaking

Exigir coleção não nula, não vazia e sem evento null. Criar snapshot próprio, proteger Events com coleção somente de leitura e preservar as mesmas instâncias de envelopes/payloads.

Ordenar por ReplayTime, InstrumentId.Value, ReplayEventKind e ordinal original de entrada, nessa ordem. Usar comparações de instantes e o comparador padrão de Guid. Definir Quote = 0, Trade = 1 e Candle = 2 exclusivamente para determinismo, sem prioridade financeira.

Ordinal é interno e local ao dataset; não é identidade ou sequence. Eventos com os três primeiros critérios iguais preservam a ordem original. Determinismo exige a mesma ordem de entrada nesses empates finais. Não usar hash de runtime, aleatoriedade ou ordem incidental de containers.

Reter todas as ocorrências, inclusive o mesmo objeto repetido ou payloads estruturalmente iguais. Expor somente Events, Count, StartTime mínimo e EndTime máximo de ReplayTime, sem metadata operacional de origem ou importação.

## Virtual Time

ReplayTimeline nasce com CurrentTime = dataset.StartTime e range fechado do dataset. AdvanceTo exige instante não nulo e aceita somente StartTime <= instant <= EndTime e instant >= CurrentTime. Instante igual é permitido como no-op; retrocesso ou valor fora do range é rejeitado sem mudar a posição. Comparar instantes diretamente evita aritmética de duração e overflow.

Avanço é explícito e serial por sessão, independente de enumeração automática, CPU ou relógio real. Criar nova timeline reinicia a posição; não oferecer reset, seek ou rewind. Não usar wall clock, timers, sleeps, delays ou velocidades de playback nesta fundação.

## Consequences

- Semântica de replay pode ser testada em memória sem Infrastructure, armazenamento, transporte ou serviços externos.
- Dataset imutável preserva ocorrências e payloads; construção materializa e ordena o histórico, enquanto o orchestrator existente continua lazy.
- Empates completos são estáveis em relação à entrada; não se inventa identidade ou sequence universal.
- Timeline representa até onde o tempo histórico virtual avançou, mas ainda não representa consumo, conclusão de sessão ou cursor de eventos.
- Range pode terminar antes do fechamento de Candle, pois seus limites usam OpenTime; disponibilidade dos dados para evitar look-ahead requer decisão posterior.
- Novas sessões podem ter timelines independentes sobre o mesmo dataset, sem estado global ou promessa de writers concorrentes.
- Application.Tests valida construção, imutabilidade, todos os critérios de ordenação, duplicatas, precisão de ticks e limites/monotonicidade sem novos pacotes.

## Alternatives

Task.Delay baseado em timestamps mistura pacing com tempo virtual e torna a posição dependente de execução/relógio real. Ordenação por ReceivedTime reproduziria recepção em vez de ocorrência histórica. Candle.CloseTime é uma alternativa de disponibilidade, mas mudaria a convenção de posicionamento aprovada para esta fundação; não resolver essa política silenciosamente aqui.

Uma hierarquia fechada de Replay seria válida, mas factories e três payloads tipados em classe sealed exigem menos tipos e mantêm os estados inválidos inacessíveis. MarketEvent no Domain ou object público ampliariam ou enfraqueceriam a fronteira canônica.

Deduplicar por igualdade, inventar Guid/sequence para desempate ou ordenar por valores financeiros alteraria a semântica das ocorrências. Formatos/importers ou persistência prematuros acoplariam representação lógica ao transporte/armazenamento.

## Deferred Decisions

HistoricalReplayProvider, sessões e integração com consumers/orchestrator, disponibilidade de candles fechados e prevenção de look-ahead, cancelamento/exposição assíncrona, importação e formatos, persistência, registro/lifetime em DI, pacing e velocidades, seek/rewind/reset, clocks de estratégias/backtesting, sequences/identidade e deduplicação.

## References

- ADR-006 — Temporal Semantics.
- ADR-007 — Market Data Core Model.
- ADR-008 — Trade Identity and Semantics.
- ADR-009 — Market Data Provider Boundary.
- ADR-010 — Deterministic Simulation Provider.
- ADR-011 — Market Data Orchestration.
- Historical Replay Foundation — TASK 0.14.
