# ADR-014 — Replay Session Execution Model

- Status: Accepted
- Date: 2026-10-02.
- Scope: Execução síncrona heterogênea e tempo virtual em Application.

## Context

HistoricalReplayDataset preserva eventos heterogêneos em ordem canônica e ReplayTimeline permite avanço virtual explícito e monotônico. HistoricalReplayProvider projeta três streams independentes, sem timeline compartilhada e sem garantia de ordem global entre esses streams. Precisamos coordenar uma execução diretamente sobre o dataset, sem recombinar operações de providers.

CurrentTime inicial coincide com StartTime antes de qualquer consumo. Eventos com timestamps iguais e ocorrências repetidas exigem progresso explícito por posição, independente de tempo virtual.

## Decision

ReplaySession is an Application-level, synchronous,
consumer-driven execution cursor over a canonical
HistoricalReplayDataset.

Each session owns one independent ReplayTimeline and
advances virtual time to the ReplayTime of an event before
that event is reported as consumed.

ReplaySession does not use wall-clock time, pacing,
background execution, provider streams, seek or rewind.

## Ownership and Initial State

Receber somente HistoricalReplayDataset explícito, rejeitando null com ArgumentNullException. Manter dataset e uma nova ReplayTimeline privados; CurrentTime delega à timeline. Position começa em zero, CurrentTime em dataset.StartTime e IsCompleted em false. Dataset não é exposto e nenhuma dependência/registro DI é adicionado.

Cada Session representa uma execução serial independente. Não há cursor estático/global, timeline fornecida externamente ou segunda cópia temporal. Restart significa construir outra Session sobre o mesmo dataset imutável.

## Transition Semantics

Expor bool TryMoveNext(out ReplayEvent? replayEvent), com NotNullWhen(true). Havendo evento, obter dataset.Events[Position], avançar timeline para ReplayTime, incrementar Position e retornar a mesma instância com true. Assim, ao retornar, CurrentTime == replayEvent.ReplayTime. Não avançar para o evento seguinte antecipadamente.

Inicializar o parâmetro out com null e incrementar Position somente após AdvanceTo concluir. Falha temporal não reporta consumo nem avança Position. A invariância do dataset e o ownership privado da timeline evitam esse cenário normalmente. Atomicidade lógica não promete thread safety; chamadas à mesma execução são serializadas pelo consumidor.

## Completion and Same-Time Events

Position conta ocorrências entregues. IsCompleted equivale a Position == dataset.Count. O último evento deixa CurrentTime == dataset.EndTime. Depois do fim, chamadas retornam false/null com estado inalterado e sem exceção.

Timestamps iguais não impedem entregas sucessivas: cada ocorrência avança Position, preservando o mesmo tempo virtual e a ordem do dataset. Não reordenar, filtrar, deduplicar, clonar envelopes/payloads ou reescrever timestamps e valores financeiros.

## Consequences

- Uma única execução mantém o fluxo heterogêneo canônico, tempo virtual e progresso coerentes após cada entrega.
- Position distingue início sem consumo, eventos simultâneos e conclusão, sem inferir progresso a partir do relógio.
- Dataset compartilhado é imutável; timelines e posições de Sessions distintas são independentes.
- A API síncrona pull não exige I/O, tokens, agendamento, background loops ou pacing.
- Domain, contratos existentes, Replay Foundation, providers, orchestrator, Infrastructure, persistence, API, DI e frontend permanecem inalterados.
- A execução é serial e irreversível dentro de uma instância; reinício usa nova Session.

## Alternatives

Recombinar streams de providers perderia a coordenação heterogênea e introduziria dependência desnecessária de adapters. Avançar timeline antecipadamente deixaria CurrentTime incompatível com a última ocorrência entregue.

Usar apenas tempo como progresso não distingue eventos simultâneos nem estado inicial. Expor timeline mutável ou Position com setter permitiria quebrar invariantes. IAsyncEnumerable, cancelamento e pacing antecipariam controles sem trabalho assíncrono nesta fundação.

Seek, rewind e reset adicionariam políticas de ciclo de vida e repetição antes da definição de disponibilidade de dados. Locks ou workers não são necessários para a execução serial solicitada.

## Look-Ahead Limitation

A Session preserva a semântica existente: ReplayTime de Candle é OpenTime, embora o payload já contenha OHLCV final. Coerência temporal da entrega não representa autorização para uma estratégia observar esses valores em OpenTime. Não conectar Strategy Engine ou Backtesting antes de definir disponibilidade e política contra look-ahead.

## Deferred Decisions

Disponibilidade de Market Data/candles e look-ahead, Backtesting/Strategy Engine, controllers, async/cancelamento, pacing/playback speeds, seek/rewind/reset, concorrência/background execution, integração com providers, DI, API/SignalR/frontend, persistence/Redis, event identity, sequence e deduplicação.

## References

- ADR-012 — Historical Replay Time Semantics.
- ADR-013 — Historical Replay Provider.
- Replay Session Foundation — TASK 0.16.
