# ADR-018: Indicator Engine Foundation

Status: Accepted

Date: 2026-10-03

## Context

The approved Market Replay Foundation separates historical occurrence from
complete-payload availability. Future consumers need one mathematical indicator
implementation so chart, backtest, strategy, and robot values do not diverge.
The first step must establish inputs, timestamps, warm-up, precision, validation,
immutability, and determinism before adding a catalog of indicators.

Domain already provides immutable candles with decimal OHLCV values and
representable close times. Indicator calculation can use these types without
changing Domain or coupling mathematical computation to historical replay.

## Decision

```text
The Vortex Trading Terminal uses one deterministic
Application-layer Indicator Engine shared by Charts,
Backtesting, Strategies and Robots.

Indicators consume explicitly supplied Candle sequences
and do not access replay sessions, providers, persistence,
wall clocks or future market data.

IndicatorPoint timestamps represent the close time of the
Candle whose completed information produced the value.

The initial execution model is deterministic batch
calculation.

SimpleMovingAverage is the first reference implementation.
```

## Placement and consumer model

Add `IIndicator`, `IndicatorPoint`, `IndicatorSeries`, and `SimpleMovingAverage`
under `VortexTrading.Application/Indicators`. Domain, Infrastructure, frontend,
providers, API, and dependency injection remain unchanged. No packages are added.

`IIndicator` exposes a stable name and synchronous batch calculation from
`IReadOnlyList<Candle>` to `IndicatorSeries`. SMA's name is exactly `SMA`, and
its positive constructor period is a separate read-only property.

Consumers determine which closed candles are already available and explicitly
supply them. The engine has no availability-policy dependency or execution-time
argument and cannot independently certify whether a supplied candle is closed
relative to a caller's causal horizon. Future consumers must honor this boundary.

## Temporal semantics and warm-up

A result point uses the final candle's CloseTime for its corresponding window.
SMA uses only that candle and its preceding N-1 close prices. Appending valid
later candles does not change previously emitted prefix values.

Before N supplied candles, return an empty valid series. Exactly N candles emits
one point. No partial averages, null result, padded values, or insufficient-data
exception are produced. Gaps do not add candles or reset the count-based window.

## Value types, snapshots, and decimal decision

`IndicatorPoint` is an immutable value record of non-null `UtcInstant` and decimal.
Negative, zero, and positive indicator values are allowed. `IndicatorSeries`
owns a read-only snapshot, preserves point order and duplicates, and permits
empty collections. Null source collections and null points are rejected.

SMA uses `Candle.Close.Value` and decimal arithmetic only. No source-price selector,
double fallback, explicit rounding, or quantization is introduced. Decimal range
and finite precision apply to sums and division, including nonterminating means.
Overflow in an actual window propagates instead of producing a fallback value.
This choice does not require future statistical indicators to use decimal
internally; double-based computation may be justified in a later decision.

## Validation and current-window algorithm

Snapshot the input candle references locally without cloning or modifying them.
Validate the whole batch before warm-up handling and calculation:

- Null input throws `ArgumentNullException` for `candles`.
- Null entries, mixed instruments, or mixed intervals throw `ArgumentException`
  for `candles`.
- OpenTime must be strictly increasing; duplicates and backward time throw
  `ArgumentException` for `candles` rather than being sorted or deduplicated.
- Coherence uses existing instrument and interval value equality.
- Gaps are accepted; missing-bar detection and additional overlap rules are absent.

Recompute a local decimal sum from zero for each complete current window, adding
its closes in supplied order from oldest to newest. No sum is carried between
windows: precision lost while a larger value was present cannot contaminate
later windows after that value leaves. Emit sum/N at the window's final CloseTime.
Finite decimal rounding within the current window remains possible, and actual
sum overflow is not caught. Calculation is O(C * Period), where C is the supplied
candle count, explicitly accepted for numerical window isolation, with O(C)
input snapshot and result storage.
Snapshot and validation remain linear. No global cache or persistent indicator
state is introduced.

## Determinism

Repeated calls with the same input and period produce the same point count,
values, timestamps, and order. Calculation has no wall clock, randomness, replay,
provider, persistence, I/O, or background dependency. A new result snapshot is
returned each time, and reuse across different batches retains no window state.

## Alternatives considered

- Frontend/backtest/strategy-specific formulas: rejected because they risk
  diverging values and warm-up semantics.
- A second OHLCV model: rejected because the existing Domain candle is sufficient.
- Direct replay availability checks inside indicators: rejected because consumers
  own causal selection while indicators own mathematics.
- Silent sorting, grouping, or deduplication: rejected because they conceal
  incoherent input and alter the supplied series.
- Linear rolling sums: rejected because previously absorbed small contributions
  cannot be reconstructed by subtracting an outgoing larger value. Direct current-
  window recomputation accepts O(C * Period) to avoid that historical contamination.
- Double SMA or overflow fallback: rejected because decimal input requires no
  conversion and overflow must remain observable.
- Incremental mutable state and caching: deferred until the batch contract is
  reviewed and a separate incremental API is justified.

## Consequences and limitations

One implementation supplies consistent mathematical and temporal semantics for
future consumers, with typed immutable results and explicit warm-up behavior.
The contract rejects incoherent series instead of repairing them silently.

Complete input and output must fit in memory. Decimal operations may round within
their finite precision or overflow even when an abstract mean is representable.
Whole-batch validation or a later overflowing window aborts the calculation;
there is no partial-result API. A caller can still violate causal input selection,
so the boundary must be honored in future integrations. Concurrent source
mutation is outside the batch contract.

## Deferred decisions

Future incremental APIs, warm-up state transfer, caching, selectors, statistical
double-based indicators, and gap policies require separate review. EMA, RSI, ATR,
MACD, Stochastic, Bollinger Bands, VWAP, ADX, and OBV are not implemented here.
Chart rendering, consumer integration, Backtesting, Strategies, Robots, signals,
trading, orders, risk, portfolio, P&L, API, DI, persistence, and Redis are deferred.
