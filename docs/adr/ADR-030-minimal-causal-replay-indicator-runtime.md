# ADR-030: Minimal Causal Replay Indicator Runtime

Status: Accepted
Date: 2026-10-04

## Context

The Indicator Engine is frozen at eacc79e and the minimal consumption boundary is
checkpointed at a8c6e01 under Accepted ADR-029. Approved TASK 0.33 selected causal
replay as the first real consumer and defined a bounded integration, preserving
all existing Replay and Indicator contracts.

CausalReplaySession delivers by availability and canonical ordinal, but its
initial CurrentTime and equal-time ties do not prove event consumption. Replay
preserves duplicate occurrences while indicator batches require unique, strictly
increasing Candle.OpenTime in one instrument/interval context. The model has no
candle revision or replacement semantics.

## Decision

Add one sealed CausalIndicatorReplayRuntime under Application/ReplayIndicators.
Receive an existing historical dataset and create a fresh private causal session
from position zero. Do not accept or expose an externally mutable/advanced session.
Delegate Position, CurrentTime, and IsCompleted to that session. Expose only one
additional read-only state flag, HasAcceptanceFailure, for terminal acceptance failure.

Maintain private buffers keyed by (InstrumentId, CandleInterval), using existing
value equality. Accept only Candle payloads actually delivered by the owned session.
Preserve original references, delivery order, and the full accepted context prefix.
Quote/Trade advance and return as original events without changing candle buffers
or automatically calculating anything.

## Strict acceptance and D-03

The first candle establishes a context's anchor. Every later candle must have
OpenTime greater than the last accepted OpenTime. Validate before mutation.
Apply the approved D-03 fail-fast policy without considering object identity,
payload equality, envelope identity, or ordinal as reasons to merge occurrences.

On acceptance failure, the session has already consumed event N. Keep Position N
and the existing time; do not append the invalid candle. Set HasAcceptanceFailure
and throw InvalidOperationException containing position, instrument, interval,
incoming OpenTime, and previous accepted OpenTime. Keep the out event null.
The exception from that operation is the authoritative diagnosis; retain no
exception history and add no error framework.

After this failure, both advancement and calculation throw immediately without
changing state. Read-only state remains available. Do not skip, replace, merge,
deduplicate, retry delivery, or roll back the session. A new runtime is required
for a new execution. D-03 applies even to contexts not currently requested for calculation.

Replay completion and runtime health are independent: a duplicate final occurrence
can make IsCompleted true while HasAcceptanceFailure is also true.

## Calculation-on-demand

Provide Calculate(InstrumentId, CandleInterval, IndicatorSpecification) returning
the existing MultiSeriesIndicatorResult. A request never advances the session.
Check terminal acceptance state first; reject null instrument/interval arguments.
A context with no accepted candles delegates an empty batch to the boundary.

Snapshot the selected buffer into a private Candle array without cloning payloads.
Own one stateless IndicatorBatchCalculator internally. The caller supplies one
specification per request; retain neither specifications nor results. Construction,
dispatch, mathematical validation, and single/multi conventions remain in the
unchanged boundary and frozen engine.

Propagate legitimate calculation exceptions unchanged, including argument errors,
unsupported specifications, overflow, and arithmetic failures. A calculation
failure leaves position, time, buffers, and acceptance health unchanged. Further
requests and advancement remain allowed. Do not return a stale or empty fallback.

## Anchors, outputs, and ownership

The first accepted candle per context anchors the entire batch for that execution.
VWAP accumulates over that prefix and OBV begins at zero there. Gaps, UTC midnight,
date changes, Quote/Trade, other instruments, and other intervals do not reset it.
This is historical-prefix semantics, not a market session.

Return the boundary result directly. Preserve component names/order, independent
warm-ups, immutable points, and Candle.CloseTime timestamps. Do not align, pad,
rewrite time, synthesize values, or create a result envelope with execution metadata.
The caller can observe serial runtime state around its request.

## Execution and dependency direction

Run synchronously under one owner, without thread-safety claims, locks, workers,
parallel replay, or concurrent collections. The integration depends on Replay
and IndicatorConsumption; neither component nor Indicators depends on this runtime.
No new project, package, reference, provider, DI, persistence, or API is required.

Accepted buffers grow with historical prefixes. Snapshotting is linear; repeatedly
recalculating prefix batches can accumulate quadratic work for fixed indicator
periods. Keep the frozen batch engine as the correctness oracle. Do not truncate
history, optimize formulas, or introduce cache/incremental execution without
separate evidence and architectural review.

## Alternatives

- Caller-managed buffers: possible, but repeats causal acceptance and failure policies.
- Modify Replay or the consumption boundary: rejected because integration is additive.
- Automatic calculation/subscriptions: rejected in favor of explicit independent requests.
- Configurable duplicate/revision policy: deferred; no revision identity or semantics exist.
- Canonicalization layer: deferred until an explicit source-normalization contract exists.
- Rich analytics runtime, result envelope, public buffers, or exception hierarchy:
  unnecessary for the approved bounded integration.

## Validation

Compare calculations to the existing boundary over explicitly expected delivered
prefixes. Cover initial and empty-context behavior, Quote/Trade, future invisibility,
equal-time delivery, instruments/intervals, equivalent keys, all ten specifications,
warm-ups, cumulative anchors, duplicate variants, terminal failure, final-event
failure, mathematical failure without acceptance failure, stable completion,
determinism, and original input/result integrity.

Do not add buffer escape hatches, reflection, or backward-insertion test APIs.
The session cannot normally deliver backward OpenTime within a fixed-duration
context. A defensive ArithmeticException remains propagated without inventing
a nonconvergent public fixture or modifying the frozen numeric policy.

## Deferred concerns

Charts, Backtesting, Strategy/Robot engines, orders, portfolio, risk, MarketSession,
TradingCalendar, seek/reset/rewind, cache, incremental indicators, registry,
subscriptions, result history, persistence, serialization, endpoints, SignalR,
frontend, and checkpoint creation are outside TASK 0.34.

Keep this ADR Proposed until the architect approves acceptance in a separate checkpoint task.
