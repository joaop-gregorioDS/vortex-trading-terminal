# Minimal Indicator Consumption Boundary

## Purpose and placement

TASK 0.32 adds a small batch consumption boundary in
`VortexTrading.Application/IndicatorConsumption`, above the Indicator Engine
frozen at `eacc79e2942780b90a9c9f26f94f0602eec69dc1`.
It introduces typed configurations and explicit construction mapping so future
consumers do not select indicators by their human-readable Name or duplicate
single/multi-series conventions. No consumer is integrated in this task.

The existing Application project and dependencies are sufficient. Domain,
Infrastructure, API, persistence, frontend, replay, all ten frozen indicators,
their contracts, tests, and ADRs remain unchanged. The new decision is
[ADR-029](../adr/ADR-029-minimal-indicator-consumption-boundary.md), status
`Proposed` pending architectural review.

## Specifications and configuration identity

`IndicatorSpecification` is an abstract record with a private-protected normal
constructor and a computed MachineId. Ten sealed records form the explicitly
supported family. Their typed mathematical parameters are get-only properties;
constructors require explicit arguments and simply preserve them.

| Specification | MachineId | Required parameters |
| --- | --- | --- |
| SmaSpecification | sma | Period: int |
| EmaSpecification | ema | Period: int |
| MacdSpecification | macd | FastPeriod, SlowPeriod, SignalPeriod: int |
| RsiSpecification | rsi | Period: int |
| StochasticSpecification | slow-stochastic | KPeriod, KSmoothingPeriod, DPeriod: int |
| BollingerBandsSpecification | bollinger-bands | Period: int; StandardDeviationMultiplier: decimal |
| AtrSpecification | atr | Period: int |
| VwapSpecification | vwap | None |
| AdxSpecification | adx | Period: int |
| ObvSpecification | obv | None |

Record equality includes the concrete type and stored parameters. Consequently,
SMA(20) equals another SMA(20), differs from SMA(50), and differs from EMA(20).
Parameterless configurations compare equal within their own type. Bollinger
compares decimal numerically: 2m and 2.0000m are equal, while a representable
numeric difference remains distinct. There is no string or floating-point
conversion in configuration equality. Hash codes serve in-process collections,
not persisted or cross-process identity.

MachineId is an exact canonical token with ordinal semantics. It has no setter,
alias, parser, external lookup, or derivation from an indicator Name or class
name. It identifies the indicator type, not a full parameterized configuration.
No display metadata or default periods are introduced.

The family is closed for supported calculation: only the ten sealed types are
mapped. The base normal constructor restricts ordinary external derivation, but
C# records also expose a protected copy constructor. A foreign subtype can use
that path; the calculator explicitly rejects it with NotSupportedException,
even when its MachineId spoofs a supported token. This is not an extension or
registration mechanism.

Configurations contain no instrument, interval, session, time, batch, provider,
cache key, workspace identity, or visual metadata. A caller may associate one
configuration with multiple calculation contexts or visual items externally.

## Validation authority and ordering

Specifications describe parameters, including invalid mathematical values.
Creating SmaSpecification(0) succeeds. Attempting its calculation constructs
SimpleMovingAverage(0), whose existing constructor throws. Positive-period,
MACD ordering, and Bollinger multiplier rules are never duplicated here.

Calculate first rejects a null specification with ArgumentNullException for
`specification`, then dispatches by concrete type. For a supported type, the
frozen constructor runs before Calculate receives the batch. Thus invalid
configuration fails before batch access, including when candles is null.
With valid configuration, null candles retains the engine's
ArgumentNullException for `candles`.

## Batch calculator and explicit mapping

`IndicatorBatchCalculator` is sealed and has one public operation:

```text
Calculate(IndicatorSpecification, IReadOnlyList<Candle>)
    -> MultiSeriesIndicatorResult
```

An explicit switch maps each supported specification to its frozen constructor:

| Specification | Frozen implementation |
| --- | --- |
| SmaSpecification | SimpleMovingAverage |
| EmaSpecification | ExponentialMovingAverage |
| MacdSpecification | MovingAverageConvergenceDivergence |
| RsiSpecification | RelativeStrengthIndex |
| StochasticSpecification | StochasticOscillator |
| BollingerBandsSpecification | BollingerBands |
| AtrSpecification | AverageTrueRange |
| VwapSpecification | VolumeWeightedAveragePrice |
| AdxSpecification | AverageDirectionalIndex |
| ObvSpecification | OnBalanceVolume |

Every call creates its configured indicator and delegates the original candle
collection directly. The calculator has no fields, instance cache, pool, I/O,
clock, service provider, or retained calculation state. MachineId is not used
for dispatch. No public factory or extra calculator interface is needed.

## Single/multi-series adaptation

Single-series indicators SMA, EMA, RSI, ATR, VWAP, and OBV are wrapped as exactly
one NamedIndicatorSeries named `Value`, with that exact case. The private adapter
passes the original calculated IndicatorSeries to the named entry. It does not
copy points or construct another IndicatorSeries. Empty output remains a
non-null result containing one empty Value series.

Multi-series indicators return their original MultiSeriesIndicatorResult
directly. The calculator does not wrap, copy, rename, reorder, pad, or align it:

| Indicator | Preserved output order |
| --- | --- |
| MACD | MACD, Signal, Histogram |
| Bollinger Bands | Middle, Upper, Lower |
| Slow Stochastic | %K, %D |
| ADX | +DI, -DI, ADX |

Count on the common result counts component series, not points. Each component
retains its own count and timestamps. The caller retains its specification and
context; no new result envelope attaches them.

## Responsibility boundaries

The caller acquires and assembles a stable batch for one instrument and interval.
The calculator neither reads providers nor consumes streams, queries storage,
groups candles, sorts, filters, repairs, or deduplicates. The engine remains the
authority for full sequence validation, mathematical calculation, warm-up,
decimal arithmetic, and Candle.CloseTime output.

The runtime owning data availability must pass only completed candles already
permitted at its execution position. The calculator has no execution horizon
and cannot certify causality. Future integration with CausalReplaySession must
use actually delivered occurrences: CurrentTime alone does not prove delivery,
especially at initial position zero and equal-availability ties.

D-03 from TASK 0.31 remains a future integration decision. Replay preserves
duplicate occurrences, while indicators reject duplicate OpenTime in a batch.
This boundary propagates that rejection and never silently takes first/latest,
merges, or deduplicates candles.

The caller also owns session and batch anchors. VWAP and OBV accumulate over the
entire supplied batch; gaps and UTC midnight do not trigger a reset. A shorter
batch changes that anchor. No session or calendar model is introduced.

Warm-up is preserved through actual outputs, including independent component
lengths. No null/zero/not-ready points or synthetic timestamps are introduced.
IndicatorPoint.Time remains unchanged, including tick precision. Alignment and
missing-value presentation belong to future consumers, not this calculator.

## Failure and concurrency semantics

No engine exception is caught or translated. Invalid parameters or sequences,
duplicates, mixed instruments/intervals, OverflowException, ArithmeticException,
and other legitimate failures propagate. No partial success, empty fallback,
last-known value, retry, or error-code/result framework is added.

Calls on one calculator are independent, including after a previous failure.
The frozen implementations have immutable configuration and local accumulators.
Concurrent calls are supported with stable input collections; IReadOnlyList
alone is not an immutability guarantee. Source collections must not be mutated
concurrently during the engine's snapshot. Returned results remain immutable.

## Verification

Boundary tests compare every mapping against direct frozen indicator calls with
nonlinear OHLCV inputs. They cover configuration equality, all parameter
positions, high-precision decimal Bollinger multipliers, output naming/order,
counts/timestamps, empty and partial warm-ups, construction-before-batch
validation, unsupported subtypes, batch rejection, true decimal overflow,
stateless reuse, concurrency, input/result immutability, and batch anchors.

Direct oracle calls create independent result objects, so tests compare their
observable points rather than claiming reference equality between separate
calculations. Reference pass-through is explicit in production: the single
adapter stores the calculated series unchanged; multi branches return the
engine result directly. No test hook, injection seam, or reflection is added
to access an otherwise local engine result.

## Deferred concerns

Registry, catalog, descriptors, generic parameter metadata, warm-up metadata,
serialization/JSON converters, DTOs, persistence, external ID parsing, API,
frontend types, display metadata, DI registration, cache, incremental execution,
Replay integration, Charts, Backtesting, Strategies, and Robots remain deferred.
No generic indicator framework or shared mathematical helper is introduced.
The frozen stateless batch engine remains the correctness oracle for a future
separately approved incremental layer.
