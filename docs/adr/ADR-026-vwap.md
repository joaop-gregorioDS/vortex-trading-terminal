# ADR-026: Cumulative batch Volume Weighted Average Price

Status: Accepted
Date: 2026-10-03

## Context

The Indicator Engine supports closed-candle batch calculations. VWAP adds volume
weighting without requiring infrastructure. Domain has no trading calendar or
market-session contract; a date or a gap cannot identify a session reliably.
This decision records the numeric policy before the implementation.

## Decision

Place sealed `VolumeWeightedAveragePrice` in Application/Indicators. It implements
the unchanged `IIndicator`, reports `Name = "VWAP"`, has no period parameters,
and returns the existing immutable `IndicatorSeries` of `IndicatorPoint`.
Callers remain responsible for supplying closed candles.

Snapshot candle references and validate the entire batch before arithmetic:
null collection throws ArgumentNullException for `candles`; null elements,
mixed InstrumentId, mixed CandleInterval, duplicate or backward OpenTime throw
ArgumentException for `candles`. Do not sort, deduplicate, repair, or duplicate
Domain OHLCV invariants. OpenTime must be strictly increasing; gaps are valid.

### Concrete TypicalPrice precision policy

Use High, Low, and Close, excluding Open. Evaluate left to right:

```text
try: TypicalPrice = ((High + Low) + Close) / 3m
only if that sum overflows:
     TypicalPrice = (High / 3m + Low / 3m) + Close / 3m
```

The private helper catches OverflowException only around this mean calculation.
Division by 3 cannot overflow for positive decimal prices, so this catch handles
only artificial intermediate sum overflow. No catch surrounds weighting or
accumulation. There is no precheck using rounded decimal subtraction, no shared
math library, no floating-point conversion, explicit rounding, quantization,
scaling, normalization, or clamping.

Preserve the ordinary direct expression whenever its additions succeed under
natural decimal arithmetic. For flat prices 1 and 2, the chosen values are exactly
1 and 2; unconditional split division would give, respectively,
0.9999999999999999999999999999 and 2.0000000000000000000000000001.
For High=2, Low=1, Close=1, the direct value is
1.3333333333333333333333333333. Finite decimal precision remains visible.

For valid positive prices, each divided term is at most decimal.MaxValue / 3;
decimal.MaxValue is exactly divisible by 3. Thus the fallback's positive partial
sums fit decimal. Flat decimal.MaxValue produces decimal.MaxValue successfully
despite overflowing the direct sum. Flat decimal.MaxValue - 2 produces
decimal.MaxValue - 3: each division loses one third at that magnitude. This is a
documented precision limitation, not repaired or presented as exact-real arithmetic.

### Weighting and zero volume

For each supplied candle, in chronological input order:

```text
WeightedPrice = TypicalPrice * Candle.Volume.Value
CumulativeWeightedPrice += WeightedPrice
CumulativeVolume += Candle.Volume.Value
Value = CumulativeVolume == 0m
      ? current TypicalPrice
      : CumulativeWeightedPrice / CumulativeVolume
```

The weighted addition precedes the volume addition. Each accumulator starts at
zero for every Calculate call, including reuse of the same indicator instance.
The zero-volume prefix emits each current TypicalPrice; later positive volume
does not assign retroactive weight to that prefix. After positive cumulative
volume, a zero-volume candle preserves the VWAP value but advances output Time.

There is no warm-up. Empty input produces an empty series; every valid supplied
candle produces one point at its corresponding CloseTime. Output count equals
input count. OpenTime, wall clock, replay time, or received time never supplies
output Time.

### Batch and temporal semantics

Accumulation spans the whole supplied batch. Do not reset at midnight, across
UTC/local dates, on interval boundaries, or after gaps, regardless of gap size.
Do not infer sessions from Venue, exchange, timezone, date, or calendar. Do not
pad, interpolate, or insert candles. A separate call starts a separate batch.

All arithmetic is decimal with its natural precision and range. Multiplication,
addition, and division may round or underflow; for example, minimum positive
decimal price times minimum positive decimal volume becomes zero. Even a single
positive-volume point uses the actual multiply/divide sequence; its round trip
is not universally exact. No minimum-price repair is applied.
True overflow in TypicalPrice * Volume, CumulativeWeightedPrice + WeightedPrice,
or CumulativeVolume + Volume propagates OverflowException and aborts the call.
Valid Domain fixtures exist for all three: price 2 with maximum volume; two
maximum-price candles with volume 1; and two minimum-positive-price candles
with maximum volume, whose weighted sum fits but volume sum overflows.

Local accumulators, immutable results, no external dependencies, and a single
cumulative arithmetic pass give determinism and no look-ahead. Each point uses
only its current prefix. Valid extensions preserve previous points exactly;
whole-batch validation or arithmetic failure still aborts the entire call.
Reference snapshot, validation, calculation, and result construction each take
linear time: O(n) overall time and O(n) memory. No historical sum is recalculated
per output. Do not mutate the source collection concurrently during snapshotting.

## Alternatives

- Close-only, OHLC4, or HL2 prices violate the chosen H/L/C typical-price policy.
- Unconditional split division avoids sum overflow but changes ordinary values
  unnecessarily; the conditional policy preserves the direct expression.
- Always using the direct sum rejects representable large means unnecessarily.
- Daily/session reset would invent calendar semantics absent from Domain.
- Skipping zero-volume points or arbitrary carry during the initial prefix would
  violate the one-point-per-candle, current-TypicalPrice policy.
- Scaled cumulative averages, overflow fallback, shared helpers, and incremental
  state add unapproved semantics or abstractions.

## Consequences and limits

VWAP is a single-series cumulative batch indicator, not a session-aware execution
API. Decimal range limits can reject a long or heavily weighted valid batch even
when the mathematical final average would fit. The split fallback and ordinary
products/divisions retain finite-precision effects, explicitly tested.
No other indicator, contract, package, Domain model, persistence, Redis, API, DI,
frontend, replay provider, or orchestration changes are needed.

Future session-aware VWAP requires an explicit calendar/session boundary and its
own decision. Future consumers, charts, strategies, backtesting, robots, trading,
and incremental calculation remain deferred; they may consume these immutable
results through separately approved contracts.
