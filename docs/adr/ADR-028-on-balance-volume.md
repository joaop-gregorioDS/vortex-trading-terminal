# ADR-028: On-Balance Volume

Status: Accepted
Date: 2026-10-04

## Context

The deterministic batch Indicator Engine has nine initial technical indicators.
OBV completes this initial set with signed cumulative volume driven by Close
direction. Unlike price-weighted VWAP, OBV uses only the comparison between
consecutive supplied Close values and the current candle's Volume. Its initial
balance requires an explicit convention rather than an arbitrary first-volume
contribution. No new infrastructure or public contract is needed.

## Decision

Place sealed, parameterless `OnBalanceVolume` in Application/Indicators,
implementing `IIndicator`, with `Name = "OBV"`. It has no period or configuration.
Use the existing Domain Candle and return the unchanged immutable IndicatorPoint
and IndicatorSeries types. Only Candle.Close.Value and Candle.Volume.Value
participate in mathematics; High, Low, and Open do not. Callers remain responsible
for supplying closed candles available at their causal execution horizon.

### Complete input validation

Take a local snapshot of candle references and validate the entire snapshot
before any arithmetic. Null collection throws ArgumentNullException for `candles`.
Null candle, mixed InstrumentId, mixed CandleInterval, duplicate OpenTime, and
backward OpenTime throw ArgumentException for `candles`. Coherence uses the
existing value equality. OpenTime must be strictly increasing in supplied order;
do not sort, deduplicate, repair, or introduce additional Domain invariants.
Invalid later candles reject the batch even when an earlier transition would
overflow. Empty valid input returns an empty, non-null series.

### Canonical zero baseline and transition mathematics

The first supplied candle always produces `OBV = 0m`, regardless of its Volume,
including zero or decimal.MaxValue. It establishes only the previous Close.
For each subsequent candle i:

```text
OBV[0] = 0m
Close[i] > Close[i-1]: OBV[i] = OBV[i-1] + Volume[i]
Close[i] < Close[i-1]: OBV[i] = OBV[i-1] - Volume[i]
Close[i] == Close[i-1]: OBV[i] = OBV[i-1]
```

Compare actual decimal Close values directly, without rounding or converting a
price difference into direction. Numerically equal Close values with different
decimal scales still count as equal. Only the current Volume is added or
subtracted, never previous, average, or cumulative raw volume. Equal Close
ignores the current Volume for the balance, even at decimal.MaxValue.

Zero Volume preserves the balance for every direction and still emits a point.
Its Close becomes the next transition's previous Close. The immediately preceding
supplied candle is always used, including after zero Volume or equal Close.

Positive, zero, and negative outputs are valid. Do not clamp or normalize them.
For Close 10, 11, 10 and Volume 500, 1.25, 0.50, outputs are 0, 1.25, 0.75.
For Close 10, 9, 8, 9 and Volume 500, 100, 50, 200, outputs are
0, -100, -150, 50, including natural recovery from negative to positive.

### Output count and time

There is no warm-up. Every valid supplied candle produces exactly one point:
`OutputCount = candles.Count`. A single candle produces one zero point. Each
point's Time is exactly its corresponding Candle.CloseTime, including the first
point and points with unchanged balances. Never use OpenTime, received time,
replay time, or wall clock as output time.

### Decimal precision and overflow

The accumulator and all arithmetic remain decimal. Do not use integer or
floating-point accumulation, explicit rounding, quantization, or scaling.
Fractional volume is preserved under native decimal semantics, including the
minimum positive decimal when added to zero. Decimal has finite precision:
sub-unit contributions to an extreme balance may be absorbed by native
addition/subtraction. This is not arbitrary-precision arithmetic or a guarantee
that every mathematically nonzero contribution changes a representable balance.

Natural overflow in balance addition or subtraction propagates OverflowException
and aborts the call without partial results. No catch, clamp, wrapping, floating
fallback, reset, skip, or reordered calculation is introduced. Both overflow
fixtures are possible with unchanged Domain invariants: Close 10, 11, 12 and
Volume 500, MaxValue, 1 overflow positively; Close 10, 9, 8 with the same Volumes
overflow negatively. Their two-candle prefixes produce MaxValue and MinValue.
Opposite-direction MaxValue volume can cancel an extreme balance back to zero;
raw volume is not separately accumulated. Equal Close at an extreme balance
with maximum current Volume remains unchanged without unnecessary overflow.

### Temporal scope and stateless execution

OBV is cumulative over the entire supplied batch. Gaps of any size compare with
the last supplied Close, without reset, padding, interpolation, or inference of
missing candles. Crossing UTC midnight, date boundaries, or a large gap does not
reset the balance. There is no implicit session, trading calendar, daily reset,
exchange reset, or timezone reset. Callers explicitly choose the batch boundary.

Each Calculate call starts at zero with local state. No balance or previous Close
survives between calls, including after empty input, validation failure, or
overflow. The input collection, candles, prices, volumes, and timestamps are
unchanged. Results use the existing immutable series snapshot and cannot be
changed through source collection replacement, reversal, or clearing.

### Determinism, causality, and complexity

Identical ordered input produces exactly identical decimal representations,
timestamps, and order. No clocks, randomness, external services, asynchronous
work, or retained cache participates. OBV[i] depends only on candles[0..i].
For successful calculations, every valid prefix result equals the corresponding
full-result prefix, including after a maximum-volume future shock. Whole-batch
validation still rejects invalid future input, and overflow aborts the whole
call rather than returning a prefix. Consumers must enforce their own causal
availability boundary before supplying the batch.

Snapshot, complete validation, one forward calculation pass, and result snapshot
construction each take O(n) time. Total memory is O(n) for reference snapshot and
output. No generic cumulative framework, shared comparison engine, shared
validator, or shared volume accumulator is extracted.

## Alternatives

- Seed with first Volume: rejected because it assigns an arbitrary initial
  directional contribution without a preceding Close. Vortex uses zero.
- Use price-weighted or averaged volume: rejected because those define a
  different calculation from current-volume OBV.
- Carry the last nonzero-volume Close: rejected because direction always compares
  consecutive supplied candles, regardless of Volume.
- Clamp negative values or cap overflow: rejected because signed balance and
  explicit failure preserve the specified mathematical behavior.
- Reset at midnight or infer market sessions: deferred to an explicit future
  consumer policy; dates and gaps do not define an implicit batch boundary.
- Introduce incremental state or a generic accumulator: deferred until there is
  an independently reviewed need and contract.

## Consequences and limitations

OBV completes the ten initial indicators while preserving Application placement,
Domain independence, existing single-series contracts, and immutable results.
Tests cover manual signed sequences, fractional and zero volumes, complete
validation before arithmetic, both overflows, gaps, UTC midnight, immutability,
reuse after failures, exact determinism, and prefix invariance.

Absolute OBV depends on the supplied batch's starting point and zero convention;
different historical prefixes need not produce the same later absolute balance.
Finite decimal precision and range remain explicit limitations. This batch API
does not detect incomplete candles, establish external availability, repair
missing data, or support concurrent source mutation.

Charts, Replay/Backtesting consumers, Strategies, and Robots can reuse this
calculation after their contracts and causal boundaries are reviewed separately.
No consumer integration, session abstraction, API, DI, persistence, Redis,
frontend rendering, trading, signals, or additional indicator is introduced.
