# Causal Replay Session

## Purpose and placement

`CausalReplaySession` belongs to Application, under `MarketData/Replay`. It is a
synchronous serial pull execution of a `HistoricalReplayDataset`, delivering
complete historical payloads at their availability time. It performs no I/O and
does not require a provider, dependency injection, persistence, or Redis.

`ReplayTime` locates an occurrence in history. Availability determines when its
complete payload can be delivered: the existing `ReplayEventAvailability` policy
uses Quote/Trade `EventTime` and Candle `CloseTime`. The session delegates to that
policy rather than implementing kind-specific rules. `ReceivedTime` is preserved
as payload metadata and does not change this historical policy.

## Private schedule snapshot

The constructor requires a non-null dataset and reads its canonical `Events`.
Each position represents an independent occurrence. For each position, the session
creates a private immutable entry containing the original `ReplayEvent` reference,
an `AvailabilityTime` calculated exactly once through `ReplayEventAvailability`,
and an `OriginalOrdinal` equal to its index in `dataset.Events`.

The constructor materializes one private array sorted by:

1. `AvailabilityTime` ascending.
2. `OriginalOrdinal` ascending.

There is no additional kind or instrument priority. The original ordinal means
the canonical dataset position, not the caller's input collection position.
Ties preserve that existing order; they do not define financial precedence.

Sorting, timeline bounds, and delivery all use the stored availability values.
`TryMoveNext` never recalculates the policy. No dataset reference is retained after
construction, and neither the schedule nor timeline is exposed. The dataset,
events, and payloads remain unchanged. There is no filtering or deduplication:
repeated event references, shared payloads, and structurally equal distinct
payloads each retain every occurrence.

## Virtual time and state

Every session constructs its own `ReplayTimeline` with explicit bounds equal to
the minimum and maximum scheduled availability. The non-empty dataset invariant
guarantees those bounds exist. The session does not use dataset occurrence bounds.

Initially `Position = 0`, `IsCompleted = false`, and `CurrentTime` equals the
minimum availability, without consuming an event. `CurrentTime` delegates to the
private timeline. `EndTime` also delegates to it and means maximum availability;
it can extend beyond `HistoricalReplayDataset.EndTime`.

For example, a dataset containing Candle open at 10:00 with close at 10:05, Trade
at 10:03, and Quote at 10:04 is delivered as Trade at 10:03, Quote at 10:04, then
Candle at 10:05. Initial virtual time is 10:03 and the causal horizon is 10:05,
although occurrence bounds are 10:00 through 10:04.

## Transition and completion

The consumer calls `TryMoveNext(out ReplayEvent?)`. The output is first cleared
to null. If completed, the method returns false without changing state. Otherwise
it reads the scheduled entry at `Position`, advances the private timeline to its
stored availability, increments `Position`, assigns the original event, and
returns true. The output is annotated `NotNullWhen(true)`.

Advancement happens before progress or output assignment. If the timeline throws,
the position does not change, the occurrence is not consumed, and output remains
null. Exceptions are not caught or hidden. The sorted, bounded schedule makes
normal advances valid and monotonic; there is no supported public operation that
invalidates it. This transition guarantee is serial, not concurrent thread safety.

After each successful call, current time equals the stored availability of the
delivered occurrence. Equal availability keeps virtual time unchanged while
position advances once per occurrence. Position counts delivered occurrences;
completion means `Position == schedule.Length`. After the last event, current
time equals `EndTime`. Repeated calls return false and null while position,
completion, current time, and end time remain unchanged.

Returning original events preserves Quote, Trade, and Candle references,
timestamps, and exact decimal values without rebuilding or rounding payloads.

## Independent deterministic execution

Each session owns a separate schedule, timeline, and position. Sessions over the
same dataset can be interleaved independently. Restart means constructing a new
session; it produces the same event reference sequence, current-time values, and
end time. There is no shared cache or global mutable state.

Execution progresses only when the single consumer pulls an occurrence. Reading
state does not advance time or consume events. There are no asynchronous streams,
background tasks, cancellation, synchronization primitives, wall clocks, delays,
timers, pacing, playback speed, seek, rewind, or reset.

## Relationship and limits

The existing `ReplaySession` remains ordered by occurrence and advances at
`ReplayTime`. It is unchanged. Both session types use the existing generic
`ReplayTimeline`; causal execution chooses explicit availability bounds without
changing that timeline, the dataset, events, availability policy, or providers.

The entire finite dataset and schedule are in memory. Schedule construction uses
O(n) additional storage and O(n log n) sorting; each pull uses O(1) schedule access.
This policy describes historical complete-payload availability, not live arrival,
latency, revisions, exchange sequencing, or general financial causality. The
schedule does not expose future payloads through the session's public API, but
callers retaining their original dataset can still inspect it directly.

Backtesting, Strategy Engine, indicators, risk, orders, fills, portfolio, and P&L
remain deferred. This foundation provides causal market-data delivery only.

The architectural decision is recorded in
[ADR-017](../adr/ADR-017-causal-replay-execution.md), with status `Proposed` pending
architectural review.
