# ADR-024: Bollinger Bands

Status: Accepted

Date: 2026-10-03

## Context and placement

The Indicator Engine has single-series SMA, EMA, Wilder RSI, and Wilder ATR, plus
the composed multi-series MACD. Bollinger Bands adds a finite rolling window with
mean, population dispersion, and three synchronized outputs. It reuses the accepted
IMultiSeriesIndicator and immutable result types in VortexTrading.Application/Indicators.
No existing production type, Domain, Infrastructure, replay, API, DI, or package changes.

## Contract, period, and multiplier

BollingerBands is a sealed, stateless implementation with Name = "Bollinger Bands".
Constructor accepts int period and decimal standardDeviationMultiplier, exposed
as read-only Period and StandardDeviationMultiplier. Both must be strictly positive.
Invalid arguments throw ArgumentOutOfRangeException for period or
standardDeviationMultiplier respectively. Fractional K is valid; there are no
mandatory defaults such as period 20 or K 2. Maximum positive int periods are
accepted without allocating by period when the batch is too short.

## Middle and population variance

Take a local snapshot of Candle references and validate the full sequence. Reuse
the unchanged SimpleMovingAverage(Period) over that snapshot for Middle. Its
rolling sum, finite decimal expression order, Count, Time, and Value are canonical,
so Middle exactly matches the approved SMA rather than an alternative mean routine.
Each output at index j corresponds to supplied candles j through j + Period - 1.

For that window, use Candle.Close.Value and the corresponding Middle value:

```text
Variance = sum((Close_i - Middle)^2) / Period
Sigma = sqrt(Variance)
Upper = Middle + StandardDeviationMultiplier * Sigma
Lower = Middle - StandardDeviationMultiplier * Sigma
```

Divisor N treats the supplied window as the whole population. Sample variance with
divisor N - 1 is not the Vortex policy; the window 2, 4 distinguishes them with
mean 3 and population variance 1 instead of sample variance 2. Period one yields
zero variance and all bands equal the close under the existing SMA semantics.

Compute explicit deviations and their squares per window. Do not use E[x^2] - E[x]^2,
which can cancel near-equal large quantities or overflow when deviations themselves
are small. A test with prices around 1e20 and dispersion +/-1 verifies the direct
formula. No shared variance kernel or optimized rolling-statistics state is added.

## Decimal-only private square root

All mean, deviation, square, accumulation, variance, root, K, and band arithmetic
uses decimal. There is no double/float conversion, Math.Sqrt, external numeric
library, explicit rounding, quantization, or clamping.

SquareRoot remains a private static detail inside BollingerBands. It does not
extend the public surface or create a DecimalMath service, helper, or generic
abstraction. It defends its own domain: negative value throws
ArgumentOutOfRangeException for value, and value zero returns zero immediately.
The direct sum-of-squares variance is nonnegative; do not repair a negative value.

For positive values, the initial approximation is:

```text
current = value >= 1 ? value : 1
```

Use bounded Newton-Raphson, evaluating the average as:

```text
next = current / 2 + (value / current) / 2
```

This decimal formulation avoids an unnecessary numerator overflow from current
plus value/current before division. It is the specified defensive expression;
finite decimal rounding may differ from evaluating a combined numerator.
No logarithm or floating-point approximation seeds the algorithm.

## Precision, convergence, two-cycle stabilization, and explicit failure

Private fixed constants are absolute tolerance 0.000000000000000000000000001m
(1e-27) and maximum 128 iterations. After each next calculation, the primary
criterion is Abs(next - current) <= tolerance: return next without post-rounding.
A fixed point also satisfies this criterion because its iterate difference is zero.
Do not relax the tolerance globally or make it depend on arbitrary values.

If the primary criterion fails, retain one previous approximation and recognize
only next == previous with current != next, the explicit period-two cycle A -> B -> A.
Decimal has finite precision: when the mathematical root lies between adjacent
representable values, Newton may stabilize as A <-> B. This recognized cycle is
numerical stabilization, not algorithmic non-convergence. No history collection,
generic cycle detector, or acceptance of arbitrary periods is introduced.

Both positive cycle values are representable candidates. Select the candidate x
with the smaller representable Newton error Abs(x - value / x). Do not square the
candidate to rank it: x * x could overflow unnecessarily. If current has smaller
error, return current; if next has smaller error, return next. Only when the errors
are exactly equal, return Min(current, next) as a deterministic tie-break.

Otherwise retain current as previous, replace current with next, and continue.
ArithmeticException means 128 updates were exhausted without primary tolerance
convergence (including a fixed point) or explicit two-cycle stabilization.
Do not return the last approximation, zero, or an unvalidated fallback at exhaustion.

Tolerance is a stability criterion between iterates, not a universal bound on
Abs(root^2 - value) or a relative-error guarantee across all magnitudes. Decimal
precision and scale are finite. Exact roots are expected when Newton reaches
that representable value; other roots are finite decimal approximations.

Tests exercise sqrt(0), sqrt(1), sqrt(4), sqrt(9), and sqrt(16) transitively with
positive Domain candle windows, plus fractional roots, sqrt(2), and the minimum
positive decimal variance 1e-28. The sqrt(2) fixture checks a decimal reference,
Newton stability, a residual bound appropriate to that radicand, determinism,
and band ordering without floating-point tolerance or private-method reflection.

The original tolerance-only policy failed for the valid ordinary-price window
50, 400, 5, period 3, with variance 31172.222222222222222222222223. Newton alternates
between 176.55656946775507119032035448 and 176.55656946775507119032035449, separated
by 1e-26, greater than 1e-27. Under the corrected policy the higher candidate wins:
its representable Newton error is zero, while the lower candidate's error is 2e-26.
The window now returns all three bands normally, synchronized at the third close.

Transitive tests prove both directions of these observed cycles, compare candidate
errors, and assert exact band values through Calculate without reflection or a
production hook. The window 1, 431, 5 exercises a cycle where the lower candidate
wins, so neither unconditional Min nor unconditional Max satisfies the tests.
Repeated calculations, extension with future candles, and reuse across unrelated
batches preserve exact decimal results after two-cycle stabilization.

Finite precision and range remain limitations; the tolerance is an iterate-stability
criterion, not arbitrary precision. The 128-iteration bound remains defensive
against genuine exhaustion without any of the accepted stabilization criteria.

## Natural overflow and finite precision limits

Natural OverflowException propagates without catch, conversion, clamping, or
partial silent results from SMA sum, deviations, squares, variance accumulation,
necessary Newton operations, multiplier product, and final band additions/subtractions.
Do not transform overflow into ArithmeticException; that exception denotes
iteration exhaustion in this implementation.

Valid Domain fixtures separately exercise mean accumulation overflow, squared
deviation overflow, sum-of-squares overflow with individually representable squares,
K * Sigma overflow, and Upper overflow with a representable offset. Lower is
allowed to be negative because bands are IndicatorPoint values rather than Price.
No Domain type is changed or invalid candle manufactured to force a failure.

The high-magnitude variance 1e28 with root 1e14 is exercised through closes 1e14
and 3e14, demonstrating successful bounded Newton with large initial estimates.
The private function is not exposed to inject decimal.MaxValue as a radicand:
for a successful nonconstant window the unscaled sum of squares must fit before
division by N >= 2. This limits the largest reachable variance through this formula.

Decimal squared deviations below the smallest positive representable magnitude
can underflow to zero; a test explicitly preserves that behavior without fallback.
Finite rounding also affects the reused SMA and all intermediate operations.
The direct formula avoids cancellation from subtracting raw moments, but it does
not make decimal arbitrary precision or eliminate range limits.

## Output structure, synchronization, warm-up, and time

Always return exactly three NamedIndicatorSeries in order Middle, Upper, Lower,
including three empty series for empty input or fewer than Period valid candles.
Each band has max(0, candles.Count - Period + 1) points. The first output belongs
to the Period-th Candle.CloseTime; each subsequent point uses the last supplied
candle's CloseTime in its window, through the canonical SMA point.

All three counts and per-index timestamps match. Upper >= Middle >= Lower for
successful representable output; zero variance collapses the bands. No percentage
offset, alignment framework, metadata change, sorting, or padding is introduced.
The general MultiSeriesIndicatorResult remains unconstrained for MACD and future
indicators; synchronization is a Bollinger-specific property.

## Validation, temporal gaps, and immutability

Null collection throws ArgumentNullException for candles. Null candle elements,
mixed InstrumentId, mixed CandleInterval, duplicate OpenTime, or backward OpenTime
throw ArgumentException for candles. Validate the full reference snapshot before
warm-up or arithmetic. Coherence uses existing value equality. Do not silently
accept invalid input because the window is not ready, or repair/sort/deduplicate it.

OpenTime is strictly increasing; temporal gaps remain valid. The window consists
of the last N supplied observations rather than elapsed intervals. Do not reset,
infer missing candles, or pad. Output never uses OpenTime, ReceivedTime, ReplayTime,
or wall clock. Closed-candle availability remains the existing caller responsibility.

Inputs, immutable candles, prices, and timestamps remain untouched. Middle uses
the SMA result; Upper and Lower use existing immutable IndicatorPoint/IndicatorSeries.
NamedIndicatorSeries and MultiSeriesIndicatorResult preserve read-only ownership.
Source mutation after calculation cannot change any returned component.

## Deterministic batch, no look-ahead, and complexity

Every Calculate call uses local state, independent of previous batches or failures.
Same ordered input, Period, and K produce exactly equal names, order, values, and
timestamps when successful; numerical failures are deterministic as well.
Each point uses only its current window. Multiple-prefix tests compare Middle,
Upper, and Lower, including a future price shock. Whole-batch validation, overflow,
or non-convergence aborts the call instead of yielding partial output.

Snapshot, validation, and SMA are O(n). Direct variance costs O(n * Period) and
Newton costs at most 128 updates per output, so the accepted bound is O(n * Period)
with fixed iteration limit. Memory is O(n) for snapshots and results; the private
Newton calculation and individual variance accumulation use local scalar state.
No I/O, caches, randomness, clock, retained windows, or statistics framework.

## Consequences

- Adds synchronized multi-series output and population dispersion to the engine.
- Middle exactly reuses the approved SMA; existing indicators remain intact.
- Numeric policy is explicit, decimal-only, private, bounded, and testable through bands.
- Direct deviations trade O(n * Period) work for clarity and reduced cancellation.
- Finite-decimal two-cycles stabilize by minimum Newton error, with smaller-value ties.
- Genuine iteration exhaustion remains an explicit error under the defensive bound.
- Overflow and underflow remain decimal behavior rather than masked financial data.
- Rendering and execution consumers can reuse one implementation and temporal policy.

## Alternatives considered and future consumers

Sample variance is rejected because Vortex chooses population windows. Raw moment
subtraction and fragile rolling variance optimizations are rejected for this task.
Public/shared decimal math, generic statistics, or configurable tolerance are
deferred until another real requirement exists. Floating-point square root,
rounded results, clamping, last-iterate return, and silent fallback are rejected.
The reviewed correction recognizes explicit two-cycles with minimum Newton error
and exact-tie selection. Generic cycle acceptance, globally relaxed tolerance,
and precision-relative convergence remain outside this policy.

Charts, Backtesting, Strategies, and Robots may consume the same three bands.
Default parameters, trading interpretations, indicators beyond Bollinger, shared
math, caching, incremental execution, frontend rendering, API, SignalR, trading,
risk, persistence, Redis, and DI require separate authorization.
