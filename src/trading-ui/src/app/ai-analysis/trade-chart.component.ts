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
  `]
})
export class TradeChartComponent implements AfterViewInit, OnDestroy, OnChanges {
  @ViewChild('chartContainer') chartContainer!: ElementRef<HTMLDivElement>;
  @Input() candles: CandleData[] = [];
  @Input() tradeLevels: TradeLevels | null = null;

  private chart: IChartApi | null = null;
  private candleSeries: ISeriesApi<'Candlestick'> | null = null;
  private resizeObserver: ResizeObserver | null = null;

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
  }

  private createChart() {
    const container = this.chartContainer.nativeElement;

    this.chart = createChart(container, {
      width: container.clientWidth,
      height: 400,
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
        const { width } = entry.contentRect;
        if (this.chart) {
          this.chart.applyOptions({ width });
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
