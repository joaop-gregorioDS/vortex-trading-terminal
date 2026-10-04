# ADR-027: Wilder Average Directional Index

Status: Accepted
Date: 2026-10-03

## Context

The deterministic batch Indicator Engine already includes Wilder RSI and ATR.
ADX adds directional movement and strength using High, Low, and the preceding
Close. Directional indices and ADX have different warm-ups, fitting the existing
multi-series result without changing public contracts or Domain models.

## Decision

Place sealed `AverageDirectionalIndex(int period)` in Application/Indicators,
implementing `IMultiSeriesIndicator`, with `Name = "ADX"` and read-only Period.
Require period > 0; otherwise throw ArgumentOutOfRangeException for `period`.
There is no default period. Use the existing Candle; neither Open nor Volume
participates. Callers remain responsible for supplying closed candles.

Snapshot references and validate the whole input before arithmetic or warm-up
returns. Null collection throws ArgumentNullException for `candles`. Null candle,
mixed InstrumentId, mixed CandleInterval, duplicate or backward OpenTime throw
ArgumentException for `candles`. Use existing value equality; require strictly
increasing OpenTime without sorting, deduplicating, repair, or extra Domain rules.

### Transition mathematics

For current candle i > 0 and the immediately preceding supplied candle:

```text
TR = max(High - Low, abs(High - PreviousClose), abs(Low - PreviousClose))
UpMove = High - PreviousHigh
DownMove = PreviousLow - Low
+DM = UpMove if UpMove > DownMove and UpMove > 0m; otherwise 0m
-DM = DownMove if DownMove > UpMove and DownMove > 0m; otherwise 0m
```

Positive ties discard both directions; two nonpositive moves also produce zeros.
There is no TR contribution from the first supplied candle. Do not reuse ATR's
special first-candle policy, fabricate candles, or call another indicator.

### Wilder directional smoothing

For N = Period, seed each smoothed TR/+DM/-DM with the sum of its first N
transitions, requiring N+1 candles. These are smoothed sums, not means:

```text
SmoothedTR = sum(first N transition TR)
SmoothedPlusDM = sum(first N transition +DM)
SmoothedMinusDM = sum(first N transition -DM)
after the seed: SmoothedNew = (SmoothedPrevious - SmoothedPrevious / N) + Current
+DI = (SmoothedPlusDM / SmoothedTR) * 100m
-DI = (SmoothedMinusDM / SmoothedTR) * 100m
```

If SmoothedTR is exactly zero, both DI are exactly zero. Division precedes
multiplication by 100, avoiding an unnecessary large numerator overflow.
The first DI belong to candle N+1 at that candle's CloseTime.
Sum seeds matter for finite precision and overflow: for N=2, a sum of 1e-28
is preserved, whereas prematurely averaging it rounds to zero. Tests freeze this
distinction through the public outputs.

### DX and ADX

```text
DX = (abs(+DI - -DI) / (+DI + -DI)) * 100m
if +DI + -DI == 0m: DX = 0m
FirstADX = sum(first N DX values) / N
LaterADX = (PreviousADX * (N - 1m) + CurrentDX) / N
```

DX is private, including its initial sum/count. No prior DX is carried across
a zero denominator. Zero policies concern zero denominators, never overflow.
Wilder's ADX recurrence is used exactly; there is no EMA alpha or alternate seed.
The manual N=2 fixture has seed (TR,+DM,-DM)=(10,6,2), DI=(60,20), DX=50.
The next smoothed tuple is (9,3,3), DX=0, so first ADX is 25. Another zero DX
updates ADX to 12.5. Fractional scaling and reflected prices verify the same rules.

### Outputs, warm-up, and alignment

Always return exactly three immutable named series, ordered `+DI`, `-DI`, `ADX`,
including three empty series for empty input or insufficient directional seed.
TR, raw/smoothed DM, smoothed TR, and DX are never public series or APIs.
For C supplied candles:

```text
+DI count = -DI count = max(0, C - N)
ADX count = max(0, C - 2N + 1)
first DI/DX: candle N+1
first ADX: candle 2N
```

Implementation derives the ADX capacity from DI count and counts received DX,
without evaluating 2*N or N+1 in bounded integer arithmetic. Very large positive
periods therefore remain valid and return empty series for small valid batches.
For N=14, DI start at candle 15 and ADX at candle 28. For N=1, both start at
candle 2 and ADX equals the current DX. Do not pad or delay DI to align lengths.
Once ADX starts, its index j aligns with DI index j+N-1 and the same candle's
CloseTime. Never substitute OpenTime, wall clock, received time, or replay time.

### Decimal, overflow, and complexity

All math stays decimal, with natural finite precision and range. There is no
floating-point conversion, explicit rounding, quantization, scaling, clamp,
catch, or overflow fallback. Expected representable DI/DX/ADX values are 0..100;
tests exercise mixed, expanding, inside, flat, oscillating, and perfect-trend data.
Perfect upward/downward fixtures yield one active DI and ADX=100; flat data yield
all zeros. Range is not enforced by repairing results.

Arithmetic overflow propagates OverflowException and aborts the whole call.
Valid Domain fixtures cover a true-range seed sum overflow and a later smoothed
TR recurrence overflow after a successful prefix. Positive Domain prices keep
raw differences and TR components within decimal; a subtraction/absolute-value
overflow fixture cannot be constructed without violating Domain. DM <= TR per
transition, so overflowing a DM seed cannot be isolated from the TR seed processed
first. Bounded DX seeds and ADX recurrence with an int period do not need an
artificial overflow fixture. No Domain change or private test hook is introduced.

Input snapshot, full validation, one forward transition/smoothing pass, and
immutable result construction are O(n) time and O(n) memory. No historical window
is recomputed and no intermediate sequence is retained unnecessarily. Gaps are
valid, including cross-day/large gaps, without reset, padding, interpolation, or
missing-candle inference. Local state starts fresh on every call, even after
validation or overflow failure. Values, timestamps, and order are deterministic.
Each output uses only its current prefix; all three series are prefix invariant
for valid extensions whose arithmetic succeeds. Full-batch validation still
rejects an invalid future candle before any arithmetic.

## Alternatives and limitations

EMA smoothing, directional means, ATR first-TR semantics, tie bias, carried DX,
public intermediate series, and artificial alignment violate the chosen policy.
Shared Wilder/TR/DM/validation utilities and generic frameworks are premature;
small explicit private mathematics preserve the existing indicators unchanged.
Decimal is not arbitrary precision. Sum overflow can abort an otherwise valid
batch even if a mathematical average or ratio would fit. This batch API does not
detect incomplete candles or support incremental state; callers must not mutate
source collections concurrently while snapshotting.

## Consequences and future consumers

No packages, existing indicators/contracts, Domain, Infrastructure, persistence,
Redis, replay, API, DI, or frontend changes are required. Future charts,
backtesting, strategies, robots, and other consumers may use the immutable result
through separately approved integrations. OBV and incremental execution remain
deferred.
