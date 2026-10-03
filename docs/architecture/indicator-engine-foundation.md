# Indicator Engine Foundation

## Purpose, placement, and consumers

The Vortex Trading Terminal has one deterministic Indicator Engine in
`VortexTrading.Application/Indicators`. Charts, Backtesting, Strategies, and
Robots will reuse its contracts and calculations instead of maintaining separate
frontend or execution-specific formulas. This foundation provides the batch
contract, Simple Moving Average, Exponential Moving Average, Wilder Relative
Strength Index, and Wilder Average True Range; no consumer integration is introduced.

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

## Limits and deferred work

The model is finite batch calculation over a coherent closed-candle sequence.
Callers should not mutate source collections concurrently while supplying a
batch. Future incremental stateful calculation requires its own contract review.

MACD, Stochastic, Bollinger Bands, VWAP, ADX, and OBV remain deferred,
as do indicator caching, consumer adapters, charts, Backtesting, Strategies,
Robots, trading, signals, risk, orders, and persistence. Decimal is the decision
for this SMA; statistical indicators may later justify double internally through
an explicit architectural decision.

See [ADR-018](../adr/ADR-018-indicator-engine-foundation.md), status `Proposed`,
for the architectural decision pending review.
