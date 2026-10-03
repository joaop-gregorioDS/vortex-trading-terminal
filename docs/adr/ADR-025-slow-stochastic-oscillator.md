# ADR-025: Slow Stochastic Oscillator

Status: Accepted

Date: 2026-10-03

## Context, placement, and canonical variant

The Indicator Engine already provides SMA, EMA, Wilder RSI, Wilder ATR, MACD,
and Bollinger Bands. Stochastic adds rolling High/Low extremes, normalized Close
position, and two successive finite arithmetic means. The canonical Vortex
variant is Slow Stochastic, parameterized rather than fixed to conventional 14/3/3.

StochasticOscillator lives in VortexTrading.Application/Indicators and implements
the existing IMultiSeriesIndicator. It uses Domain Candle and the unchanged immutable
IndicatorPoint, IndicatorSeries, NamedIndicatorSeries, and MultiSeriesIndicatorResult.
No Domain, Infrastructure, other indicator, public result contract, package, replay,
provider, API, DI, frontend, or persistence changes are introduced.

## Contract and positive periods

The class is sealed and stateless, with Name = "Stochastic Oscillator".
Its constructor accepts int kPeriod, int kSmoothingPeriod, and int dPeriod, exposed
as read-only KPeriod, KSmoothingPeriod, and DPeriod. Each must be strictly positive;
otherwise throw ArgumentOutOfRangeException with the respective constructor parameter.
There are no mandatory defaults, relative ordering constraints, or period caps.

Period one is valid independently at each stage. Smoothing period one makes public
K equal Raw K without additional delay. D period one reproduces Slow K Count, Time,
and Value exactly. All periods one supports both ordinary and degenerate candles
starting at the first CloseTime. Maximum int periods accept short batches without
allocating by period or evaluating potentially overflowing combined period sums.

## Raw K, supplied candles, and moving extremes

For each complete window of the last KPeriod supplied candles, scan their High.Value
and Low.Value directly to find HighestHigh and LowestLow. Read Close.Value only
from the last candle in that window. Open and Volume do not participate.
Old extremes cease to influence Raw K immediately when they leave the window.

The mathematical definition is:

```text
Range = HighestHigh - LowestLow
Raw %K = 100 * (Close - LowestLow) / Range
```

For positive Range, evaluate the decimal expression in this order:

```text
Raw %K = ((Close - LowestLow) / Range) * 100m
```

The valid numerator lies between zero and Range, so division normalizes it to 0..1
before scaling. This avoids an unnecessary overflow from 100 * a large numerator.
A valid near-maximum-price fixture demonstrates that the unsafe multiplication
would overflow while the approved evaluation produces normal 50 or 100 outputs.

## Degenerate range and private intermediate sequence

When HighestHigh == LowestLow, Raw K is exactly 50m, the neutral Vortex convention.
Do not divide by zero, retain the previous value, skip the point, or use 0 or 100.
Only this condition triggers 50; it is not an overflow or invalid-input fallback.
Long constant sequences produce 50 in both smoothed public outputs.

Raw K points carry their corresponding window-ending Candle.CloseTime but remain
a local private intermediate sequence. They are never a third public named series
or a new public API. Existing IndicatorPoint values represent intermediate numbers;
no synthetic Candle, Price, or OHLC model is manufactured to invoke candle-based SMA.

## Slow K and D: private finite window means

Public K and D are:

```text
Slow %K = sum(last KSmoothingPeriod Raw %K values) / KSmoothingPeriod
%D = sum(last DPeriod Slow %K values) / DPeriod
```

A private Smooth method inside StochasticOscillator computes each complete window
directly, summing from oldest to newest and then dividing by the positive period.
It is not shared and does not change SimpleMovingAverage or its established semantics.
Each mean uses only its current intermediate window, with no partial mean or padding.

Direct summation avoids retaining decimal subtraction drift from an outgoing window:
after nonterminating raw values exit, a window consisting entirely of zeros yields
exactly zero. Tests cover this transition at both smoothing stages, manual Raw values
20/50/80 producing Slow K 50, and Slow values 30/50/70 producing D 50.

## Warm-up, lengths, order, and timestamps

Always return exactly two named series in order "%K", "%D", even if both are empty.
Raw K starts conceptually at candle K; public warm-ups are independent:

| Stage | First supplied candle ordinal |
| --- | --- |
| Raw K, private | K |
| Slow K, public %K | K + S - 1 |
| Public %D | K + S + D - 2 |

Here K = KPeriod, S = KSmoothingPeriod, D = DPeriod, and C = supplied candle count.
The lengths are:

```text
RawCount = max(0, C - K + 1)
SlowKCount = max(0, RawCount - S + 1) = max(0, C - K - S + 2)
DCount = max(0, SlowKCount - D + 1) = max(0, C - K - S - D + 3)
```

Sequential stage lengths and insufficient-data checks avoid combined int period sums.
At 14/3/3, candles 13, 14, and 15 yield no public points; candle 16 yields first K,
17 yields second K, and 18 yields third K plus first D. Do not delay K until D exists,
align their initial lengths, or add null, zero, duplicated points, or placeholders.

Each mean inherits Time from the final intermediate point of its window, ultimately
the corresponding Candle.CloseTime. After D starts, its point at index j aligns
with K at index j + DPeriod - 1. New eligible candles produce one point in each
public series with matching CloseTime, while K keeps its legitimate earlier prefix.
Never use OpenTime, ReceivedTime, ReplayTime, or a wall clock for output timestamps.

## Validation, Domain invariants, gaps, and ownership

Snapshot supplied Candle references and validate the entire snapshot before any
warm-up decision or arithmetic. Null collection throws ArgumentNullException for
candles. Null elements, mixed InstrumentId, mixed CandleInterval, duplicate OpenTime,
and backward OpenTime throw ArgumentException for candles. Coherence uses existing
Domain value equality. Do not sort, deduplicate, repair, or repeat OHLC invariants
already enforced by Candle. Prices, timestamps, and input collection remain untouched.

Temporal gaps are valid. Windows count supplied observations rather than elapsed
intervals; no reset, interpolation, inferred candle, or padding occurs.
Public results own immutable snapshots using the existing contracts. Source changes
after calculation cannot mutate either component or the ordered named result.

## Decimal arithmetic, range, and overflow limits

All extremes, subtraction, normalization, raw values, sums, and means use decimal.
No double, float, explicit rounding, quantization, clamp, or fallback is introduced.
Division-before-multiplication determines finite-decimal expression order: a ratio
of 1/3 scaled by 100 can differ from 100/3. Tests freeze the approved ordering.
Finite division or products can round or underflow naturally; this is not arbitrary
precision, and the mathematical formula is not an exact real-arithmetic guarantee.

Valid Candle invariants place Close within High/Low and require positive OHLC prices.
Raw values and arithmetic means therefore remain 0..100 when representable, without
clamping. Tests cover boundaries, fractional values, minimum positive price ranges,
varying datasets, and constant sequences at decimal.MaxValue.

Natural OverflowException is never caught or converted to 50 or another fallback.
However, a valid range-overflow fixture is unreachable with current Domain invariants:
0 < LowestLow <= HighestHigh <= decimal.MaxValue, so their nonnegative difference
and Close - LowestLow cannot exceed decimal.MaxValue. Negative or incompatible
prices must not be injected by bypassing or modifying Domain.

Intermediate values are bounded by 100 and collection/period counts by int.MaxValue.
Each legitimate raw or slow smoothing sum is at most 214748364700m, far below the
decimal limit; D is the resulting mean rather than another unbounded accumulation.
There is no reachable accumulation-overflow fixture under these bounds. Document
this constraint instead of exposing a private helper or inventing invalid inputs.

## Stateless execution, determinism, causality, and complexity

Every call uses local snapshots, extremes, and intermediate points; no state is
retained across batches, insufficient-data results, or validation failures.
Identical ordered input and periods yield exactly equal names, order, decimal values,
and timestamps. Each stage uses only values available at its window-ending candle.
Multiple prefixes and an appended extreme High/Low shock preserve all previous
public points. Invalid later input still rejects the whole batch during validation.
The caller remains responsible for supplying candles closed at its causal horizon.

Let n be input length, R raw output count, L Slow K count, and T D count.
Snapshot and validation cost O(n); direct extremes cost O(R * KPeriod).
Direct private means cost O(L * KSmoothingPeriod + T * DPeriod).
Total time is O(n + R*K + L*S + T*D), bounded by O(n * (K + S + D));
the extreme-search stage satisfies O(n * KPeriod). Memory is O(n) for local
snapshots, intermediate points, and public results. No period-sized allocation
is made for a stage lacking enough input.

## Alternatives, consequences, limitations, and future consumers

Fast Stochastic, fixed 14/3/3 defaults, EMA smoothing, public Raw K, forced alignment,
previous-value carry for zero range, and clamp-based range repair are rejected.
Shared rolling windows, maximum/minimum deques, intermediate SMA abstractions,
validators, statistics frameworks, and incremental execution are deferred.
Direct window means favor simple deterministic current-window arithmetic over
an O(n) rolling sum with retained rounding drift; optimization needs separate review.

Charts, Backtesting, Strategies, and Robots may consume the same K/D implementation.
Trading interpretations, adapters, rendering, API, SignalR, DI, risk, orders,
persistence, Redis, and further indicators remain separately authorized work.
