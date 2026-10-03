# ADR-019: Exponential Moving Average

Status: Accepted

Date: 2026-10-03

## Context

The approved Indicator Engine Foundation supplies one Application-layer batch
contract, immutable point and series types, closed-candle input responsibility,
and a decimal SMA reference implementation. EMA adds the first recursive
indicator. Its initialization must be explicit because seed conventions can
produce different values despite using the same recurrence.

The EMA convention established here will be required by a future MACD rather
than allowing a separate initialization policy hidden inside that indicator.
No MACD or consumer integration is introduced by this decision.

## Decision

```text
Vortex Trading Terminal defines EMA using an SMA seed
over the first N Candle.Close values.

The first EMA point is available at the CloseTime of
the Nth Candle.

Subsequent values use:

alpha = 2 / (N + 1)

EMA_t =
alpha * Close_t +
(1 - alpha) * EMA_(t-1)

The implementation uses decimal arithmetic and the
existing IndicatorPoint / IndicatorSeries contracts.
```

Add only `ExponentialMovingAverage` as a production type under
`VortexTrading.Application/Indicators`, implementing the existing `IIndicator`.
Its name is exactly `EMA`, and its read-only positive period is supplied through
the constructor. Nonpositive periods throw `ArgumentOutOfRangeException` for
`period`. Only Candle.Close is used; no configurable price selector is added.

## SMA seed rationale and first output

Average the first N close prices directly inside EMA. This produces an explicit
N-observation initialization rather than treating the first close as a complete
seed or exposing partial warm-up results. Do not call `SimpleMovingAverage` as
a black box, allocate an intermediate SMA series, or couple indicator
implementations.

The first EMA point is this seed at the Nth Candle.CloseTime. Fewer than N valid
candles returns a non-null empty series, and exactly N produces one point.
No padded or partial averages are emitted.

## Alpha, recurrence, and timestamps

Calculate alpha as `2m / (Period + 1m)` so both division and denominator addition
use decimal, including periods near the maximum integer value. Calculate each
subsequent point as `alpha * Close + (1m - alpha) * previousEma`, without a
rearranged recurrence or explicit rounding. For period one, alpha is one and
every output reproduces its corresponding close price.

Every point uses the corresponding Candle.CloseTime, never OpenTime, replay
time, or a wall clock. For period three and closes 10, 20, 30, 40, 50, seed is 20,
alpha is 0.5, and subsequent values are 30 and 40. For period four and closes
10, 20, 30, 40, 50, 10, seed is 25, alpha is 0.4, and outputs are 25, 35, 25.

## Decimal arithmetic and overflow

Close prices, seed sum, seed, alpha, recurrence, and output remain decimal.
There is no double conversion, explicit rounding, quantization, overflow catch,
or numeric fallback. .NET decimal has finite range and precision: nonterminating
seed or alpha division and subsequent products may round naturally.

The exact weighted expression matters at finite precision. For period two and
closes 1, 2, 3, alpha is `0.6666666666666666666666666667`; the natural decimal
weighted products and sum produce `2.5000000000000000000000000001` after the
seed 1.5. This result is retained, not rounded to a price or asserted through
floating-point tolerance.

Arithmetic overflow propagates, including seed accumulation overflow from valid
Domain candles. A sum may overflow even if an abstract mathematical mean would
fit. No changes to Domain invariants are required to test this behavior.

## Validation consistency and gaps

Each call takes a local snapshot of input candle references without cloning or
modifying candles. The entire snapshot is validated before warm-up or arithmetic,
using the same public invariants as SMA:

- Null collection throws `ArgumentNullException` for `candles`.
- Null elements, mixed instruments, mixed intervals, duplicate OpenTime, and
  backward OpenTime throw `ArgumentException` for `candles`.
- Instruments and intervals use existing value equality.
- OpenTime must be strictly increasing; no sorting, grouping, or deduplication.
- Gaps remain valid and neither reset EMA nor infer or pad missing candles.

The small validation routine remains private to EMA. Temporary duplication is
accepted to preserve approved SMA and production contracts. A shared validator
extraction requires a separate architectural task.

## Deterministic batch semantics and no look-ahead

Each calculation independently seeds and advances its local recurrence. Reusing
one instance with a different batch carries no previous EMA forward. Returned
`IndicatorSeries` snapshots and points remain immutable after source collection
changes.

Same input and period produce the same values, timestamps, and ordering. Each
point depends only on closes up to its corresponding candle; appending valid
later candles preserves all common-prefix points. Whole-batch validation still
rejects invalid later input, and overflow aborts calculation without returning
partial output.

The consumer remains responsible for supplying causally available complete
candles. EMA has no clock or execution horizon and cannot independently verify
that responsibility. It has no replay, provider, Infrastructure, I/O, DI, or
randomness dependency.

## Consequences

- One EMA convention is available to all future Indicator Engine consumers.
- Seed, warm-up, alpha, recurrence, and output timestamps are explicit and tested.
- Snapshot, validation, seed, recurrence, and output construction are O(n), with
  O(n) batch storage and no global cache or retained mathematical state.
- Existing indicators, contracts, replay, Domain, and Infrastructure are unchanged.
- Decimal limitations and observable overflow remain part of the numeric contract.
- Private validation duplication is temporarily retained rather than refactoring SMA.

## Alternatives considered

- First-close seed: rejected in favor of an explicit average of N observations.
- Partial warm-up EMA values: rejected because the approved convention first
  emits at the Nth complete candle.
- Calling SMA for initialization: rejected to avoid implementation coupling and
  an unnecessary intermediate series.
- Double arithmetic, explicit rounding, or a rearranged recurrence: rejected
  because the authorized decimal weighted expression defines this convention.
- Shared validator extraction: deferred to preserve existing approved production.
- Incremental state, cache, or per-consumer EMA variants: deferred or rejected in
  favor of one deterministic stateless batch implementation.

## Limitations and future MACD dependency

Complete batches and results must fit in memory. Concurrent source mutation,
incremental seed/state transfer, caching, gap detection, alternative seeds, and
price selectors are outside this decision. The availability boundary depends on
correct consumer input selection.

Future MACD must reuse this SMA-seeded EMA convention and its decimal temporal
semantics. MACD, RSI, other indicators, Backtesting, Strategies, Robots, trading,
orders, risk, persistence, API, frontend indicators, and DI integration remain
outside this task.
