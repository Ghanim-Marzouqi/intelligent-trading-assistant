import {
  Component,
  Input,
  ElementRef,
  ViewChild,
  AfterViewInit,
  OnDestroy,
  OnChanges,
  SimpleChanges
} from '@angular/core';
import { createChart, CandlestickSeries, CandlestickData, Time, IChartApi, ISeriesApi } from 'lightweight-charts';

export interface CandleData {
  time: number;
  open: number;
  high: number;
  low: number;
  close: number;
  volume: number;
}

export interface TradeLevels {
  entry: number;
  stopLoss: number;
  takeProfit: number;
  direction: string;
}

@Component({
  selector: 'app-trade-chart',
  standalone: true,
  template: `<div #chartContainer class="chart-container"></div>`,
  styles: [`
    .chart-container {
      width: 100%;
      height: 400px;
      border-radius: 8px;
      overflow: hidden;
      border: 1px solid var(--border);
    }

    @media (max-width: 768px) {
      .chart-container {
        height: 300px;
      }
    }

    @media (max-width: 480px) {
      .chart-container {
        height: 250px;
      }
    }
  `]
})
export class TradeChartComponent implements AfterViewInit, OnDestroy, OnChanges {
  @ViewChild('chartContainer') chartContainer!: ElementRef<HTMLDivElement>;
  @Input() candles: CandleData[] = [];
  @Input() tradeLevels: TradeLevels | null = null;
  @Input() livePrice: { bid: number; ask: number; timestamp: Date } | null = null;
  @Input() timeframe: string = 'H1';

  private chart: IChartApi | null = null;
  private candleSeries: ISeriesApi<'Candlestick'> | null = null;
  private resizeObserver: ResizeObserver | null = null;

  private currentCandleTime = 0;
  private currentCandle: CandlestickData<Time> | null = null;

  ngAfterViewInit() {
    this.createChart();
  }

  ngOnChanges(changes: SimpleChanges) {
    if (!this.chart) return;

    if (changes['candles'] && this.candles?.length) {
      this.updateCandles();
    }
    if (changes['tradeLevels']) {
      this.updateTradeLevels();
    }
    if (changes['livePrice'] && this.livePrice && this.candleSeries) {
      this.updateLiveCandle();
    }
  }

  private createChart() {
    const container = this.chartContainer.nativeElement;

    this.chart = createChart(container, {
      width: container.clientWidth,
      height: container.clientHeight,
      layout: {
        background: { color: '#141b2d' },
        textColor: '#8b9dc3',
      },
      grid: {
        vertLines: { color: '#1e293b' },
        horzLines: { color: '#1e293b' },
      },
      crosshair: {
        mode: 0,
      },
      timeScale: {
        borderColor: '#2a3548',
        timeVisible: true,
        secondsVisible: false,
      },
      rightPriceScale: {
        borderColor: '#2a3548',
      },
    });

    this.candleSeries = this.chart.addSeries(CandlestickSeries, {
      upColor: '#22c55e',
      downColor: '#ef4444',
      borderUpColor: '#22c55e',
      borderDownColor: '#ef4444',
      wickUpColor: '#22c55e',
      wickDownColor: '#ef4444',
    });

    if (this.candles?.length) {
      this.updateCandles();
    }
    if (this.tradeLevels) {
      this.updateTradeLevels();
    }

    this.resizeObserver = new ResizeObserver(entries => {
      for (const entry of entries) {
        const { width, height } = entry.contentRect;
        if (this.chart) {
          this.chart.applyOptions({ width, height });
        }
      }
    });
    this.resizeObserver.observe(container);
  }

  private updateCandles() {
    if (!this.candleSeries || !this.candles?.length) return;

    const data: CandlestickData<Time>[] = this.candles.map(c => ({
      time: c.time as Time,
      open: c.open,
      high: c.high,
      low: c.low,
      close: c.close,
    }));

    this.candleSeries.setData(data);
    this.chart?.timeScale().fitContent();

    // Track the last candle for live updates
    if (data.length > 0) {
      const last = data[data.length - 1];
      this.currentCandleTime = last.time as number;
      this.currentCandle = { ...last };
    }
  }

  private updateLiveCandle() {
    if (!this.livePrice || !this.candleSeries) return;

    const mid = (this.livePrice.bid + this.livePrice.ask) / 2;
    const tickTime = Math.floor(new Date(this.livePrice.timestamp).getTime() / 1000);
    const candleTime = this.floorToTimeframe(tickTime);

    if (candleTime === this.currentCandleTime && this.currentCandle) {
      this.currentCandle.close = mid;
      this.currentCandle.high = Math.max(this.currentCandle.high, mid);
      this.currentCandle.low = Math.min(this.currentCandle.low, mid);
      this.candleSeries.update(this.currentCandle);
    } else if (candleTime > this.currentCandleTime) {
      this.currentCandleTime = candleTime;
      this.currentCandle = {
        time: candleTime as Time,
        open: mid,
        high: mid,
        low: mid,
        close: mid,
      };
      this.candleSeries.update(this.currentCandle);
    }
  }

  private floorToTimeframe(timestamp: number): number {
    let duration: number;
    switch (this.timeframe) {
      case 'M15': duration = 15 * 60; break;
      case 'H1': duration = 60 * 60; break;
      case 'H4': duration = 4 * 60 * 60; break;
      case 'D1': duration = 24 * 60 * 60; break;
      default: duration = 60 * 60;
    }
    return Math.floor(timestamp / duration) * duration;
  }

  private updateTradeLevels() {
    if (!this.candleSeries) return;

    if (this.tradeLevels) {
      const tl = this.tradeLevels;

      this.candleSeries.createPriceLine({
        price: tl.entry,
        color: '#3b82f6',
        lineWidth: 2,
        lineStyle: 0, // Solid
        axisLabelVisible: true,
        title: `Entry ${tl.entry}`,
      });

      this.candleSeries.createPriceLine({
        price: tl.stopLoss,
        color: '#ef4444',
        lineWidth: 1,
        lineStyle: 2, // Dashed
        axisLabelVisible: true,
        title: `SL ${tl.stopLoss}`,
      });

      this.candleSeries.createPriceLine({
        price: tl.takeProfit,
        color: '#22c55e',
        lineWidth: 1,
        lineStyle: 2, // Dashed
        axisLabelVisible: true,
        title: `TP ${tl.takeProfit}`,
      });
    }
  }

  ngOnDestroy() {
    this.resizeObserver?.disconnect();
    this.chart?.remove();
    this.chart = null;
    this.candleSeries = null;
  }
}
