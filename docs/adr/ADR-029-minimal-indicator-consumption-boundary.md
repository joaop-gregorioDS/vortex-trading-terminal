# ADR-029: Minimal Indicator Consumption Boundary

Status: Accepted
Date: 2026-10-04

## Context

The Indicator Engine Foundation is frozen at
`eacc79e2942780b90a9c9f26f94f0602eec69dc1`, with ten decimal batch indicators and
independent single/multi-series contracts. TASK 0.31 recommended a minimal
consumption boundary before future consumers invent their own construction and
result conventions. No Charts, Backtesting, Strategy, or Robot consumer is
implemented yet.

Name is human-readable and does not identify configuration: SMA(20) and SMA(50)
have the same Name. Consumers need explicit typed configuration and common
structural output while preserving existing mathematical policies.

## Decision

Add only the following production component families under
Application/IndicatorConsumption:

- IndicatorSpecification: an abstract record and ten sealed, typed configurations.
- IndicatorBatchCalculator: explicit constructor mapping and synchronous delegation.

Use get-only int/decimal parameters and structural record equality. Keep the
supported family fixed to SMA, EMA, MACD, RSI, Slow Stochastic, Bollinger Bands,
ATR, VWAP, ADX, and OBV. Expose exact canonical MachineId tokens `sma`, `ema`,
`macd`, `rsi`, `slow-stochastic`, `bollinger-bands`, `atr`, `vwap`, `adx`, `obv`.
Do not derive them from Name/class names or implement aliases, parsing, lookup,
reflection, or registration.

Restrict the base normal constructor with private-protected accessibility and
seal the supported specifications. Records retain a protected copy constructor;
an unsupported subtype using that path is explicitly rejected by the calculator
with NotSupportedException. Dispatch depends on supported concrete types, never
on MachineId, so spoofing a token cannot select an algorithm.

Specifications store parameters without mathematical validation or defaults.
The frozen indicator constructors remain the sole authority for positive
periods, MACD ordering, and multiplier validity. Construct before delegating
the batch; invalid configuration therefore fails before candle access.

Return the existing MultiSeriesIndicatorResult. Adapt each single-series result
to exactly one NamedIndicatorSeries named `Value`, preserving the original
IndicatorSeries reference. Return each multi-series result directly, preserving
its reference, names, order, counts, point references, values, and timestamps.
Do not introduce another result container or attach configuration/context.

No changes to frozen contracts, algorithms, tests, ADRs, or mathematical policies
are permitted. No new project, package, framework/project reference, or DI
registration is required.

## Ownership and errors

The caller owns acquisition, batch assembly, instrument/interval context,
causal availability, session anchors, alignment, and presentation. The calculator
does not consume Replay, filter by time, sort, group, repair, or deduplicate.
Duplicate replay occurrences remain a future integration decision (TASK 0.31
D-03), and duplicate OpenTime rejection propagates unchanged.

The calculator rejects null specification explicitly. With valid configuration,
null candles is rejected by the frozen engine. All configuration, sequence,
overflow, and arithmetic failures propagate without catch, fallback, partial
success, or translation. Empty warm-ups and independent component availability
are successful mathematical results and remain unchanged.

Each calculation creates its configured indicator and retains no state or cache.
Concurrent calls require stable input collections. VWAP/OBV batch anchors and
all existing CloseTime semantics remain the caller/engine responsibilities.

## Consequences

- Consumers can request typed mathematical configurations without knowing classes.
- Configurations distinguish periods, indicator types, and decimal multipliers.
- Single/multi consumers share one existing result shape without engine migration.
- Validation has one mathematical authority, preventing divergent duplicate rules.
- The ten mappings are explicit and reviewed when the supported family changes.
- No data-acquisition, session, temporal, or presentation service is introduced.
- Batch performance and finite decimal behavior remain those of the frozen engine.
- Frozen batch implementations remain reference oracles for any future incremental work.

## Alternatives considered

- Direct construction in every consumer: retained for engine tests/internal uses,
  but rejected as the common future consumer convention because it duplicates
  construction and single/multi handling.
- Rich registry/catalog: deferred until a real discovery or metadata consumer exists.
- String plus arbitrary parameter dictionaries: rejected internally in favor of
  explicit typed configuration.
- Changing IIndicator or adding generic indicator contracts: rejected to preserve
  the frozen foundation.
- New calculation-result envelope: unnecessary because the existing immutable
  multi-series container also represents one series.
- Duplicate specification validation: rejected; constructor authority is retained.
- Public factory, reflection discovery, dynamic DI, or shared instance cache:
  unnecessary for explicit stateless mapping.

## Validation

Compare all ten mappings against direct frozen batch calculations. Cover record
identity, parameter order, decimal precision, single Value adaptation, original
multi outputs, empty/independent warm-ups, timestamps, propagated failures,
statelessness, concurrency, and input/result immutability. Do not modify existing
engine tests or add production injection hooks merely to observe local references.

## Deferred decisions

Serialization, external ID resolution, persisted configuration schemas,
registry/catalog, parameter and warm-up metadata, cache, incremental execution,
consumer-specific errors, Replay integration and duplicate policy, session
calendars, Charts, Backtesting, Strategy/Robot runtimes, API, and frontend remain
outside this decision.

This ADR remains Proposed until architectural review. TASK 0.32 does not authorize
staging, commit, push, or acceptance of this ADR.
