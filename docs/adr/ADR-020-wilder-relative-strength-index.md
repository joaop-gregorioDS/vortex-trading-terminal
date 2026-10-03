# ADR-020: Wilder Relative Strength Index

Status: Accepted

Date: 2026-10-03

## Context

The Application Indicator Engine already provides deterministic batch contracts,
immutable IndicatorPoint and IndicatorSeries results, SMA, and SMA-seeded EMA.
RSI adds Wilder smoothing over price changes. Initialization, smoothing, zero
denominators, and output availability must be explicit so future consumers do
not implement incompatible variants of the same named indicator.

## Decision: Wilder RSI

Add RelativeStrengthIndex as a third IIndicator implementation under
VortexTrading.Application/Indicators, using the existing Domain Candle and
result contracts. Name is exactly `RSI`. Its constructor accepts a read-only
positive integer Period; nonpositive periods throw ArgumentOutOfRangeException
for `period`. Fourteen is a common consumer configuration, not a hard-coded
default or restriction in the calculation.

Only Candle.Close.Value supplies prices. No price selector, other indicator
implementation, generic smoother, shared validator, or consumer adapter is used.

## Changes, gains, losses, and arithmetic seed

For consecutive supplied candles:

```text
Change_t = Close_t - Close_(t-1)
Gain_t = max(Change_t, 0)
Loss_t = max(-Change_t, 0)

InitialAverageGain = sum(first N gains) / N
InitialAverageLoss = sum(first N losses) / N
```

Gains and losses are nonnegative; a change cannot contribute to both. The seed
uses N price changes, which require N+1 candles, unlike SMA/EMA warm-up. Up to N
valid candles returns a non-null empty IndicatorSeries, after full validation.
Exactly N+1 candles produces one point at the (N+1)th Candle.CloseTime. No partial
seed, padding, or Wilder recurrence is applied before that point.

## Wilder smoothing and distinction from EMA

After the seed, advance the local averages once for each subsequent change:

```text
AverageGain_t = (PreviousAverageGain * (N - 1) + CurrentGain) / N
AverageLoss_t = (PreviousAverageLoss * (N - 1) + CurrentLoss) / N
```

Preserve this expression with decimal arithmetic. Wilder smoothing corresponds
to a weight of 1/N for the new observation, distinct from the conventional EMA
alpha 2/(N+1). Do not call ExponentialMovingAverage or SimpleMovingAverage.

For period two and closes 10, 13, 12, 14, 11:

- Seed averages are 1.5 and 0.5, producing RSI 75.
- The next averages are 1.75 and 0.25, producing RSI 87.5.
- The following averages are 0.875 and 1.625. Natural decimal RS division and
  the RSI expression produce `35.000000000000000000000000002`.

## Relative strength, RSI, and zero conventions

In the general case:

```text
RS = AverageGain / AverageLoss
RSI = 100 - (100 / (1 + RS))
```

Handle zero averages before dividing:

- AverageLoss zero and AverageGain positive: RSI is 100, without infinity.
- AverageGain zero and AverageLoss positive: RSI is 0.
- Both averages zero: RSI is 50, the explicit Vortex flat-market convention.

These rules apply at the seed and every recursive point. The formula and rules
produce values between 0 and 100; no clamp masks calculation errors. Period one
is valid, needs two candles initially, and thereafter considers only the current
change: an increase produces 100, a decrease 0, and a flat change 50.

## Decimal precision and overflow

Price changes, gains, losses, sums, averages, RS, RSI, and outputs are decimal.
There is no double conversion, explicit rounding, quantization, clamping,
exception catch, or numeric fallback. Presentation may round values later.

.NET decimal has finite precision and range. Nonterminating divisions and
recursive arithmetic may round naturally, and very small averages can round
to zero. Zero conventions use the actual computed decimal averages. For closes
10, 12, 11, 14 and period three, decimal seed averages are 5/3 and 1/3; the exact
RS is `5.0000000000000000000000000006` and RSI is
`83.33333333333333333333333334`, not an explicitly rounded percentage.

Arithmetic overflow propagates as OverflowException. Valid positive Domain
prices can overflow the seed gain/loss sum through alternating extreme prices.
A large gain divided by a tiny nonzero average loss can also overflow RS even
though a real-number RSI would fit within 0..100. No partial series is returned,
and Domain is not modified to accommodate those scenarios. The existing warm-up
comparison avoids computing Period+1 in integer arithmetic for int.MaxValue.

## Validation and temporal gaps

Each call snapshots the supplied candle references, then validates the complete
snapshot before warm-up or arithmetic. Candles are immutable and not cloned or
modified. Public invariants match SMA and EMA:

- Null collection throws ArgumentNullException for `candles`.
- Null elements, mixed InstrumentId, mixed CandleInterval, duplicate OpenTime,
  and backward OpenTime throw ArgumentException for `candles`.
- Instrument and interval coherence use existing value equality.
- OpenTime must be strictly increasing in the supplied order.
- No sorting, grouping, deduplication, or timestamp normalization is performed.

A small private validator is retained inside RSI without refactoring approved
SMA/EMA. Invalid sequences cannot be masked by insufficient warm-up. Validation
also covers later candles before seed calculation begins.

Gaps are valid. Changes compare consecutive supplied candles across gaps;
elapsed intervals do not add observations, reset averages, or infer market data.
There is no additional contiguity or overlap requirement.

## Output time, deterministic batch, and no look-ahead

Every result uses its corresponding Candle.CloseTime, never OpenTime, replay
time, received time, or a wall clock. Each Calculate call independently seeds
and advances local averages. No previous close, gain, loss, RSI, or cache is
retained between batches. Same input and period produce the same values,
timestamps, and ordering; result snapshots survive source collection changes.

Each point depends only on supplied closes through its corresponding candle.
Appending valid future candles preserves every common-prefix point exactly.
Whole-batch validation still rejects invalid future input, and arithmetic
overflow aborts calculation. These failure semantics do not return partial output.

The caller remains responsible for selecting complete, causally available
candles. RSI has no execution horizon or clock and cannot independently verify
availability. There are no replay, provider, Infrastructure, DI, I/O, randomness,
or external data dependencies.

## Consequences

- One explicit Wilder RSI convention serves all future Indicator Engine consumers.
- N-change warm-up, seed, smoothing, zero rules, and timestamps are tested.
- Snapshot, validation, seed, recurrence, and result construction are O(n).
- Input snapshot and output require O(n) batch memory, not incremental state.
- Existing indicators, contracts, Domain, replay, and Infrastructure remain intact.
- Finite decimal precision and observable overflow are part of this contract.
- Private validation duplication is temporary and requires separate review to extract.

## Alternatives considered

- Conventional EMA alpha 2/(N+1): rejected because this decision selects Wilder RSI.
- Rolling simple-average RSI: rejected; simple averaging is used only for the seed.
- First-change or partial seeds: rejected in favor of N complete price changes.
- Flat-market RSI 0 or 100, null, or exception: rejected in favor of neutral 50.
- Double, explicit rounding, a rearranged ratio, or overflow fallback: rejected
  in favor of the authorized decimal expression and natural exception behavior.
- A generic Wilder smoother or shared SMA/EMA validator: deferred to preserve scope.
- Consumer-specific formulas, incremental state, and caching: not introduced.

## Limitations and future consumers

Batches and results must fit in memory. Concurrent source collection mutation,
incremental calculation, caching, price selectors, missing-bar detection, and
automatic availability filtering are outside this decision. Near decimal limits,
overflow remains possible even when an abstract final RSI would be representable.

Charts, Backtesting, Strategies, and Robots will reuse this same calculation.
None is integrated here. ATR, MACD, ADX, other indicators, a composition engine,
trading, risk, orders, API, frontend indicators, persistence, Redis, and DI remain
outside this task.
