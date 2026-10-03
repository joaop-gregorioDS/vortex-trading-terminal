# ADR-016 — Replay Timeline Explicit Bounds

- Status: Accepted
- Date: 2026-10-02.
- Scope: Generalização de construção da timeline virtual em Application.

## Context and Problem Discovered by TASK 0.18

ReplayTimeline originalmente recebia somente HistoricalReplayDataset, fixando StartTime e EndTime nos limites de ocorrência. ReplayEventAvailability separou disponibilidade de ocorrência: um Candle pode ficar disponível após dataset.EndTime. A TASK 0.18 foi corretamente bloqueada porque AdvanceTo rejeitaria o horizonte causal necessário.

Não cabe à timeline inferir disponibilidade, modificar o dataset ou reconstruir eventos para obter outro range. Precisamos permitir que o proprietário forneça limites explícitos, preservando a reprodução por ocorrência existente.

## Decision

ReplayTimeline supports explicit StartTime and EndTime bounds
independently of HistoricalReplayDataset.

The existing dataset-based constructor is preserved for
occurrence-ordered replay and retains its behavior.

ReplayTimeline remains a generic bounded monotonic virtual
timeline and does not know about occurrence, availability,
market-data kinds, providers or backtesting.

## Constructor Semantics and Validation

Adicionar ReplayTimeline(UtcInstant startTime, UtcInstant endTime), com assinatura não nullable e validação defensiva de null em runtime. Null causa ArgumentNullException para startTime ou endTime. endTime < startTime causa ArgumentOutOfRangeException para endTime, sem DomainException.

Limites iguais são válidos. Preservar exatamente as referências recebidas em StartTime e EndTime, inicializando CurrentTime em startTime. Não reconstruir, normalizar ou realizar aritmética sobre UtcInstant.

## Dataset Compatibility and Delegation

Preservar ReplayTimeline(HistoricalReplayDataset? dataset), incluindo a anotação nullable atual e ArgumentNullException para dataset nulo. Delegar ao construtor explícito usando dataset.StartTime e dataset.EndTime; a validação de dataset ocorre antes da leitura de EndTime. Há uma única fonte de inicialização de limites e CurrentTime.

ReplaySession continua usando a API por dataset sem alteração. ReplayEvent, Dataset, política de disponibilidade e todos os contratos permanecem intactos.

## Advance Semantics

AdvanceTo permanece inalterado: rejeitar null, instantes fora do intervalo fechado e retrocesso; permitir igualdade como no-op e EndTime exato. Falha preserva CurrentTime. O range agora decorre da construção escolhida, sem alterar monotonicidade ou ownership serial.

## Causal Replay Motivation

Uma futura execução causal poderá calcular seu menor/maior AvailabilityTime e fornecer esses limites. Um range 10:03 → 10:05 é válido mesmo quando um dataset de ocorrência termina às 10:04. A timeline não calcula nem consulta AvailabilityTime; recebe apenas instantes.

Esta TASK generaliza a capacidade temporal e não retoma CausalReplaySession, schedule, desempates ou entrega causal. A retomada depende de revisão e checkpoint separados.

## Consequences

- Timeline representa intervalo virtual genérico, limitado e monotônico; dataset é uma fonte possível dos limites.
- Reprodução existente mantém API, instâncias, estado inicial e comportamento de avanço.
- O proprietário escolhe os limites e a razão de execução, mantendo a timeline independente de Market Data.
- Novos testes cobrem construção explícita, validações, referências, avanços, instante único, horizonte mais amplo e limites temporais.
- Apenas ReplayTimeline muda em produção; Domain, Infrastructure, Session, Dataset e disponibilidade permanecem inalterados.

## Alternatives Considered

Alterar dataset.EndTime confundiria ocorrência e disponibilidade. Criar dataset artificial, envelopes sintéticos ou mutar bounds após construção contornaria contratos e permitiria inconsistência. Uma segunda classe temporal duplicaria monotonicidade/validações. Um construtor específico de causal replay acoplaria a timeline a uma semântica que não lhe pertence.

Remover o construtor por dataset quebraria compatibilidade sem necessidade. Duplicar lógica de inicialização entre construtores facilitaria divergência; a delegação mantém uma única regra.

## Limitations and Deferred Decisions

Timeline não fornece relógio real, pacing, cursor, progresso de eventos, reset, seek, rewind, sincronização concorrente ou execução em background. Continua sendo controlada serialmente pelo proprietário.

Adiar CausalReplaySession, availability ordering/schedule, horizonte causal calculado, Backtesting, Strategy Engine, integração com providers, async/cancelamento, DI, API/SignalR/frontend, persistence/Redis.

## References

- ADR-012 — Historical Replay Time Semantics.
- ADR-014 — Replay Session Execution Model.
- ADR-015 — Market Data Availability Time.
- TASK 0.18 — Causal Replay Session Foundation, BLOCKED.
- TASK 0.18-A — Replay Timeline Generalization.
