# ADR-017: Causal Replay Execution

Status: Accepted

Date: 2026-10-02

## Context

Historical replay occurrence time and complete-payload availability are separate
concepts. A Candle belongs to its open time but cannot be delivered as complete
before its close. `ReplaySession` intentionally follows occurrence time. A
separate causal session must preserve those semantics while providing delivery
at the historical availability defined by `ReplayEventAvailability`.

ADR-016 and checkpoint `13724f9` provide generic explicit `ReplayTimeline` bounds.
The earlier dependency on dataset occurrence bounds is therefore resolved: the
causal horizon can extend beyond the dataset's last occurrence time without
changing existing contracts.

## Decision

```text
CausalReplaySession materializes a deterministic
availability schedule from HistoricalReplayDataset.

Each occurrence receives an AvailabilityTime derived once
through ReplayEventAvailability and retains its original
canonical dataset ordinal.

Delivery order is AvailabilityTime ascending followed by
original ordinal ascending.

Each session owns an independent ReplayTimeline bounded by
the minimum and maximum AvailabilityTime.

The causal horizon may exceed HistoricalReplayDataset.EndTime.
```

The public production addition belongs to Application. Construction rejects a
null dataset. The session does not retain the dataset after building its private
schedule, expose mutable state, or introduce packages or external dependencies.

## Schedule snapshot and calculate-once semantics

Each index in `dataset.Events` is one occurrence. Construction records the
original event reference, the result of exactly one availability-policy call,
and that canonical index in a private immutable entry. The sorted entries are
materialized into a private array exactly once per session.

Ordering, timeline bounds, and consumption use these stored availability values.
The session does not duplicate Quote/Trade/Candle rules or recalculate the policy
in `TryMoveNext`. It does not filter, deduplicate, clone, or mutate events or
payloads. Equal values, repeated event references, and shared payloads remain
distinct occurrences.

## Tie-break rationale

Equal availability follows original canonical dataset ordinal, not raw input
order. No kind or instrument priority is added. Preserving a total existing
occurrence order provides deterministic execution without inventing financial
precedence among simultaneous observations.

## Timeline bounds and transitions

The non-empty schedule supplies minimum and maximum availability to the existing
explicit timeline constructor. Initial position is zero, completion is false,
and current time is minimum availability. `CurrentTime` and `EndTime` delegate
to the private timeline; the latter is maximum availability, independent of
dataset occurrence end.

`TryMoveNext` clears output to null, checks completion, advances to the stored
next availability, increments position, assigns the original event, and returns
true. Progress and output assignment occur only after successful advancement.
An advancement exception is propagated with unchanged position and null output.
Sorted availability and matching bounds guarantee valid normal advances.

Equal times keep the timeline unchanged while counting every occurrence. The
last delivery makes position equal schedule length, completion true, and current
time equal end time. Further calls return false and null without changing state.
This is a serial transition guarantee, not thread safety.

## Determinism and existing ReplaySession

Every session independently owns its schedule, timeline, and position. Restart
requires a new session over the dataset and reproduces event reference order,
current-time values, and end time. No cache or global mutable state is used.

`ReplaySession` remains occurrence-ordered. `ReplayTimeline`, the dataset, event
types, availability policy, providers, Domain, and Infrastructure remain
unchanged. The new session is synchronous, serial, and driven by consumer pulls,
without I/O, wall clocks, pacing, or background execution.

## Consequences

- Complete historical payloads are delivered only at their scheduled availability.
- All ordering and execution decisions use one materialized availability snapshot.
- The causal horizon can exceed the occurrence horizon, including Candle closes.
- Duplicate occurrences, payload identity, timestamps, and decimals are preserved.
- Construction adds O(n) memory and O(n log n) sorting; each pull accesses one entry.
- Two separate session types make the occurrence/availability distinction explicit.
- The full schedule must fit in memory, and one consumer must execute each session.

## Alternatives considered

- Modify `ReplaySession`: rejected because it would change approved occurrence
  semantics and existing consumers.
- Bound execution by the dataset: rejected because late Candle availability may
  fall outside occurrence bounds.
- Recalculate availability during ordering or consumption: rejected because the
  constructor snapshot must supply the same values for all execution decisions.
- Add kind or instrument priorities: rejected because no such financial ordering
  has been approved; canonical ordinal already provides deterministic ties.
- Async execution, pacing, shared caches, or thread safety: deferred because this
  finite in-memory serial pull session needs none of them.

## Limitations and deferred decisions

Historical availability is not live received-time ordering, network latency,
corrections, exchange sequencing, or a full financial-causality model. Ties are
mechanical, not financial precedence. The session exposes no future payloads,
but callers holding the source dataset can still inspect that dataset directly.

Seek, rewind, reset, streaming datasets, cancellation, playback speed, concurrent
consumers, and live arrival policies are outside this decision. Backtesting,
Strategy Engine, indicators, risk, orders, fills, portfolio, and P&L are deferred.
No API, frontend, provider, DI, persistence, or Redis integration is introduced.
