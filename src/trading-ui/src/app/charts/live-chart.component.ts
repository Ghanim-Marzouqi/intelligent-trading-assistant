import {
  Component,
  Input,
  ElementRef,
  ViewChild,
  AfterViewInit,
  OnDestroy,
  OnChanges,
  SimpleChanges,
  inject
} from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Subject, Subscription } from 'rxjs';
import { takeUntil } from 'rxjs/operators';
import {
  createChart,
  CandlestickSeries,
  LineSeries,
  HistogramSeries,
  CandlestickData,
  Time,
  IChartApi,
  ISeriesApi,
  IPriceLine,
} from 'lightweight-charts';
import { environment } from '../../environments/environment';
import { SignalRService, PriceUpdate } from '../shared/services/signalr.service';
import { CandleData } from '../ai-analysis/trade-chart.component';
import { IndicatorConfig } from './indicator-panel.component';
import { PriceLevel } from './drawing-levels.component';
import {
  computeSMA,
  computeEMA,
  computeRSI,
  computeVolume,
  CandleInput,
} from './indicators.util';

@Component({
  selector: 'app-live-chart',
  standalone: true,
  template: `
    <div class="live-chart-wrap">
      <div class="chart-overlay">
        <span class="overlay-symbol">{{ symbol }}</span>
        @if (currentPrice) {
          <span class="overlay-prices">
            <span class="overlay-bid">{{ currentPrice.bid.toFixed(pricePrecision) }}</span>
            <span class="overlay-sep">/</span>
            <span class="overlay-ask">{{ currentPrice.ask.toFixed(pricePrecision) }}</span>
          </span>
        }
      </div>
      @if (loadingCandles) {
        <div class="chart-status">
          <svg class="spinner" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <circle cx="12" cy="12" r="10" stroke-dasharray="31.4" stroke-dashoffset="10"/>
          </svg>
          Loading...
        </div>
      }
      @if (candleError) {
        <div class="chart-status chart-error">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" style="width:18px;height:18px">
            <circle cx="12" cy="12" r="10"/><line x1="15" y1="9" x2="9" y2="15"/><line x1="9" y1="9" x2="15" y2="15"/>
          </svg>
          Failed to load chart data
          <button class="retry-btn" (click)="loadCandles()">Retry</button>
        </div>
      }
      <div #chartContainer class="chart-container"></div>
    </div>
  `,
  styles: [`
    :host {
      display: flex;
      flex-direction: column;
      flex: 1;
      min-height: 0;
    }

    .live-chart-wrap {
      position: relative;
      border: 1px solid var(--border);
      border-radius: 8px;
      overflow: hidden;
      background: #141b2d;
      flex: 1;
      min-height: 300px;
    }

    .chart-overlay {
      position: absolute;
      top: 8px;
      left: 12px;
      z-index: 2;
      display: flex;
      align-items: center;
      gap: 10px;
      pointer-events: none;
    }

    .overlay-symbol {
      font-weight: 700;
      font-size: 14px;
      color: var(--text-bright);
      font-family: var(--font-mono);
    }

    .overlay-prices {
      font-size: 12px;
      font-family: var(--font-mono);
    }

    .overlay-bid {
      color: #ef4444;
    }

    .overlay-sep {
      color: var(--text-muted);
      margin: 0 2px;
    }

    .overlay-ask {
      color: #22c55e;
    }

    .chart-container {
      width: 100%;
      height: 100%;
    }

    .chart-status {
      position: absolute;
      inset: 0;
      z-index: 3;
      display: flex;
      align-items: center;
      justify-content: center;
      gap: 8px;
      background: rgba(20, 27, 45, 0.9);
      color: var(--text-muted);
      font-size: 13px;
    }

    .chart-error {
      color: var(--danger);
    }

    .retry-btn {
      padding: 4px 12px;
      font-size: 12px;
      font-weight: 600;
      background: var(--surface-light);
      border: 1px solid var(--border);
      border-radius: 4px;
      color: var(--text);
      cursor: pointer;
      margin-left: 4px;
    }

    .retry-btn:hover {
      background: var(--surface);
      border-color: var(--text-muted);
    }

    .spinner {
      width: 16px;
      height: 16px;
      animation: spin 1s linear infinite;
    }

    @keyframes spin {
      to { transform: rotate(360deg); }
    }
  `]
})
export class LiveChartComponent implements AfterViewInit, OnDestroy, OnChanges {
  @ViewChild('chartContainer') chartContainer!: ElementRef<HTMLDivElement>;
  @Input() symbol = '';
  @Input() timeframe = 'H1';
  @Input() indicators: IndicatorConfig[] = [];
  @Input() priceLevels: PriceLevel[] = [];
  @Input() slPrice: number | null = null;
  @Input() tpPrice: number | null = null;

  currentPrice: PriceUpdate | null = null;
  pricePrecision = 5;
  loadingCandles = false;
  candleError = false;

  private chart: IChartApi | null = null;
  private candleSeries: ISeriesApi<'Candlestick'> | null = null;
  private resizeObserver: ResizeObserver | null = null;
  private priceSub: Subscription | null = null;
  private http = inject(HttpClient);
  private signalR = inject(SignalRService);

  private currentCandleTime = 0;
  private currentCandle: CandlestickData<Time> | null = null;
  private chartReady = false;
  private destroy$ = new Subject<void>();

  // Stored candle data for indicator recomputation
  private candleData: CandleInput[] = [];

  // Indicator series references
  private smaSeries: ISeriesApi<'Line'> | null = null;
  private emaSeries: ISeriesApi<'Line'> | null = null;
  private rsiSeries: ISeriesApi<'Line'> | null = null;
  private rsiPaneIndex: number = -1;
  private rsiUpperLine: IPriceLine | null = null;
  private rsiLowerLine: IPriceLine | null = null;
  private volumeSeries: ISeriesApi<'Histogram'> | null = null;
  private volumePaneIndex: number = -1;

  // SL/TP price lines
  private slLine: IPriceLine | null = null;
  private tpLine: IPriceLine | null = null;

  // Drawing level price lines
  private levelLines = new Map<string, IPriceLine>();

  // Track previous indicator state for diffing
  private prevIndicators: IndicatorConfig[] = [];

  ngAfterViewInit() {
    this.createChart();
    this.chartReady = true;
    if (this.symbol) {
      // Defer to next microtask to avoid NG0100 ExpressionChangedAfterItHasBeenCheckedError
      Promise.resolve().then(() => {
        this.loadCandles();
        this.subscribeToPrices();
      });
    }
  }

  ngOnChanges(changes: SimpleChanges) {
    if (changes['symbol'] && !changes['symbol'].firstChange && this.chartReady) {
      // Symbol changed: reload everything
      this.cleanupSubscription();
      this.removeAllIndicators();
      this.removeAllLevelLines();
      this.removeSLTPLines();
      this.currentCandle = null;
      this.currentCandleTime = 0;
      this.candleData = [];
      this.loadCandles();
      this.subscribeToPrices();
    }

    if (changes['timeframe'] && !changes['timeframe'].firstChange && this.chartReady) {
      this.removeAllIndicators();
      this.currentCandle = null;
      this.currentCandleTime = 0;
      this.candleData = [];
      this.loadCandles();
    }

    if (changes['indicators'] && this.chartReady && this.candleData.length > 0) {
      this.syncIndicators();
    }

    if (changes['priceLevels'] && this.chartReady) {
      this.syncLevelLines();
    }

    if ((changes['slPrice'] || changes['tpPrice']) && this.chartReady) {
      this.syncSLTPLines();
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

    this.resizeObserver = new ResizeObserver(entries => {
      for (const entry of entries) {
        const { width, height } = entry.contentRect;
        if (this.chart && width > 0 && height > 0) {
          this.chart.applyOptions({ width, height });
        }
      }
    });
    this.resizeObserver.observe(container);
  }

  loadCandles() {
    if (!this.symbol) return;

    this.loadingCandles = true;
    this.candleError = false;

    this.http.get<CandleData[]>(
      `${environment.apiUrl}/api/ai/candles/${this.symbol}?timeframe=${this.timeframe}&count=200`
    ).pipe(takeUntil(this.destroy$)).subscribe({
      next: (data) => {
        this.loadingCandles = false;
        if (!this.candleSeries) return;

        // Detect price precision from data
        if (data.length > 0) {
          const priceStr = data[0].close.toString();
          const decimalIdx = priceStr.indexOf('.');
          this.pricePrecision = decimalIdx >= 0 ? priceStr.length - decimalIdx - 1 : 2;
        }

        // Store raw candle data for indicator computation
        this.candleData = data.map(c => ({
          time: c.time,
          open: c.open,
          high: c.high,
          low: c.low,
          close: c.close,
          volume: c.volume,
        }));

        const chartData: CandlestickData<Time>[] = data.map(c => ({
          time: c.time as Time,
          open: c.open,
          high: c.high,
          low: c.low,
          close: c.close,
        }));

        this.candleSeries.setData(chartData);
        this.chart?.timeScale().fitContent();

        // Track the last candle for live updates
        if (chartData.length > 0) {
          const last = chartData[chartData.length - 1];
          this.currentCandleTime = last.time as number;
          this.currentCandle = { ...last };
        }

        // Apply indicators after candles loaded
        this.syncIndicators();
        this.syncLevelLines();
        this.syncSLTPLines();
      },
      error: () => {
        this.loadingCandles = false;
        this.candleError = true;
      }
    });
  }

  private subscribeToPrices() {
    if (!this.symbol) return;

    this.signalR.subscribeToSymbol(this.symbol);
    this.priceSub = this.signalR.priceUpdates$.subscribe(prices => {
      const price = prices.get(this.symbol);
      if (!price || !this.candleSeries) return;

      this.currentPrice = price;
      const mid = (price.bid + price.ask) / 2;
      const tickTime = Math.floor(new Date(price.timestamp).getTime() / 1000);
      const candleTime = this.floorToTimeframe(tickTime);

      if (candleTime === this.currentCandleTime && this.currentCandle) {
        // Update existing candle
        this.currentCandle.close = mid;
        this.currentCandle.high = Math.max(this.currentCandle.high, mid);
        this.currentCandle.low = Math.min(this.currentCandle.low, mid);
        this.candleSeries.update(this.currentCandle);
      } else if (candleTime > this.currentCandleTime) {
        // New candle
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
    });
  }

  private floorToTimeframe(timestamp: number): number {
    const duration = this.getTimeframeDurationSeconds();
    return Math.floor(timestamp / duration) * duration;
  }

  private getTimeframeDurationSeconds(): number {
    switch (this.timeframe) {
      case 'M15': return 15 * 60;
      case 'H1': return 60 * 60;
      case 'H4': return 4 * 60 * 60;
      case 'D1': return 24 * 60 * 60;
      default: return 60 * 60;
    }
  }

  // --- Indicator Management ---

  private syncIndicators() {
    if (!this.chart || !this.candleSeries || this.candleData.length === 0) return;

    for (const ind of this.indicators) {
      switch (ind.type) {
        case 'SMA':
          this.syncOverlay('sma', ind, () => computeSMA(this.candleData, ind.period));
          break;
        case 'EMA':
          this.syncOverlay('ema', ind, () => computeEMA(this.candleData, ind.period));
          break;
        case 'RSI':
          this.syncRSI(ind);
          break;
        case 'VOLUME':
          this.syncVolume(ind);
          break;
      }
    }

    this.prevIndicators = this.indicators.map(i => ({ ...i }));
  }

  private syncOverlay(key: 'sma' | 'ema', ind: IndicatorConfig, compute: () => { time: Time; value: number }[]) {
    const seriesKey = key === 'sma' ? 'smaSeries' : 'emaSeries';
    const series = this[seriesKey];

    if (ind.enabled) {
      const data = compute();
      if (!series) {
        // Create new line series on main pane (paneIndex 0)
        const newSeries = this.chart!.addSeries(LineSeries, {
          color: ind.color,
          lineWidth: 2,
          priceLineVisible: false,
          lastValueVisible: false,
        }, 0);
        newSeries.setData(data);
        this[seriesKey] = newSeries;
      } else {
        series.applyOptions({ color: ind.color });
        series.setData(data);
      }
    } else if (series) {
      this.chart!.removeSeries(series);
      this[seriesKey] = null;
    }
  }

  private syncRSI(ind: IndicatorConfig) {
    if (ind.enabled) {
      const data = computeRSI(this.candleData, ind.period);
      if (!this.rsiSeries) {
        // Create a new pane for RSI
        const pane = this.chart!.addPane();
        pane.setStretchFactor(0.25);
        this.rsiPaneIndex = pane.paneIndex();

        this.rsiSeries = pane.addSeries(LineSeries, {
          color: ind.color,
          lineWidth: 2,
          priceLineVisible: false,
          lastValueVisible: true,
        });
        this.rsiSeries.setData(data);

        // Add reference lines at 30 and 70
        this.rsiUpperLine = this.rsiSeries.createPriceLine({
          price: 70,
          color: 'rgba(239, 68, 68, 0.4)',
          lineWidth: 1,
          lineStyle: 2,
          axisLabelVisible: false,
          title: '',
        });
        this.rsiLowerLine = this.rsiSeries.createPriceLine({
          price: 30,
          color: 'rgba(34, 197, 94, 0.4)',
          lineWidth: 1,
          lineStyle: 2,
          axisLabelVisible: false,
          title: '',
        });
      } else {
        this.rsiSeries.applyOptions({ color: ind.color });
        this.rsiSeries.setData(data);
      }
    } else if (this.rsiSeries) {
      this.removeRSIPane();
    }
  }

  private removeRSIPane() {
    if (this.rsiSeries && this.chart) {
      // Find current pane index of the RSI series
      const panes = this.chart.panes();
      for (let i = panes.length - 1; i >= 1; i--) {
        const seriesInPane = panes[i].getSeries();
        if (seriesInPane.includes(this.rsiSeries as any)) {
          this.chart.removePane(i);
          break;
        }
      }
      this.rsiSeries = null;
      this.rsiUpperLine = null;
      this.rsiLowerLine = null;
      this.rsiPaneIndex = -1;
    }
  }

  private syncVolume(ind: IndicatorConfig) {
    if (ind.enabled) {
      const data = computeVolume(this.candleData);
      if (!this.volumeSeries) {
        const pane = this.chart!.addPane();
        pane.setStretchFactor(0.2);
        this.volumePaneIndex = pane.paneIndex();

        this.volumeSeries = pane.addSeries(HistogramSeries, {
          priceLineVisible: false,
          lastValueVisible: false,
        });
        this.volumeSeries.setData(data);
      } else {
        this.volumeSeries.setData(data);
      }
    } else if (this.volumeSeries) {
      this.removeVolumePane();
    }
  }

  private removeVolumePane() {
    if (this.volumeSeries && this.chart) {
      const panes = this.chart.panes();
      for (let i = panes.length - 1; i >= 1; i--) {
        const seriesInPane = panes[i].getSeries();
        if (seriesInPane.includes(this.volumeSeries as any)) {
          this.chart.removePane(i);
          break;
        }
      }
      this.volumeSeries = null;
      this.volumePaneIndex = -1;
    }
  }

  private removeAllIndicators() {
    if (!this.chart) return;
    if (this.smaSeries) {
      this.chart.removeSeries(this.smaSeries);
      this.smaSeries = null;
    }
    if (this.emaSeries) {
      this.chart.removeSeries(this.emaSeries);
      this.emaSeries = null;
    }
    this.removeRSIPane();
    this.removeVolumePane();
  }

  // --- SL/TP Price Lines ---

  private syncSLTPLines() {
    if (!this.candleSeries) return;

    // SL line
    if (this.slPrice != null && this.slPrice > 0) {
      if (this.slLine) {
        this.slLine.applyOptions({ price: this.slPrice });
      } else {
        this.slLine = this.candleSeries.createPriceLine({
          price: this.slPrice,
          color: '#ef4444',
          lineWidth: 1,
          lineStyle: 2,
          axisLabelVisible: true,
          title: 'SL',
        });
      }
    } else if (this.slLine) {
      this.candleSeries.removePriceLine(this.slLine);
      this.slLine = null;
    }

    // TP line
    if (this.tpPrice != null && this.tpPrice > 0) {
      if (this.tpLine) {
        this.tpLine.applyOptions({ price: this.tpPrice });
      } else {
        this.tpLine = this.candleSeries.createPriceLine({
          price: this.tpPrice,
          color: '#22c55e',
          lineWidth: 1,
          lineStyle: 2,
          axisLabelVisible: true,
          title: 'TP',
        });
      }
    } else if (this.tpLine) {
      this.candleSeries.removePriceLine(this.tpLine);
      this.tpLine = null;
    }
  }

  private removeSLTPLines() {
    if (this.candleSeries) {
      if (this.slLine) {
        this.candleSeries.removePriceLine(this.slLine);
        this.slLine = null;
      }
      if (this.tpLine) {
        this.candleSeries.removePriceLine(this.tpLine);
        this.tpLine = null;
      }
    }
  }

  // --- Drawing Level Lines ---

  private syncLevelLines() {
    if (!this.candleSeries) return;

    const currentIds = new Set(this.priceLevels.map(l => l.id));

    // Remove stale lines
    for (const [id, line] of this.levelLines) {
      if (!currentIds.has(id)) {
        this.candleSeries.removePriceLine(line);
        this.levelLines.delete(id);
      }
    }

    // Add or update lines
    for (const level of this.priceLevels) {
      const existing = this.levelLines.get(level.id);
      if (existing) {
        existing.applyOptions({
          price: level.price,
          color: level.color,
          title: level.label,
        });
      } else {
        const line = this.candleSeries.createPriceLine({
          price: level.price,
          color: level.color,
          lineWidth: 1,
          lineStyle: 0, // Solid
          axisLabelVisible: true,
          title: level.label,
        });
        this.levelLines.set(level.id, line);
      }
    }
  }

  private removeAllLevelLines() {
    if (this.candleSeries) {
      for (const [, line] of this.levelLines) {
        this.candleSeries.removePriceLine(line);
      }
    }
    this.levelLines.clear();
  }

  // --- Cleanup ---

  private cleanupSubscription() {
    if (this.priceSub) {
      this.priceSub.unsubscribe();
      this.priceSub = null;
    }
    if (this.symbol) {
      this.signalR.unsubscribeFromSymbol(this.symbol);
    }
    this.currentPrice = null;
  }

  ngOnDestroy() {
    this.destroy$.next();
    this.destroy$.complete();
    this.cleanupSubscription();
    this.resizeObserver?.disconnect();
    this.chart?.remove();
    this.chart = null;
    this.candleSeries = null;
    this.smaSeries = null;
    this.emaSeries = null;
    this.rsiSeries = null;
    this.volumeSeries = null;
    this.levelLines.clear();
  }
}
