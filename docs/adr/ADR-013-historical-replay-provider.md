# ADR-013 — Historical Replay Provider

- Status: Accepted
- Date: 2026-10-02.
- Scope: Segundo adapter de Market Data em Infrastructure, projetando dataset em memória.

## Context

Application possui a porta de Market Data, orchestrator independente de provider e fundação de Replay com dataset imutável ordenado e timeline virtual. Infrastructure possui SimulationMarketDataProvider. Precisamos comprovar que a mesma porta acomoda uma fonte histórica sem alterar consumidores, Domain ou contratos.

O dataset já define ordem heterogênea por ReplayTime, instrumento, kind e ordinal. A porta existente define três operações independentes. Introduzir pacing, carga externa ou timeline compartilhada dentro dessas operações misturaria projeção com controle de uma sessão histórica.

## Decision

HistoricalReplayProvider is an Infrastructure adapter that projects an immutable HistoricalReplayDataset through the existing IMarketDataProvider streams.

It preserves the relative dataset order of matching events, does not pace against wall-clock time, does not own a shared ReplayTimeline and does not modify canonical payloads.

Receber dataset explícito não nulo e expor descriptor historical-replay / Historical Replay, com capabilities Quotes | Trades | Candles. Manter o adapter sem DI, importer, I/O, novos projetos ou pacotes.

## Stream Semantics and Ordering

Validar assinaturas na chamada, copiar IDs e usar HashSet privado apenas para membership. Rejeitar null, vazio e elemento null; normalizar IDs duplicados sem multiplicar ocorrências históricas ou alterar a coleção do chamador.

Percorrer dataset.Events sem nova ordenação. Selecionar o tipo e os instrumentos solicitados, e, para Candle, a igualdade de valor do CandleInterval. Intervalo null é inválido; ausência de tipo, instrumento ou intervalo correspondente completa normalmente um stream vazio.

Emitir as mesmas instâncias dos payloads. Preservar todas as ocorrências, preços, quantidades, timestamps e ReceivedTime. A ordem da assinatura não define a ordem de saída. Não agregar candles, reconstruir envelopes/payloads ou deduplicar eventos.

Retornar projeções IAsyncEnumerable tipadas e lazy, sem coleção intermediária de eventos. Usar iterator privado com EnumeratorCancellation para tokens da operação/enumeração e verificações durante todo o percurso e na conclusão. Cada enumeração possui progresso local e reiniciável.

## Timeline Decision

Não integrar ReplayTimeline ao provider nesta etapa. Streams independentes não podem avançar uma timeline global compartilhada com garantias corretas entre tipos. A futura Replay Session/controlador deverá percorrer o fluxo heterogêneo e coordenar um único tempo virtual.

Não simular passagem de segundos reais, usar relógio de parede, Task.Delay, timers, sleeps ou velocidades de playback. Projeção entrega dados conforme consumo; pacing é uma decisão separada.

## Consequences

- Simulation e Historical Replay implementam a mesma porta com semânticas distintas e sem alteração em MarketDataOrchestrator.
- Dataset permanece fonte de verdade para ordem e ocorrências; o provider faz apenas uma projeção por operação.
- Snapshot privado protege a seleção contra alterações posteriores do chamador; HashSet é consultado sem produzir ordem própria.
- Streams são finitos, lazy, repetíveis e independentes, sem cursor global ou mutação de dataset.
- Cancelamento usa exceções padrão e também se aplica a projeções vazias e à conclusão após o último evento.
- Domain, Application, Replay Foundation, Simulation provider, persistence, API, DI e frontend permanecem inalterados.
- Infrastructure.Tests pode comprovar o comportamento totalmente em memória, inclusive integração com o orchestrator existente, sem novas dependências.

## Alternatives

Ordenar novamente por instrumento ou usar a assinatura como loop externo quebraria a ordem relativa histórica. Materializar coleções filtradas criaria trabalho/buffering antecipados e enfraqueceria a projeção lazy.

Compartilhar ReplayTimeline entre os três streams confundiria seus progressos independentes. Um stream público heterogêneo exigiria alterar a porta aprovada e invadiria a futura responsabilidade de sessão. Task.Delay e velocidade de playback misturariam controle temporal com seleção de dados.

Importers, banco ou arquivos introduziriam dependências externas antes de provar a segunda implementação. Deduplicar ou reconstruir payloads alteraria a história canônica já representada no dataset.

## Limitations

Cada operação garante somente a ordem relativa de seus matches no dataset. Consumo separado de Quote/Trade/Candle não garante uma ordem global entre operações. O provider não representa posição virtual, conclusão de sessão, pausa ou controle interativo.

Candles são projetados com a semântica OpenTime aprovada, sem política de disponibilidade dos valores finais para estratégias. Esta etapa não resolve look-ahead. Cada enumeração percorre o dataset em memória; não há índice/cache por tipo ou instrumento, formato externo ou streaming de armazenamento.

## Deferred Decisions

Replay Session, fluxo heterogêneo e coordenação de timeline, Backtesting/look-ahead, importação e formatos, armazenamento/persistência, registro/lifetime em DI, API/SignalR/frontend, provider registry/fallback, pacing e velocidades, seek/rewind/reset, identidade/sequence e deduplicação.

## References

- ADR-009 — Market Data Provider Boundary.
- ADR-010 — Deterministic Simulation Provider.
- ADR-011 — Market Data Orchestration.
- ADR-012 — Historical Replay Time Semantics.
- Historical Replay Provider — TASK 0.15.
