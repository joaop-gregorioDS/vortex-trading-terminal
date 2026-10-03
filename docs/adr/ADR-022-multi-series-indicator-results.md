# ADR-022: Multi-Series Indicator Results

Status: Accepted

Date: 2026-10-03

## Context

The Indicator Engine has four approved single-series implementations: SMA, EMA,
Wilder RSI, and Wilder ATR. They share IIndicator, immutable IndicatorPoint,
and IndicatorSeries, and require no contract migration.

Future composite indicators can naturally produce multiple related series.
MACD is one example: its line, signal, and histogram are separate outputs that
need not begin at the same time. Forcing these into one IndicatorSeries would
distort the existing contract or require consumer-specific conventions.
This decision adds only result contracts; it does not implement MACD.

## Decision: additive, independent contracts

Keep IIndicator and its IndicatorSeries return type unchanged. SMA, EMA, RSI,
and ATR remain single-series. Add these three production types exclusively
under VortexTrading.Application/Indicators:

```text
IMultiSeriesIndicator
NamedIndicatorSeries
MultiSeriesIndicatorResult
```

IMultiSeriesIndicator exposes exactly:

```csharp
string Name { get; }
MultiSeriesIndicatorResult Calculate(IReadOnlyList<Candle> candles);
```

It does not inherit IIndicator because the two calculation results have different
semantics. It reuses the existing Domain Candle input. No generic indicator
interface or result framework is introduced, and no concrete implementation or
fake production indicator is required in this task.

## Named series, normalization, and pair equality

NamedIndicatorSeries is a sealed immutable record with read-only Name and Series.
Null, empty, or whitespace-only names throw ArgumentException for `name`.
Store Name.Trim(), preserving case and interior whitespace. Do not apply case,
Unicode, or other normalization beyond trimming.

Null IndicatorSeries throws ArgumentNullException for `series`. Store the exact
supplied instance without cloning its points or constructing another series.
An empty IndicatorSeries is valid.

Pair value semantics use the normalized ordinal string name and the existing
equality of the Series component. IndicatorSeries is a sealed class with
reference equality, not structural point equality. Consequently, two pairs
with equal names and the same series instance are equal and have equal hashes;
two distinct series instances with identical point contents remain unequal.
This record is appropriate for the immutable pair and preserves existing series
semantics without modifying IndicatorSeries or promising deep value equality.

Hash codes support normal in-process equality collections, not serialized or
cross-process stable identities. Determinism here concerns observable names,
order, references, and lookup behavior rather than a persisted hash number.

## Ordered collection and snapshot ownership

MultiSeriesIndicatorResult accepts IEnumerable<NamedIndicatorSeries> and consumes
it once to create its own array snapshot. Validate the snapshot, then expose it
through a read-only wrapper as IReadOnlyList<NamedIndicatorSeries> Series.
Count derives from that collection. Empty input yields zero entries without
inventing component series.

- Null collection throws ArgumentNullException for `series`.
- Any null element throws ArgumentException for `series`.
- Duplicate normalized names throw ArgumentException for `series`.
- Name uniqueness uses StringComparer.Ordinal, including case sensitivity.
- Preserve the exact supplied order and NamedIndicatorSeries references.
- Preserve the exact IndicatorSeries references inside each entry.

For example, Secondary, Primary, Tertiary retain that order. Primary and primary
are distinct names, while padded Primary and Primary collide after trimming.
Ordinally different Unicode sequences remain distinct; linguistic equivalence
does not merge names.

Later source additions, removals, replacements, clearing, or reordering cannot
change the result or its lookup. Mutation through generic or nongeneric mutable
collection casts is rejected by the wrapper. MultiSeriesIndicatorResult snapshots
only the collection of named entries; IndicatorSeries continues to own point
immutability. No point snapshot, cloning, sorting, deduplication, or timestamp
rewriting is reimplemented here.

The result itself is a sealed class, consistent with the existing IndicatorSeries
snapshot container. No structural equality between independently constructed
result containers is introduced; determinism is defined by their observable
contents and preserved references.

## Lookup

Provide bool TryGetSeries(string? name, out IndicatorSeries? series). Null, empty,
or whitespace-only lookup names throw ArgumentException for `name`, including
on an empty result. Trim the name and compare using StringComparer.Ordinal.

An exact or edge-trimmed match returns true and the original IndicatorSeries.
An absent name or case mismatch returns false and sets the output to null,
without throwing. All lookup uses the owned snapshot, never the source enumerable.

A simple linear scan is sufficient for the minimal contract and avoids a second
retained name index. Construction is O(k), with a temporary ordinal HashSet for
uniqueness; snapshot storage is O(k), and lookup is O(k), for k named series.
No indicator calculation or point traversal is performed.

## Different lengths and timestamps: no alignment policy

Component series may have different counts, including zero, and different first,
last, or interior timestamps. There is no requirement for common timestamps,
simultaneous warm-up, equal counts, or implicit padding. This allows a future
composite result to retain outputs that become available at different times.

Do not add InstrumentId, CandleInterval, alignment metadata, or cross-series
validation to IndicatorSeries. Do not reinterpret negative values, zero values,
duplicate points, or supplied point ordering permitted by the existing type.
Different component names may refer to the same IndicatorSeries instance.

Future indicator implementations must explicitly define their own calculation,
warm-up, and temporal relationships. This container does not assert that any
mathematical or causal relationship holds between independently supplied series.

## Determinism and scope boundaries

Repeated construction from the same ordered named entries preserves Count,
names, order, entry references, series references, and lookup behavior. All
public data is immutable after construction and source mutation cannot change it.
This is a result foundation, not a temporal calculation or availability check.

There are no replay, provider, Infrastructure, I/O, DI, wall-clock, or randomness
dependencies. No MACD-specific constant, enum, or component name is hard-coded
in production or tests; tests use neutral names. No public multi-value point,
per-timestamp value dictionary, generic result, shared validator, or smoother
is introduced. Existing production, Domain, and packages remain unchanged.

## Consequences

- Single-series contracts and all four approved indicators remain intact.
- Multi-series outputs have one minimal, immutable, ordered representation.
- Names have explicit trimming, uniqueness, case, and lookup rules.
- Ownership is clear: result owns the entry snapshot, each series owns its points.
- Existing series identity and point semantics are preserved without migration.
- Different component warm-ups and times remain representable without alignment.
- No concrete multi-series indicator or consumer adapter is shipped yet.

## Alternatives considered

- Changing IIndicator.Calculate or having IMultiSeriesIndicator inherit it:
  rejected because the returns represent different contracts.
- IIndicator<TResult> or a generic result framework: deferred to avoid enlarging
  the architectural change beyond these three types.
- Multi-value points or per-timestamp dictionaries: rejected in favor of multiple
  existing IndicatorSeries with independent observation times.
- A mutable dictionary as the public result: rejected because ordered immutable
  entries and explicit ordinal name rules are the selected contract.
- Forced equal counts or timestamp alignment: rejected because future components
  can legitimately start at different times.
- Deep series equality, cloning, or metadata changes: rejected to preserve the
  existing IndicatorSeries contract and exact references.
- Case-insensitive names or automatic case normalization: rejected for Ordinal.
- Implementing MACD now: deferred until this foundation is reviewed and checkpointed.

## Limitations and future consumers

Source enumeration and its snapshot must fit in memory. Callers should not mutate
the source concurrently during construction. Lookup is linear; no retained index,
serialization schema, consumer rendering, or composition engine is defined here.
Series-reference equality is not deep financial-data equivalence, and names do
not carry instrument or interval metadata.

Charts, Backtesting, Strategies, and Robots may consume this same result contract.
MACD will require a separate task defining calculation and alignment semantics.
No indicator calculation, API, frontend rendering, trading, risk, persistence,
Redis, or DI integration is authorized by this decision.
