import { Time } from 'lightweight-charts';

export interface IndicatorPoint {
  time: Time;
  value: number;
}

export interface VolumePoint {
  time: Time;
  value: number;
  color: string;
}

export interface CandleInput {
  time: number;
  open: number;
  high: number;
  low: number;
  close: number;
  volume: number;
}

export function computeSMA(candles: CandleInput[], period: number): IndicatorPoint[] {
  if (candles.length < period) return [];
  const result: IndicatorPoint[] = [];
  let sum = 0;
  for (let i = 0; i < candles.length; i++) {
    sum += candles[i].close;
    if (i >= period) {
      sum -= candles[i - period].close;
    }
    if (i >= period - 1) {
      result.push({ time: candles[i].time as Time, value: sum / period });
    }
  }
  return result;
}

export function computeEMA(candles: CandleInput[], period: number): IndicatorPoint[] {
  if (candles.length < period) return [];
  const k = 2 / (period + 1);
  const result: IndicatorPoint[] = [];

  // Initial SMA for first EMA value
  let sum = 0;
  for (let i = 0; i < period; i++) {
    sum += candles[i].close;
  }
  let ema = sum / period;
  result.push({ time: candles[period - 1].time as Time, value: ema });

  for (let i = period; i < candles.length; i++) {
    ema = candles[i].close * k + ema * (1 - k);
    result.push({ time: candles[i].time as Time, value: ema });
  }
  return result;
}

export function computeRSI(candles: CandleInput[], period: number): IndicatorPoint[] {
  if (candles.length < period + 1) return [];
  const result: IndicatorPoint[] = [];

  let avgGain = 0;
  let avgLoss = 0;

  // Initial average gain/loss
  for (let i = 1; i <= period; i++) {
    const change = candles[i].close - candles[i - 1].close;
    if (change > 0) avgGain += change;
    else avgLoss += Math.abs(change);
  }
  avgGain /= period;
  avgLoss /= period;

  const rs = avgLoss === 0 ? 100 : avgGain / avgLoss;
  result.push({
    time: candles[period].time as Time,
    value: avgLoss === 0 ? 100 : 100 - 100 / (1 + rs),
  });

  // Wilder's smoothing
  for (let i = period + 1; i < candles.length; i++) {
    const change = candles[i].close - candles[i - 1].close;
    const gain = change > 0 ? change : 0;
    const loss = change < 0 ? Math.abs(change) : 0;

    avgGain = (avgGain * (period - 1) + gain) / period;
    avgLoss = (avgLoss * (period - 1) + loss) / period;

    const rsi = avgLoss === 0 ? 100 : 100 - 100 / (1 + avgGain / avgLoss);
    result.push({ time: candles[i].time as Time, value: rsi });
  }
  return result;
}

export function computeVolume(candles: CandleInput[]): VolumePoint[] {
  return candles.map(c => ({
    time: c.time as Time,
    value: c.volume,
    color: c.close >= c.open ? 'rgba(34,197,94,0.5)' : 'rgba(239,68,68,0.5)',
  }));
}
