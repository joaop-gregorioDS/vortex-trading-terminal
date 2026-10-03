# ADR-021: Wilder Average True Range

Status: Accepted

Date: 2026-10-03

## Context

The Application Indicator Engine provides immutable batch results and approved
SMA, EMA, and Wilder RSI calculations. ATR adds a volatility measure using candle
ranges and distances from the previous close. The first-candle convention,
warm-up, seed, and smoothing must be explicit to keep all future consumers on
the same mathematical and temporal definition.

## Decision: Wilder ATR and Candle source

Add AverageTrueRange as the fourth IIndicator implementation under
VortexTrading.Application/Indicators, reusing the existing Domain Candle,
IndicatorPoint, and IndicatorSeries. Name is exactly `ATR`. The constructor
accepts a read-only positive integer Period; nonpositive periods throw
ArgumentOutOfRangeException for `period`. Fourteen is a possible consumer
configuration, not a hard-coded default or restriction.

Use Candle.High, Low, and the immediately previous supplied Candle.Close.
Current Close becomes the previous close for the next candle; Open and Volume
do not enter the formula. No alternative OHLCV model or price selector is added.

## First-candle and subsequent True Range

The first candle of each supplied batch has no external previous close:

```text
TR_1 = High_1 - Low_1

TR_t = max(
    High_t - Low_t,
    abs(High_t - Close_(t-1)),
    abs(Low_t - Close_(t-1))
) for t > 1
```

The first candle is included, not discarded. No preceding candle is required
outside the batch. TR is a private calculation detail of ATR; no public TrueRange
indicator, interface, or series is introduced.

## Price gaps and temporal gaps

With previous close 100:

- High 105, Low 95: the intrabar range dominates and TR is 10.
- High 112, Low 108: the upward gap dominates and TR is 12, not 4.
- High 92, Low 88: the downward gap dominates and TR is 12, not 4.

High equal to Low does not imply zero TR when that price differs from the previous
close. Flat candles at an unchanged price have TR zero. True ranges and ATR are
nonnegative because of Candle invariants and the formula, without clamping.

Temporal gaps are different from price gaps. Changes compare immediately
consecutive supplied candles even when their open times are far apart. No
missing candles are inferred, inserted, padded, or counted, and gaps do not reset
the previous close or ATR. Period counts supplied TR observations, not elapsed
time intervals.

## Period semantics, warm-up, and arithmetic seed

Every candle contributes one TR, including the first. ATR(N) therefore requires
N candles, unlike RSI(N), which requires N+1 candles for N price changes.

```text
ATR_seed = (TR_1 + TR_2 + ... + TR_N) / N
```

Empty input or fewer than N valid candles returns a non-null empty IndicatorSeries
after full validation. Exactly N candles produces one point. Its value is the
arithmetic seed, and its time is the Nth Candle.CloseTime. No partial average or
Wilder smoothing is applied before the seed.

## Wilder smoothing and Period one

After the seed, advance once per supplied candle:

```text
ATR_t = (PreviousATR * (N - 1) + TR_t) / N
```

Preserve this decimal expression. It uses Wilder's weight 1/N, not conventional
EMA alpha 2/(N+1). TRs 10, 12, 8 seed ATR(3) at 10. Subsequent TRs 16 and 6
produce 12 and 10. Conventional EMA would instead produce 13 and 9.5 after the
same seed, and is explicitly rejected for ATR.

Period one is valid and needs only one candle. The seed is TR_1, and smoothing
naturally reproduces each subsequent TR. Zero current TR decays a previously
positive ATR through smoothing rather than resetting it.

RSI and ATR share the mathematical idea of Wilder smoothing, but their input,
initialization, and warm-up differ. This task introduces no generic Wilder
smoother, moving-average mode, or shared indicator validator, and does not call
or modify RSI, EMA, or SMA.

## Decimal arithmetic, absolute values, and overflow

TR, seed sum, seed, smoothing, and output use decimal throughout. There is no
double conversion, explicit rounding, quantization, numeric fallback, clamping,
or exception catch. Presentation may round values later.

Use normal decimal subtraction, Math.Abs(decimal), multiplication, addition,
and division. There is no custom absolute-value routine that changes exceptional
behavior. The tests cover extreme positive prices whose decimal difference
rounds to decimal.MinValue; normal decimal absolute value is preserved.

.NET decimal has finite precision and range. Nonterminating division and
recursive arithmetic naturally round within that precision. Fractional TRs
0.25, 0.30, 0.20 seed ATR(3) at 0.25; then TRs 0.30 and 0.20 produce
`0.2666666666666666666666666667` and `0.2444444444444444444444444445`.

Arithmetic OverflowException propagates without partial results. Valid Domain
candles with extreme ranges can overflow the initial TR sum. A valid seed can
also be followed by overflow while adding the next TR to the weighted previous
ATR, even when the abstract result after division would fit. Both scenarios are
tested without modifying Domain. Normal decimal operations retain their own
overflow semantics at every arithmetic step.

## Validation, output time, and immutability

Each call snapshots the input candle references and validates the entire sequence
before warm-up or arithmetic. Candles and their OHLCV and timestamps are not
modified or cloned. Public validation matches the existing indicators:

- Null collection throws ArgumentNullException for `candles`.
- Null candle elements, mixed InstrumentId, mixed CandleInterval, duplicate
  OpenTime, and backward OpenTime throw ArgumentException for `candles`.
- Instrument and interval coherence use their existing value equality.
- OpenTime must be strictly increasing; no sorting, grouping, or deduplication.
- Temporal gaps remain valid without an additional contiguity constraint.

The small validator remains private to ATR. Invalid later input cannot be
masked by warm-up or by starting seed arithmetic before validation finishes.

Every IndicatorPoint uses the corresponding Candle.CloseTime, never OpenTime,
replay time, received time, or a wall clock. IndicatorSeries owns its existing
immutable snapshot; changing the original collection later cannot change results.

## Deterministic batch and no look-ahead

Each batch starts independently with its own first-candle TR and seed. Previous
close, TR, and ATR are local variables, never retained between calls. Same candles
and period produce identical values, times, and ordering.

Each point depends only on supplied candles through its corresponding candle.
Appending valid future candles preserves every common-prefix point exactly.
Whole-batch validation rejects invalid later input, and overflow aborts the call
without returning partial output.

Consumers are responsible for selecting complete candles causally available to
their execution horizon. ATR has no horizon or clock and does not independently
verify candle availability. It has no replay, provider, Infrastructure, I/O, DI,
wall-clock, or randomness dependency.

## Consequences

- One explicit Wilder ATR convention is available to future engine consumers.
- First-candle TR, price gaps, warm-up, seed, smoothing, and CloseTime are tested.
- Snapshot, validation, seed, recurrence, and result construction take O(n) time.
- Input snapshot and output require O(n) batch memory, not incremental state.
- Existing indicators, contracts, Domain, replay, and Infrastructure stay unchanged.
- Finite decimal precision and observable overflow remain part of the contract.
- Private TR and validation avoid prematurely introducing shared abstractions.

## Alternatives considered

- Discarding the first candle or requiring an external previous close: rejected
  in favor of first-candle High-Low and N-candle warm-up.
- High-Low alone for all candles: rejected because it misses price gaps.
- Conventional EMA or rolling SMA after seed: rejected in favor of Wilder smoothing.
- Partial warm-up output: rejected in favor of an arithmetic seed after N TRs.
- Double, explicit rounding, a rearranged smoother, or overflow fallback: rejected
  in favor of the authorized decimal formula and natural exceptions.
- Public TrueRange, generic Wilder smoother, and shared validator: deferred.
- Consumer-specific variants, incremental state, and caches: not introduced.

## Limitations and future consumers

Complete batches and results must fit in memory. Concurrent source mutation,
incremental state, caching, price normalization, missing-bar detection, automatic
availability filtering, and alternative first-TR policies are outside this task.
Arithmetic may overflow before division even when a real-number ATR would fit.

Charts, Backtesting, Strategies, and Robots will reuse this same calculation;
no consumer is integrated here. MACD, ADX, other indicators, composition,
trading, risk, orders, API, frontend indicators, persistence, Redis, and DI remain
outside this decision.
