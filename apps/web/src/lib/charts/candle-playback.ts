/** One candle the chart already knows. Prices are drawing numbers, not order prices. */
export type PlayBar = {
  time: number;
  open: number;
  high: number;
  low: number;
  close: number;
};

/**
 * Display state of the forming candle. The path is walked while the next Yahoo read is still
 * in the delay, so the wick grows instead of appearing complete. Orders never read this state.
 */
export type Playback = {
  bucket: number;
  open: number;
  high: number;
  low: number;
  close: number;
  targetHigh: number;
  targetLow: number;
  targetClose: number;
  path: number[];
  /** 0 at the start of the path, 1 when the cached bar is fully on screen. */
  cursor: number;
  seen: number[];
};

const EPS = 1e-6;

function same(left: number, right: number): boolean {
  return Math.abs(left - right) <= EPS;
}

function dedupe(points: number[]): number[] {
  const kept: number[] = [];
  for (const point of points) {
    if (kept.length === 0 || !same(kept[kept.length - 1], point)) kept.push(point);
  }
  return kept;
}

/**
 * Route from the price on screen through the extremes the cache has not shown yet.
 * A rising close tags the low first. A falling close tags the high first.
 */
export function revealPath(from: number, drawnHigh: number, drawnLow: number, next: PlayBar): number[] {
  const rising = next.close + EPS >= from;
  const points = [from];
  const needHigh = next.high > drawnHigh + EPS;
  const needLow = next.low < drawnLow - EPS;
  if (rising) {
    if (needLow) points.push(next.low);
    if (needHigh) points.push(next.high);
  } else {
    if (needHigh) points.push(next.high);
    if (needLow) points.push(next.low);
  }
  points.push(next.close);
  return dedupe(points);
}

/** The history already on screen. Later reads only animate what this cache did not contain. */
export function seedPlayback(candle: PlayBar, minutes: PlayBar[]): Playback {
  return {
    bucket: candle.time,
    open: candle.open,
    high: candle.high,
    low: candle.low,
    close: candle.close,
    targetHigh: candle.high,
    targetLow: candle.low,
    targetClose: candle.close,
    path: [candle.close],
    cursor: 1,
    seen: minutes.map((minute) => minute.time),
  };
}

/** Queue the cached minutes that arrived during the delay. The drawn wick stays where it is. */
export function absorb(state: Playback, candle: PlayBar, minutes: PlayBar[]): Playback {
  if (!Number.isFinite(candle.time) || candle.time < state.bucket) return state;

  const base: Playback =
    candle.time === state.bucket
      ? state
      : {
          bucket: candle.time,
          open: candle.open,
          high: candle.open,
          low: candle.open,
          close: candle.open,
          targetHigh: candle.open,
          targetLow: candle.open,
          targetClose: candle.open,
          path: [candle.open],
          cursor: 1,
          seen: [],
        };

  const fresh = minutes
    .filter((minute) => minute.time >= base.bucket && !base.seen.includes(minute.time))
    .sort((left, right) => left.time - right.time);

  if (fresh.length === 0) {
    if (
      candle.high <= base.targetHigh + EPS &&
      candle.low >= base.targetLow - EPS &&
      same(candle.close, base.targetClose)
    ) {
      return base;
    }

    return {
      ...base,
      targetHigh: Math.max(base.targetHigh, candle.high),
      targetLow: Math.min(base.targetLow, candle.low),
      targetClose: candle.close,
      path: revealPath(base.close, base.high, base.low, candle),
      cursor: 0,
    };
  }

  const points: number[] = [];
  let from = base.close;
  let revealedHigh = base.high;
  let revealedLow = base.low;
  for (const minute of fresh) {
    for (const price of revealPath(from, revealedHigh, revealedLow, minute)) {
      if (points.length === 0 || !same(points[points.length - 1], price)) points.push(price);
    }
    revealedHigh = Math.max(revealedHigh, minute.high);
    revealedLow = Math.min(revealedLow, minute.low);
    from = minute.close;
  }

  for (const price of revealPath(from, revealedHigh, revealedLow, candle)) {
    if (points.length === 0 || !same(points[points.length - 1], price)) points.push(price);
  }

  return {
    ...base,
    targetHigh: candle.high,
    targetLow: candle.low,
    targetClose: candle.close,
    path: points.length > 0 ? points : [base.close],
    cursor: points.length > 1 ? 0 : 1,
    seen: [...base.seen, ...fresh.map((minute) => minute.time)],
  };
}

/** Position along the cached path. Distance, not the number of turns, sets the speed. */
export function priceAt(path: number[], progress: number): number {
  if (path.length === 0) return 0;
  if (path.length === 1 || progress <= 0) return path[0];
  if (progress >= 1) return path[path.length - 1];

  let total = 0;
  const spans: number[] = [];
  for (let index = 1; index < path.length; index += 1) {
    const span = Math.abs(path[index] - path[index - 1]);
    spans.push(span);
    total += span;
  }
  if (total <= EPS) return path[path.length - 1];

  let remain = progress * total;
  for (let index = 1; index < path.length; index += 1) {
    const span = spans[index - 1];
    if (remain <= span || index === path.length - 1) {
      const fraction = span <= EPS ? 1 : Math.min(1, remain / span);
      return path[index - 1] + (path[index] - path[index - 1]) * fraction;
    }
    remain -= span;
  }
  return path[path.length - 1];
}

/** Move the forming candle forward. High and low grow only when the played price reaches them. */
export function stepPlayback(state: Playback, elapsedMs: number, durationMs = 9_000): Playback {
  if (state.cursor >= 1 || state.path.length <= 1 || durationMs <= 0) {
    return state.cursor >= 1 ? state : { ...state, cursor: 1, close: state.path[state.path.length - 1] ?? state.close };
  }

  const cursor = Math.min(1, state.cursor + elapsedMs / durationMs);
  const price = priceAt(state.path, cursor);
  return {
    ...state,
    cursor,
    close: price,
    high: Math.max(state.high, price),
    low: Math.min(state.low, price),
  };
}

/**
 * Keeps the forming candle pulsing realistically like Profit Pro, even between Yahoo reads.
 * When the cached path is done (cursor >= 1), it generates gentle micro-ticks around the latest reference price
 * within the asset's minMove and spread bounds, expanding high/low when breached.
 */
export function pulsePlayback(
  state: Playback,
  referencePrice: number | null | undefined,
  minMove: number,
  precision: number,
  elapsedMs: number,
  durationMs = 9_000,
): Playback {
  if (state.cursor < 1 && state.path.length > 1 && durationMs > 0) {
    const nextState = stepPlayback(state, elapsedMs, durationMs);
    if (referencePrice !== null && referencePrice !== undefined && Number.isFinite(referencePrice) && referencePrice > 0) {
      return { ...nextState, targetClose: referencePrice };
    }
    return nextState;
  }

  const validTarget =
    referencePrice !== null && referencePrice !== undefined && Number.isFinite(referencePrice) && referencePrice > 0
      ? referencePrice
      : state.targetClose > 0
      ? state.targetClose
      : state.close;

  const stepSize = minMove > 0 ? minMove : 0.01;
  const diff = validTarget - state.close;

  let nextClose = state.close;

  if (Math.abs(diff) > stepSize * 0.75) {
    // The market reference price has moved or is ahead; smoothly advance toward it
    const direction = diff > 0 ? 1 : -1;
    const ticksAway = Math.abs(diff) / stepSize;
    const stepCount = ticksAway > 8 ? 2 : 1;
    const increment = direction * stepCount * stepSize;
    if (Math.abs(increment) >= Math.abs(diff)) {
      nextClose = validTarget;
    } else {
      nextClose = state.close + increment;
    }
  } else {
    // Micro-tick oscillation around reference price (Profit Pro book micro-ticks)
    const roll = Math.random();
    if (roll < 0.35) {
      nextClose = state.close + stepSize;
    } else if (roll < 0.70) {
      nextClose = state.close - stepSize;
    } else {
      nextClose = state.close;
    }

    // Mean-reversion boundary: don't drift further than 2 ticks away from validTarget
    if (nextClose > validTarget + 2 * stepSize + EPS) {
      nextClose = validTarget + stepSize;
    } else if (nextClose < validTarget - 2 * stepSize - EPS) {
      nextClose = validTarget - stepSize;
    }
  }

  // Ensure exact decimal precision
  const factor = 10 ** precision;
  nextClose = Math.round(nextClose * factor) / factor;

  const nextHigh = Math.max(state.high, nextClose, state.targetHigh);
  const nextLow = Math.min(state.low, nextClose, state.targetLow);

  return {
    ...state,
    cursor: 1,
    close: nextClose,
    high: Math.round(nextHigh * factor) / factor,
    low: Math.round(nextLow * factor) / factor,
    targetClose: validTarget,
  };
}

