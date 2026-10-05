# Minimal Causal Replay Indicator Runtime

## Purpose and placement

TASK 0.34 adds `CausalIndicatorReplayRuntime` under
`VortexTrading.Application/ReplayIndicators`. It connects the existing causal
replay session to the checkpointed indicator consumption boundary without
changing either component or the frozen Indicator Engine.

The decision is recorded in
[ADR-030](../adr/ADR-030-minimal-causal-replay-indicator-runtime.md), status
`Proposed` pending architectural review. This implementation does not authorize
staging, checkpoint creation, ADR acceptance, or push.

## Session ownership and observable state

The sealed runtime receives `HistoricalReplayDataset?` and constructs its own
private `CausalReplaySession` from position zero. A null dataset retains the
session's `ArgumentNullException` for `dataset`. An empty source is rejected by
the existing dataset constructor before a runtime can be created; no empty
session semantics are added.

The runtime never exposes its session, retains the source dataset, or rebuilds
buffers by scanning dataset events. `Position`, `CurrentTime`, and `IsCompleted`
delegate directly to the owned session. There is no independent position counter
or clock. Read-only `HasAcceptanceFailure` records only terminal candle acceptance
failure, initially false. No exception object or history is retained.

## Causal prefix rule

Only occurrences returned successfully by the owned session are considered for
acceptance. Candle availability remains `Candle.CloseTime`; Quote and Trade use
`EventTime` under the existing historical availability policy. No new timestamp
policy or ReceivedTime interpretation is introduced.

CurrentTime alone does not prove consumption. Initial position zero can already
be at a candle's availability, and tied availability events arrive one at a time
without advancing virtual time. In a healthy runtime, a context buffer is exactly
the successfully accepted Candle subsequence of the delivered causal prefix.
Do not query `AvailabilityTime <= CurrentTime` over the source dataset.

## Buffers and acceptance

A private dictionary uses `(InstrumentId, CandleInterval)` with existing value
equality. Equivalent interval durations and equivalent instrument IDs address
the same buffer. Different instruments or intervals remain independent, including
when OpenTime is equal across those contexts.

Buffers contain original Candle references in delivery order and retain the
entire accepted prefix. The first candle establishes that context's anchor.
Subsequent candles must have `OpenTime > last accepted OpenTime` in the context.
Validate before append. There is no cloning, sorting, deduplication, replacement,
repair, aggregation, gap filling, or public buffer access.

The causal schedule already orders fixed-duration candles by their CloseTime,
so backward OpenTime is not expected inside one context. Equal OpenTime remains
possible because replay preserves occurrences. The strict guard handles either
violation without adding a backward insertion API or special test hook.

## TryMoveNext and D-03

`TryMoveNext(out ReplayEvent?)` is synchronous and consumes at most one occurrence.
The output starts as null. Quote/Trade return unchanged after the session advances;
they do not create candles, change candle buffers, or run indicator calculations.
A Candle returns only after successful acceptance, using the original event and
payload references. Healthy completion returns false/null without further change.

D-03 uses the approved fail-fast policy. If an incoming OpenTime is less than or
equal to the previous accepted OpenTime in that context:

1. The session has already consumed event N, so Position is N.
2. The invalid candle is not appended; the accepted buffer is unchanged.
3. HasAcceptanceFailure becomes true.
4. The call throws InvalidOperationException and its out event remains null.

The exception identifies replay position, InstrumentId, CandleInterval, incoming
OpenTime, and previous accepted OpenTime. It does not dump financial payloads.
InvalidOperationException describes invalid runtime acceptance state; no dedicated
exception hierarchy is necessary.

Further advancement or calculation throws InvalidOperationException before any
other processing. State inspection remains available. There is no skipped-event
continuation, retry of delivery, rollback, or simulated rewind. This policy also
applies to buffers for which no indicator has been requested.

IsCompleted describes replay delivery, not analytic success. A duplicate final
event can leave IsCompleted true and HasAcceptanceFailure true simultaneously.
Starting another execution requires a new runtime; the same invalid source will
reproduce the same failure.

## Explicit calculation and snapshot

The operation is:

```text
Calculate(InstrumentId, CandleInterval, IndicatorSpecification)
    -> MultiSeriesIndicatorResult
```

Check terminal acceptance failure first. Null instrument or interval causes
ArgumentNullException for the respective argument. A missing context is a valid
empty batch. The caller supplies one specification per request; it is not retained,
registered, or assigned to a candle buffer.

Copy the selected buffer's references into a private Candle array before calling
one internally owned stateless IndicatorBatchCalculator. An empty context supplies
an empty array. The runtime does not advance replay, mutate buffers, duplicate
mathematical validation, or catch/translate boundary exceptions.

Calculate failures, including invalid/null/unsupported specifications, overflow,
and arithmetic failures, leave position, time, buffers, and acceptance health
unchanged. Another request or a later advancement remains possible. Retrying the
same failing request against the same prefix reproduces the failure; future
events do not guarantee recovery because the full prefix is retained.

## Results, warm-up, and timestamps

Return the boundary's MultiSeriesIndicatorResult directly. Store no latest result,
history, cache, or result envelope. Previously returned results remain immutable
as the runtime progresses.

Single-series results retain exactly one `Value` component. Multi-series results
retain their names, order, counts, and independent warm-ups. Empty output is valid;
no zero, null, synthetic point, padding, alignment, or readiness metadata is added.
IndicatorPoint.Time remains the corresponding Candle.CloseTime, with tick precision.
CurrentTime may be later than the last point for a context and never replaces it.
The caller may associate specification/context and observed position/time externally.

## Historical-prefix anchor

The first successfully accepted candle per context is the batch anchor. VWAP
accumulates over this full prefix; OBV starts at zero on its first candle and
continues from there. Gaps, UTC midnight, dates, Quote/Trade, and other contexts
never reset a buffer. This is not market-session semantics.

Do not truncate to a rolling window: EMA, RSI, ATR, MACD, ADX, VWAP, and OBV can
depend on history or the chosen anchor. No MarketSession or TradingCalendar is added.

## Execution and performance limits

The runtime is synchronous, serial, single-owner, and not advertised as thread-safe.
There are no locks, workers, async execution, concurrent collections, clocks, I/O,
or pacing. Do not advance an instance concurrently with calculation.

The existing causal schedule costs O(E log E) construction time and O(E) storage
for E events. Buffers retain O(N) references for N accepted candles. Each requested
context snapshot costs O(n). Engine batch cost remains unchanged: linear for
several indicators, O(n * period) for SMA/Bollinger, and window-dependent for
Stochastic. Recalculating every prefix for multiple specifications can accumulate
quadratic work for fixed periods. No benchmark or production throughput claim is
made. Batch correctness remains the oracle; cache and incremental execution are deferred.

## Verification and deferred concerns

New Application tests exercise causal visibility, tied availability, original
events, independent instrument/interval contexts, all ten specifications against
the boundary for every accepted prefix, independent warm-ups, anchors, duplicate
variants, terminal failures including the final event, true overflow recovery,
configuration errors, completion, determinism, and input/result integrity.

Terminal buffers are not exposed for tests. Acceptance-before-mutation is explicit
in production; tests preserve a pre-failure result and verify the terminal policy.
No backward injection or reflection is introduced. A defensive Bollinger
ArithmeticException remains propagatable, without claiming a public nonconvergence
fixture that has not been established.

Charts, Backtesting, Strategy/Robot engines, orders, portfolio, risk, session/calendar
models, seek/reset/rewind, cache, incremental calculation, registry, subscriptions,
result history, persistence, serialization, API, SignalR, frontend, and DI remain
outside this implementation.
