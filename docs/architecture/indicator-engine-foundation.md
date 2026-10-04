# Indicator Engine Foundation

## Purpose, placement, and consumers

The Vortex Trading Terminal has one deterministic Indicator Engine in
`VortexTrading.Application/Indicators`. Charts, Backtesting, Strategies, and
Robots will reuse its contracts and calculations instead of maintaining separate
frontend or execution-specific formulas. This foundation provides the batch
contract, Simple Moving Average, Exponential Moving Average, Wilder Relative
Strength Index, Wilder Average True Range, Moving Average Convergence Divergence,
Bollinger Bands, Slow Stochastic Oscillator, Volume Weighted Average Price,
and Wilder Average Directional Index;
no consumer integration is introduced.

The engine uses the existing Domain `Candle`, `UtcInstant`, and decimal prices.
It does not introduce another OHLCV model or change Domain, Infrastructure,
replay, providers, API, dependency injection, or frontend.

## Closed-candle responsibility boundary

Consumers must supply only candles whose completed information is already
available. In historical replay, the existing causal execution determines that
availability; the engine does not reference replay types or availability policy.
Indicators receive the candles explicitly selected by the caller and calculate
only from those candles.

The engine cannot establish that a caller has supplied closed candles relative
to an external execution time: there is no clock or execution horizon argument.
This is a responsibility boundary, not an independent availability check.
Consumers must not pass incomplete or future candles when calculating a value
for their current causal horizon.

## Public contracts

`IIndicator` exposes a stable human-readable `Name` and synchronous
`IndicatorSeries Calculate(IReadOnlyList<Candle> candles)`. There is no async,
cancellation, logging, service provider, or configuration framework dependency.

`IndicatorPoint` is an immutable value record with non-null `UtcInstant Time`
and `decimal Value`. Equality uses time and value. Null time throws
`ArgumentNullException`. Negative and zero values are allowed because future
indicators can produce them; this generic type does not enforce price rules.

`IndicatorSeries` owns a read-only snapshot exposed as `Points`, with derived
`Count`. Empty series are valid. Input point order, duplicates, and original
point references are preserved; the series neither sorts nor deduplicates.
Later source collection changes cannot change the snapshot, including through
casts to mutable collection interfaces. Null collections and null points are
rejected with parameter `points`.

Single-series indicators SMA, EMA, RSI, ATR, and VWAP keep `IIndicator -> IndicatorSeries`.
The additive, independent `IMultiSeriesIndicator` exposes `Name` and
`MultiSeriesIndicatorResult Calculate(IReadOnlyList<Candle> candles)` without
inheriting IIndicator. MACD, Bollinger Bands, Stochastic Oscillator, and ADX implement
this multi-series contract, as described below.

`NamedIndicatorSeries` is an immutable pair of trimmed, nonblank, case-preserved
`Name` and the exact supplied non-null `IndicatorSeries`. Pair equality uses the
ordinal name and existing series reference identity, not deep point equality.
`MultiSeriesIndicatorResult` snapshots an enumerable of these pairs into ordered,
read-only `Series`, with derived `Count`. Empty results are valid; null input,
null elements, and duplicate names are rejected. Names are unique using
`StringComparer.Ordinal`, so case differences remain distinct.

`TryGetSeries` trims a nonblank lookup name and returns the exact series reference
using ordinal comparison, or false/null when absent. Lookup is a simple O(k) scan
over k named series. Counts and timestamps may differ between component series;
there is no cross-series alignment, padding, metadata, point sorting, or point
deduplication. IndicatorSeries retains responsibility for point immutability.
See [ADR-022](../adr/ADR-022-multi-series-indicator-results.md), status `Proposed`.

## Deterministic batch SMA

`SimpleMovingAverage(int period)` implements `IIndicator`, exposes read-only
`Period`, and always reports `Name = "SMA"`, without including its period.
Nonpositive periods throw `ArgumentOutOfRangeException` for `period`.

Each calculation takes a local snapshot of the supplied candle references,
validates it, and computes a new result without modifying or cloning candles.
There is no retained calculation state, cache, or global mutable state. Reusing
one indicator for another batch does not retain the previous window.

For period N and the candle at index t:

```text
SMA_t = (Close[t-N+1] + ... + Close[t]) / N
```

Only `Candle.Close.Value` contributes. Selectable Open, High, Low, TypicalPrice,
and WeightedClose inputs are deferred.

## Warm-up and output timestamps

No point is emitted before N candles are supplied. Empty input or fewer than N
valid candles returns a non-null empty series, with no partial averages, padding,
or exception for insufficient data. Exactly N candles produces one point;
otherwise output count is `candles.Count - N + 1`.

Every point is timestamped with `CloseTime` of the last candle in its window,
never OpenTime or a wall clock. For closes 10, 20, 30, 40, 50 and period 3, output
values are 20, 30, 40, at the third, fourth, and fifth candle close times.

## Input and temporal validation

`Calculate(null)` throws `ArgumentNullException` for `candles`. Null elements,
mixed instruments, mixed intervals, duplicate OpenTime, and backward OpenTime
throw `ArgumentException` for `candles`. Instrument and interval coherence use
their existing value semantics, not reference identity.

Validation covers the entire supplied snapshot before the insufficient-data
return or arithmetic. Invalid input is not silently accepted during warm-up.
OpenTime must be strictly increasing in the supplied order; the engine does not
sort, deduplicate, normalize prices or timestamps, or automatically group series.

Temporal gaps are allowed and do not reset or pad a window. Period counts supplied
candles, not elapsed intervals. This foundation does not detect missing bars,
require contiguity, or add an overlap constraint beyond strictly increasing
OpenTime. All intervals in one calculation must be equal, so their close times
also remain strictly increasing.

## Decimal arithmetic and rolling complexity

SMA uses decimal throughout, with the precision and range of .NET decimal.
There is no double conversion, explicit rounding, price quantization, or fallback.
Nonterminating averages use the decimal division result. Finite precision can
round intermediate sums and divisions; this is not arbitrary-precision arithmetic.

The rolling sum removes the outgoing close before adding the next close. This
keeps the sum within the current N-candle window and avoids an unnecessary
transient N+1-candle overflow. Overflow in the actual sum propagates as
`OverflowException`, even if the mathematical mean would fit. Valid Domain
candles with maximum decimal prices exercise this behavior without Domain changes.

Snapshot, validation, and rolling calculation are O(n); each candle requires at
most one addition and one subtraction. Result snapshot construction is also
linear. Memory is O(n) for the input reference snapshot and output; it is not an
incremental constant-memory execution API.

## Determinism, immutability, and no look-ahead

The same ordered input and period produce the same values, timestamps, and order.
No random values, wall clock, pacing, background tasks, filesystem, network, or
external data source participates. Returned points and series remain immutable
after source collection changes.

Each SMA window uses only its current and preceding N-1 close prices.
Appending valid later candles does not change existing points. A prefix test
compares `Calculate(C1,C2,C3)` with the first point of
`Calculate(C1,C2,C3,C4)` for SMA(3). Whole-batch validation still rejects an invalid
later candle, and arithmetic overflow aborts the calculation instead of returning
partial output.

## Implemented indicators

- `SimpleMovingAverage`: decimal rolling SMA of Candle.Close, as described above.
- `ExponentialMovingAverage`: decimal EMA using an SMA seed over the first N
  Candle.Close values. Its first point is emitted at the Nth Candle.CloseTime.
  Subsequent points use `alpha = 2m / (N + 1m)` and
  `EMA_t = alpha * Close_t + (1m - alpha) * EMA_(t-1)`.
- `RelativeStrengthIndex`: decimal Wilder RSI of consecutive Candle.Close
  changes. Gain is `max(change, 0)` and loss is `max(-change, 0)`. Seed averages
  use the first N gains/losses, requiring N+1 supplied candles. The first point
  belongs to the (N+1)th Candle.CloseTime. Later averages use
  `(previous average * (N - 1) + current gain/loss) / N`, not EMA alpha.
- `AverageTrueRange`: decimal Wilder ATR. The first supplied candle's TR is
  `High - Low`; later TRs are `max(High - Low, abs(High - PreviousClose),
  abs(Low - PreviousClose))`, using the immediately preceding supplied close.
  Seed is the arithmetic mean of the first N TRs, requiring N candles rather
  than RSI's N+1. The first point belongs to the Nth Candle.CloseTime. Subsequent
  points use `(previous ATR * (N - 1) + current TR) / N`, not EMA alpha.

EMA uses the same full input validation before warm-up, immutable result types,
closed-candle responsibility boundary, and stateless batch execution. Gaps do
not reset it. Calculation is O(n); decimal precision is finite, including alpha
and recursive products, and overflow propagates. SMA seed is computed directly,
without invoking the SMA implementation. See
[ADR-019](../adr/ADR-019-exponential-moving-average.md), status `Proposed`.

RSI computes `RS = AverageGain / AverageLoss` and `100 - 100 / (1 + RS)`.
Zero loss with positive gain produces 100; zero gain with positive loss produces
0; both averages zero produces 50, the explicit Vortex flat-market convention.
The range is 0..100 without clamping. At most N valid candles yields an empty
series after full validation. Period one uses the current change and produces
100, 0, or 50 for an increase, decrease, or flat change, respectively.

RSI preserves the same validation, stateless batch, decimal precision, immutable
results, CloseTime output, gaps, and no-look-ahead boundaries. Calculation is
O(n); arithmetic overflow propagates, including seed sums or relative strength.
See [ADR-020](../adr/ADR-020-wilder-relative-strength-index.md), status `Proposed`.

ATR captures price gaps through the previous-close distances; temporal gaps
remain valid without resetting state or inserting candles. Period one reproduces
each TR. Valid outputs are nonnegative without clamping. The same full validation,
CloseTime timestamps, immutable results, stateless batch, O(n) calculation,
decimal precision, overflow propagation, and prefix invariance apply. TR stays
private to ATR; no shared Wilder smoother or indicator validator is introduced.
See [ADR-021](../adr/ADR-021-wilder-average-true-range.md), status `Proposed`.

## Deterministic batch MACD

`MovingAverageConvergenceDivergence(fastPeriod, slowPeriod, signalPeriod)` implements
`IMultiSeriesIndicator`, reports `Name = "MACD"`, and always returns three series in
the order `MACD`, `Signal`, `Histogram`, including three empty series for empty input.
Periods are positive and FastPeriod must be less than SlowPeriod; invalid individual
periods throw `ArgumentOutOfRangeException` for their parameter, and invalid ordering
throws `ArgumentException` for `fastPeriod`. No default periods are imposed.

Fast and Slow reuse the unchanged `ExponentialMovingAverage` over `Candle.Close`.
Fast evolves from its own seed; corresponding candle ordinals align it with Slow.
`MACD = Fast EMA - Slow EMA` starts at the SlowPeriod-th candle. Signal applies
the same Vortex EMA mathematics directly to MACD values: SMA seed of the first
SignalPeriod MACD points, then alpha `2m / (SignalPeriod + 1m)` and the canonical
weighted recurrence. No artificial candles or generic EMA helper are introduced.
`Histogram = MACD - Signal` exists only where Signal exists. Signal and Histogram
first appear at candle ordinal `SlowPeriod + SignalPeriod - 1` and share the
corresponding MACD point's Time. Every Time comes from Candle.CloseTime.

For C candles, MACDCount is `max(0, C - SlowPeriod + 1)`, SignalCount is
`max(0, MACDCount - SignalPeriod + 1)`, and HistogramCount equals SignalCount.
SignalPeriod one makes Signal equal MACD and Histogram zero. Insufficient Signal
warm-up leaves Signal/Histogram empty without calculating a partial Signal seed.
Different component lengths are intentional; alignment is private to MACD.

Full snapshot validation precedes all warm-up returns and arithmetic. The existing
input coherence, strict OpenTime ordering, temporal gaps, immutable results,
stateless batch, and closed-candle responsibility boundaries apply. Decimal-only
calculation is O(n), with O(n) memory. No rounding, clamping, or overflow fallback
is introduced; arithmetic overflow aborts the call. Valid extension preserves all
previous points in every component, subject to whole-batch validation and overflow.
See [ADR-023](../adr/ADR-023-moving-average-convergence-divergence.md), status `Proposed`.

## Deterministic batch Bollinger Bands

`BollingerBands(period, standardDeviationMultiplier)` implements `IMultiSeriesIndicator`
with `Name = "Bollinger Bands"`. Period and decimal multiplier K must be positive;
otherwise throw `ArgumentOutOfRangeException` for their respective parameter.
The unchanged `SimpleMovingAverage(Period)` supplies Middle exactly. For each window,
population variance is `sum((Close - Middle)^2) / Period`, using direct deviations
rather than `E[x^2] - E[x]^2`. Upper/Lower are `Middle +/- K * sqrt(variance)`.
All three series are ordered `Middle`, `Upper`, `Lower` and synchronized in count
and Time. They begin at the Period-th Candle.CloseTime; each count is
`max(0, candles.Count - Period + 1)`. Empty/warm-up input retains three empty series.

Square root is a private decimal Newton-Raphson implementation: zero returns zero,
negative input is rejected, and the initial estimate is variance for variance >= 1,
otherwise 1. Each iteration uses `current / 2m + (variance / current) / 2m` to avoid
an unnecessarily overflowing numerator sum. The primary criterion returns next when
its absolute difference from current is <= `1e-27m`, including fixed points.
Otherwise retain one previous approximation and recognize only `next == previous`
with `current != next`, the finite-decimal two-cycle `A -> B -> A`.
Choose between current and next by minimum representable `Abs(x - variance / x)`;
only an exact error tie selects the smaller candidate. This avoids unnecessary
candidate squaring. After 128 iterations without primary convergence or two-cycle
stabilization, throw ArithmeticException. The bound remains defensive and tolerance
unchanged; there is no generic cycle detector or universal squared-residual guarantee.
The valid window 50, 400, 5 has a representational cycle with step `1e-26` and now
produces all three bands normally. Tests cover minimum-error selection in both
numeric directions, exact determinism, prefix invariance, and stateless reuse.

All arithmetic stays decimal: no floating-point conversion, explicit rounding,
clamping, or negative-variance repair. Natural overflow propagates; subminimum squared
deviations may underflow under decimal semantics. Full input validation precedes
warm-up and arithmetic. Gaps do not reset windows; results are immutable and batches
stateless, deterministic, and prefix invariant when calculation succeeds. Direct
variance takes O(n * Period) time; bounded Newton and O(n) snapshots require no shared
math library or statistics framework. See
[ADR-024](../adr/ADR-024-bollinger-bands.md), status `Proposed`.

## Deterministic batch Slow Stochastic Oscillator

`StochasticOscillator(kPeriod, kSmoothingPeriod, dPeriod)` is the canonical Slow
Stochastic, implementing `IMultiSeriesIndicator` with `Name = "Stochastic Oscillator"`.
The three periods are read-only, positive, and independently validated; 14/3/3 is
a conventional configuration rather than mandatory defaults.
The existing Domain Candle supplies High, Low, and Close. For each KPeriod window:

```text
HighestHigh = maximum High within the window
LowestLow = minimum Low within the window
Raw %K = ((last Close - LowestLow) / (HighestHigh - LowestLow)) * 100m
Slow %K = SMA of the last KSmoothingPeriod Raw %K values
%D = SMA of the last DPeriod Slow %K values
```

When HighestHigh == LowestLow, Raw K is exactly `50m`, the neutral range policy.
Raw K remains a private intermediate sequence, never a public third series.
The two outputs are always ordered `%K`, `%D`, including empty series during warm-up.
First Raw K belongs to candle K, first public K to K + S - 1, and first D to
K + S + D - 2. For 14/3/3, public K starts at candle 16 and D at candle 18.
For C candles, K count is `max(0, C - K - S + 2)` and D count is
`max(0, C - K - S - D + 3)`, evaluated through independent stage sizes to avoid
combined integer period overflow. Different public lengths during warm-up are
intentional: do not pad, delay K, or duplicate D. Each point uses its window-ending
Candle.CloseTime; D index j aligns with K index j + DPeriod - 1.

Raw normalization divides before multiplying by 100 to avoid unnecessary overflow.
All arithmetic is decimal, without rounding, clamping, or floating-point conversion.
Private direct window means avoid outgoing-sum drift and synthetic candles; existing
SMA and public contracts stay unchanged. Valid outputs remain 0..100, and natural
OverflowException propagates. Positive Candle OHLC invariants prevent a valid
range-subtraction overflow fixture; intermediate bounded sums also fit decimal.

Full reference-snapshot validation precedes warm-up and arithmetic. Existing
coherence, strictly increasing OpenTime, valid gaps, immutable results, stateless
batch, determinism, closed-candle responsibility, and prefix invariance apply.
Direct extreme search is O(n * KPeriod); direct intermediate means add
O(n * (KSmoothingPeriod + DPeriod)), with O(n) memory. No shared rolling-extreme,
intermediate SMA, or statistics abstraction is introduced. See
[ADR-025](../adr/ADR-025-slow-stochastic-oscillator.md), status `Proposed`.

## Deterministic batch VWAP

Sealed `VolumeWeightedAveragePrice` implements `IIndicator`, reports `Name = "VWAP"`,
and has no period parameters. Its price input is H/L/C TypicalPrice, weighted by
`Candle.Volume.Value`. Private decimal arithmetic preserves `(High + Low + Close) / 3m`
whenever the left-to-right sum succeeds. Only artificial overflow in that sum
uses the fallback `(High / 3m + Low / 3m) + Close / 3m`. Ordinary flat prices 1
and 2 retain exactly 1 and 2 rather than the last-digit differences from split
division. Maximum decimal prices fit via the fallback; flat MaxValue - 2 instead
produces MaxValue - 3, the explicitly documented finite-precision limitation.

For each candle, accumulate TypicalPrice * Volume, then Volume, in input order.
With positive cumulative volume, Value is cumulative weighted price divided by
cumulative volume. While cumulative volume is zero, emit the current TypicalPrice.
Initial zero-volume candles receive no retroactive weight; zero volume after a
positive prefix preserves Value and advances Time. There is no warm-up: one point
per supplied candle, always at that candle's CloseTime, and empty input is empty.

VWAP is cumulative over the whole supplied batch, including gaps of any size and
date changes. No implicit session, daily, timezone, calendar, or gap reset exists.
Each call starts its own local accumulators. Full snapshot validation, immutable
results, deterministic execution, prefix invariance, and the closed-candle caller
boundary apply. Only current and preceding candles influence each point.

All calculation stays decimal, without explicit rounding, quantization, scaling,
clamping, or floating-point conversion. Natural precision includes product
underflow and multiply/divide round-trip effects. True weighting, weighted-sum,
and volume-sum overflows propagate OverflowException; their failures are never
caught for fallback. Snapshot, validation, a single cumulative arithmetic pass,
and result snapshot are O(n) time and O(n) memory. No shared TypicalPrice helper,
cumulative framework, or session abstraction is introduced. See
[ADR-026](../adr/ADR-026-vwap.md), status `Proposed`.

## Deterministic batch Wilder ADX

Sealed `AverageDirectionalIndex(int period)` implements `IMultiSeriesIndicator`,
reports `Name = "ADX"`, and exposes read-only positive Period with no default.
Nonpositive periods throw ArgumentOutOfRangeException for `period`. Public series
are always ordered `+DI`, `-DI`, `ADX`, including three empty series during warm-up.
TR, directional movement, smoothed sums, and DX remain private.

Beginning with the second candle, TR is the maximum of High-Low and the two
absolute distances from PreviousClose. UpMove is High-PreviousHigh; DownMove is
PreviousLow-Low. Only a positive move strictly greater than the opposing move
contributes to its DM; ties discard both. The first candle contributes no TR,
unlike ATR. Seed each smoothed TR/+DM/-DM with the first N transition sums,
requiring N+1 candles. Later sums use `(previous - previous / N) + current`.
DI use `(smoothed DM / smoothed TR) * 100m`; zero TR makes both DI zero.
Private DX is `(abs(+DI - -DI) / (+DI + -DI)) * 100m`, or zero for zero denominator.
First ADX averages the first N DX; later ADX is `(previous ADX * (N - 1m) + DX) / N`.

DI start at candle N+1; ADX starts at 2N. For C candles, each DI count is
`max(0, C-N)` and ADX count is `max(0, C-2N+1)`, derived in stages without integer
period overflow. For N=14, starts are candles 15 and 28. For N=1, both start at
candle 2 and ADX equals DX. Independent warm-up lengths are not padded or aligned
artificially. Every point uses its corresponding Candle.CloseTime; ADX index j
aligns with DI index j+N-1.

Full snapshot validation precedes warm-up and mathematics. All computation is
decimal, without rounding, clamp, floating-point conversion, or overflow fallback.
DI/DX divide before multiplying by 100. Range expectations are 0..100; sum seeds
retain minimum-positive decimals that prematurely averaged seeds would lose.
Natural overflow propagates, including smoothed TR seed and recurrence sums.
Gaps do not reset smoothing. Private local accumulators, immutable results,
determinism, and prefix invariance apply to all three public series. Snapshot,
validation, one forward calculation pass, and result construction take O(n) time
and O(n) memory. No shared Wilder smoother, TR/DM helper, validator, synthetic
candles, or changes to RSI/ATR are introduced. See
[ADR-027](../adr/ADR-027-wilder-adx.md), status `Proposed`.

## Limits and deferred work

The model is finite batch calculation over a coherent closed-candle sequence.
Callers should not mutate source collections concurrently while supplying a
batch. Future incremental stateful calculation requires its own contract review.

OBV remains deferred,
as do indicator caching, consumer adapters, charts, Backtesting, Strategies,
Robots, trading, signals, risk, orders, and persistence. Decimal is the decision
for this SMA; statistical indicators may later justify double internally through
an explicit architectural decision.

See [ADR-018](../adr/ADR-018-indicator-engine-foundation.md), status `Proposed`,
for the architectural decision pending review.
