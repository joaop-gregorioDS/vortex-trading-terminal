# ADR-023: Moving Average Convergence Divergence

Status: Accepted

Date: 2026-10-03

## Context

SMA, EMA, Wilder RSI, and Wilder ATR produce one IndicatorSeries through IIndicator.
The accepted multi-series foundation introduces an independent IMultiSeriesIndicator
and an immutable ordered MultiSeriesIndicatorResult. MACD is its first concrete
implementation. Its three component series have independent warm-ups, so a global
equal-length or equal-timestamp policy would discard legitimate output.

The existing ExponentialMovingAverage consumes Domain Candle. Signal must consume
MACD values, which may be negative or zero and do not represent OHLCV prices.
Fabricating candles would distort Domain and its positive-price constraints.

## Decision and placement

Add only MovingAverageConvergenceDivergence under VortexTrading.Application/Indicators.
It is a sealed, stateless implementation of IMultiSeriesIndicator with Name = "MACD".
Expose read-only FastPeriod, SlowPeriod, and SignalPeriod from constructor arguments.
No default 12/26/9 behavior, concrete consumer, or new package is introduced.

All three periods must be positive. Nonpositive periods throw ArgumentOutOfRangeException
with their corresponding parameter: fastPeriod, slowPeriod, or signalPeriod.
FastPeriod must be less than SlowPeriod; otherwise throw ArgumentException for
fastPeriod, without silently swapping values. Individual positivity checks precede
the ordering check. Maximum positive int periods remain valid when correctly ordered.

Existing contracts, EMA, SMA, RSI, ATR, Domain, replay, Infrastructure, API, DI, and
frontend remain unchanged. No shared validator, generic EMA, or alignment framework
is introduced.

## Fast and Slow EMA, continuity, and MACD Line

Reuse the approved ExponentialMovingAverage implementation twice over the same
validated candle-reference snapshot, once for each period. Each instance uses
Candle.Close, a seed equal to the arithmetic mean of the first N closes, then:

```text
alpha = 2 / (N + 1)
EMA_t = alpha * Close_t + (1 - alpha) * EMA_(t-1)
```

The first Fast point belongs to the FastPeriod-th candle; the first Slow point to
the SlowPeriod-th candle. Fast must evolve from its own seed before Slow exists.
Do not restart Fast at Slow warm-up or seed it from the last FastPeriod candles.

MACD is FastEMA minus SlowEMA for the same supplied candle. The Fast series starts
earlier; index offset SlowPeriod - FastPeriod pairs the matching ordinals, including
across temporal gaps. Equal intervals and strictly increasing OpenTime make each
pair refer to the same CloseTime. No timestamp join, sorting, or generic aligner
is needed. The first MACD point occurs at the SlowPeriod-th candle.

## Signal input, seed, and recurrence

Signal consumes only MACD Line values in their supplied order, never Candle.Close,
Fast, Slow, or Histogram directly. Apply the same Vortex EMA mathematics privately
inside MACD, without changing the approved candle-based EMA:

```text
Signal seed = arithmetic mean of first SignalPeriod MACD values
alphaSignal = 2 / (SignalPeriod + 1)
Signal_t = alphaSignal * MACD_t + (1 - alphaSignal) * Signal_(t-1)
```

This is a deliberate, limited duplication of the canonical EMA mathematics for
a different input. Decimal expression order matches the approved EMA: weighted
products followed by addition, not an algebraically rearranged delta formula.
No artificial Candle, DecimalExponentialMovingAverage, EmaCalculator, kernel,
generic interface, or shared smoother is introduced.

The first Signal point uses the Time of the last MACD point in its seed. Each
later Signal uses the corresponding MACD Time. If fewer than SignalPeriod MACD
points exist, do not sum a partial Signal seed; return an empty Signal series.

## Histogram, stable structure, and warm-up

Histogram is MACD minus Signal only at MACD points where Signal exists. Its Time
equals Signal.Time and the corresponding MACD.Time. Output always contains exactly
three NamedIndicatorSeries in this order:

```text
MACD
Signal
Histogram
```

For C supplied candles:

```text
MACDCount = max(0, C - SlowPeriod + 1)
SignalCount = max(0, MACDCount - SignalPeriod + 1)
HistogramCount = SignalCount
```

Before Slow warm-up, all three series are empty. Between Slow and Signal warm-ups,
MACD is populated while Signal and Histogram remain empty. Empty input still
returns Count = 3 rather than an empty MultiSeriesIndicatorResult.

The first Signal/Histogram belongs to candle ordinal SlowPeriod + SignalPeriod - 1.
For 12/26/9, MACD begins at candle 26 and Signal/Histogram at candle 34. Those periods
are conventional examples, not hard-coded defaults. SignalPeriod one is valid:
Signal equals MACD at every available point and Histogram is zero.

The implementation tests counts instead of computing the combined ordinal in int,
so maximum valid periods do not overflow integer arithmetic or allocate by period.

## Input validation, timestamps, and gaps

Calculate accepts IReadOnlyList<Candle>, takes a local snapshot of references, and
validates the entire batch before warm-up returns or EMA arithmetic. Null collection
throws ArgumentNullException for candles. Null elements, mixed InstrumentId, mixed
CandleInterval, duplicate OpenTime, or backward OpenTime throw ArgumentException
for candles. Instrument and interval coherence use existing value equality.

OpenTime must be strictly increasing. Do not repair, sort, or deduplicate input.
Temporal gaps are valid: all EMAs continue without reset, invented candles, or
padding. Periods count supplied observations rather than elapsed time.

All output Time values originate from the corresponding Candle.CloseTime, directly
through the reused EMA or transitively through MACD. Never use OpenTime, ReplayTime,
ReceivedTime, or wall clock. The existing caller responsibility for supplying closed,
causally available candles remains; MACD introduces no replay or availability dependency.

## Decimal arithmetic and overflow

Fast, Slow, MACD, Signal seed, Signal recurrence, and Histogram use decimal only.
There is no double, explicit rounding, quantization, clamping, fallback, catch,
or partial silent result. Preserve natural OverflowException from seed sums,
weighted recurrences, and both subtractions. Finite intermediate decimal precision
is intentional; arithmetic is not arbitrary precision or externally rounded data.

Tests construct valid Domain candles to exercise Fast seed and Slow seed overflow.
They also isolate Signal seed overflow: Fast(1), Slow(4), Signal(4), four initial
closes of 1 followed by three maximum decimal closes. The four MACD seed values
sum to more than decimal.MaxValue although their mathematical mean would fit.
Signal(5) over that same batch has insufficient warm-up and therefore returns
empty Signal/Histogram without calculating a partial seed.

No independently isolated valid fixture is claimed for recurrence or subtraction
overflow. Fast/Slow are weighted positive-price averages, and their same-time
difference is within decimal range in exact arithmetic; subsequent Signal values
are weighted averages of coherent MACD observations. Extreme finite precision can
still fail naturally. Tests do not invent negative Domain prices, inject arbitrary
MACD points, or modify approved types to force those operation-specific cases.
This coverage limitation does not add any protection or swallowing of overflow.

## Stateless execution, determinism, and prefix invariance

Every Calculate call uses local state and creates immutable points and series.
Source mutation after calculation cannot alter any component. Calls on the same
instance across unrelated batches are independent. Equal ordered candles and periods
produce equal component names, order, decimal values, and timestamps.

EMA state uses only current and earlier observations. Signal consumes current and
earlier MACD points; Histogram uses matching current MACD and Signal. Valid future
candles cannot change existing points in any component. Tests compare all three
series across every prefix of multiple period configurations, including 12/26/9.
Whole-batch invalid input or arithmetic overflow aborts the call, including when
the invalid or overflowing observation is later than an otherwise valid prefix.

Snapshot, validation, two EMA calculations, ordinal pairing, Signal, and result
snapshots each take O(n) time; their composition remains O(n). Memory is O(n) for
input references, EMA outputs, and component snapshots. EMA is not recalculated
for each candle. There are no caches, I/O, clocks, randomness, background execution,
or retained calculation state. Callers should not mutate input during calculation.

## Consequences

- First concrete multi-series indicator uses the accepted additive contract.
- Fast continuity and independent component warm-ups are explicit and testable.
- EMA mathematics remains canonical without changing any approved single-series type.
- Negative and zero MACD/Signal/Histogram values are valid IndicatorPoint values.
- Alignment belongs to MACD; the general result container remains unconstrained.
- Batch snapshots cost O(n) memory; Signal mathematics is locally duplicated.
- Numeric and temporal semantics can be shared by future consumers without frontend formulas.

## Alternatives considered

- Changing IIndicator or returning one multi-value point: rejected to preserve contracts.
- Fabricating candles for Signal: rejected because MACD values are not Domain OHLCV.
- Refactoring EMA into a generic calculator now: deferred to a separate architectural task.
- Resetting Fast when Slow begins: rejected because it changes the MACD calculation.
- EMA initialized from the first sample: rejected in favor of the Vortex SMA seed.
- Using closes for Signal: rejected because Signal is the EMA of MACD Line values.
- Global series alignment, equal counts, or padding: rejected for independent warm-ups.
- Per-candle complete EMA recalculation: rejected because it is unnecessary quadratic work.
- Rounding, double fallback, or overflow clamping: rejected to preserve decimal semantics.

## Limitations and future consumers

This is synchronous finite batch calculation over one coherent closed-candle sequence.
It does not establish external availability, session boundaries, instrument metadata
inside IndicatorSeries, trading signals, or crossover strategy semantics. Conventional
period defaults remain a consumer decision. Different seed policies in external
services may produce different values; no external-provider equivalence is promised.

Charts, Backtesting, Strategies, and Robots may reuse the same calculation and three
series. Rendering, adapters, incremental execution, caching, shared helpers, APIs,
SignalR, trading, risk, persistence, Redis, and DI require separate authorization.
