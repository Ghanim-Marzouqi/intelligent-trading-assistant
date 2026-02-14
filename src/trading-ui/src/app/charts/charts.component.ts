import { Component, OnInit, OnDestroy, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Subscription } from 'rxjs';
import { environment } from '../../environments/environment';
import { SignalRService, PriceUpdate } from '../shared/services/signalr.service';
import { LiveChartComponent } from './live-chart.component';
import { SymbolBarComponent, SymbolInfo } from './symbol-bar.component';
import { TradePanelComponent, OrderForm } from './trade-panel.component';
import { IndicatorPanelComponent, IndicatorConfig } from './indicator-panel.component';
import { DrawingLevelsComponent, PriceLevel } from './drawing-levels.component';

interface WatchlistEntry {
  id: number;
  symbol: string;
  addedAt: string;
}

interface WatchlistResponse {
  symbols: WatchlistEntry[];
  scheduleUtcHours: number[];
  autoPrepareMinConfidence: number;
  maxOpenPositions: number;
  maxTotalVolume: number;
  maxPositionsPerSymbol: number;
  maxDailyLossPercent: number;
}

interface OrderResult {
  success: boolean;
  orderId?: number;
  positionId?: number;
  errorMessage?: string;
  errorCode?: string;
}

@Component({
  selector: 'app-charts',
  standalone: true,
  imports: [
    CommonModule,
    LiveChartComponent,
    SymbolBarComponent,
    TradePanelComponent,
    IndicatorPanelComponent,
    DrawingLevelsComponent,
  ],
  template: `
    <div class="terminal-page">
      @if (loading) {
        <div class="loading-state">
          <svg class="spinner" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <circle cx="12" cy="12" r="10" stroke-dasharray="31.4" stroke-dashoffset="10"/>
          </svg>
          Loading...
        </div>
      } @else {
        <app-symbol-bar
          [watchlistSymbols]="watchlistSymbols"
          [allSymbols]="allSymbols"
          [selectedSymbol]="selectedSymbol"
          [timeframe]="timeframe"
          (symbolChanged)="onSymbolChange($event)"
          (timeframeChanged)="onTimeframeChange($event)">
        </app-symbol-bar>

        <!-- Result Banner -->
        @if (resultBanner) {
          <div class="result-banner" [class.success]="resultBanner.success" [class.error]="!resultBanner.success">
            <span>{{ resultBanner.message }}</span>
            <button class="banner-close" (click)="resultBanner = null">&times;</button>
          </div>
        }

        <div class="terminal-body">
          <div class="chart-area">
            @if (selectedSymbol) {
              <app-live-chart
                [symbol]="selectedSymbol"
                [timeframe]="timeframe"
                [indicators]="indicators"
                [priceLevels]="priceLevels"
                [slPrice]="orderForm.stopLoss"
                [tpPrice]="orderForm.takeProfit">
              </app-live-chart>
            } @else {
              <div class="empty-chart">
                <p>Select a symbol to begin</p>
              </div>
            }
          </div>

          <div class="side-panel">
            <div class="panel-section">
              <app-trade-panel
                [symbolInfo]="selectedSymbolInfo"
                [currentPrice]="currentPrice"
                [orderForm]="orderForm"
                [submitting]="submitting"
                (orderFormChanged)="onOrderFormChange($event)"
                (orderSubmitted)="submitOrder()">
              </app-trade-panel>
            </div>

            <details class="panel-section collapsible" open>
              <summary class="section-toggle">Indicators</summary>
              <div class="section-body">
                <app-indicator-panel
                  [indicators]="indicators"
                  (indicatorsChanged)="indicators = $event">
                </app-indicator-panel>
              </div>
            </details>

            <details class="panel-section collapsible">
              <summary class="section-toggle">Price Levels</summary>
              <div class="section-body">
                <app-drawing-levels
                  [levels]="priceLevels"
                  [currentPrice]="midPrice"
                  [digits]="selectedSymbolInfo?.digits ?? 5"
                  (levelsChanged)="priceLevels = $event">
                </app-drawing-levels>
              </div>
            </details>
          </div>
        </div>
      }
    </div>
  `,
  styles: [`
    .terminal-page {
      display: flex;
      flex-direction: column;
      gap: 12px;
      height: 100%;
    }

    .loading-state {
      display: flex;
      align-items: center;
      justify-content: center;
      gap: 10px;
      padding: 60px 20px;
      color: var(--text-muted);
      font-size: 14px;
    }

    .spinner {
      width: 20px;
      height: 20px;
      animation: spin 1s linear infinite;
    }

    @keyframes spin {
      to { transform: rotate(360deg); }
    }

    .result-banner {
      display: flex;
      justify-content: space-between;
      align-items: center;
      padding: 10px 14px;
      border-radius: var(--radius);
      font-size: 13px;
    }
    .result-banner.success {
      background: var(--success-glow);
      border: 1px solid var(--success);
      color: var(--success);
    }
    .result-banner.error {
      background: var(--danger-glow);
      border: 1px solid var(--danger);
      color: var(--danger);
    }
    .banner-close {
      background: none;
      color: inherit;
      font-size: 18px;
      padding: 0 4px;
      border: none;
      cursor: pointer;
    }

    .terminal-body {
      display: flex;
      gap: 12px;
      flex: 1;
      min-height: 0;
    }

    .chart-area {
      flex: 1;
      min-width: 0;
      display: flex;
      flex-direction: column;
    }

    .empty-chart {
      flex: 1;
      display: flex;
      align-items: center;
      justify-content: center;
      background: #141b2d;
      border: 1px solid var(--border);
      border-radius: 8px;
      color: var(--text-muted);
      font-size: 14px;
      min-height: 400px;
    }

    .side-panel {
      width: 280px;
      flex-shrink: 0;
      display: flex;
      flex-direction: column;
      gap: 12px;
      overflow-y: auto;
      max-height: calc(100vh - 160px);
    }

    .panel-section {
      background: var(--surface);
      border: 1px solid var(--border);
      border-radius: var(--radius);
      padding: 14px;
    }

    .collapsible {
      padding: 0;
    }
    .collapsible .section-body {
      padding: 0 14px 14px;
    }

    .section-toggle {
      padding: 12px 14px;
      font-size: 13px;
      font-weight: 700;
      color: var(--text-bright);
      cursor: pointer;
      user-select: none;
      list-style: none;
    }
    .section-toggle::-webkit-details-marker { display: none; }
    .section-toggle::before {
      content: '\\25B6';
      display: inline-block;
      margin-right: 8px;
      font-size: 10px;
      transition: transform 0.15s;
    }
    details[open] > .section-toggle::before {
      transform: rotate(90deg);
    }

    @media (max-width: 768px) {
      .terminal-body {
        flex-direction: column;
      }

      .side-panel {
        width: 100%;
        max-height: none;
        overflow-y: visible;
      }

      .chart-area {
        min-height: 350px;
      }
    }
  `]
})
export class ChartsComponent implements OnInit, OnDestroy {
  private http = inject(HttpClient);
  private signalR = inject(SignalRService);

  loading = true;
  watchlistSymbols: string[] = [];
  allSymbols: SymbolInfo[] = [];
  selectedSymbol = '';
  selectedSymbolInfo: SymbolInfo | null = null;
  timeframe = 'H1';
  currentPrice: PriceUpdate | null = null;
  submitting = false;
  resultBanner: { success: boolean; message: string } | null = null;

  indicators: IndicatorConfig[] = [
    { type: 'SMA', period: 20, enabled: false, color: '#f59e0b' },
    { type: 'EMA', period: 50, enabled: false, color: '#8b5cf6' },
    { type: 'RSI', period: 14, enabled: false, color: '#06b6d4' },
    { type: 'VOLUME', period: 0, enabled: false, color: '#64748b' },
  ];

  priceLevels: PriceLevel[] = [];

  orderForm: OrderForm = {
    symbol: '',
    direction: 'Buy',
    orderType: 'Market',
    volume: 0.01,
    price: null,
    stopLoss: null,
    takeProfit: null,
  };

  private priceSub: Subscription | null = null;
  private subscribedSymbol: string | null = null;

  get midPrice(): number {
    if (!this.currentPrice) return 0;
    return (this.currentPrice.bid + this.currentPrice.ask) / 2;
  }

  ngOnInit() {
    // Load watchlist and all symbols in parallel
    this.http.get<WatchlistResponse>(`${environment.apiUrl}/api/watchlist`).subscribe({
      next: (data) => {
        this.watchlistSymbols = data.symbols.map(s => s.symbol);
        if (this.watchlistSymbols.length > 0 && !this.selectedSymbol) {
          this.onSymbolChange(this.watchlistSymbols[0]);
        }
        this.loading = false;
      },
      error: () => {
        this.loading = false;
      }
    });

    this.http.get<SymbolInfo[]>(`${environment.apiUrl}/api/positions/symbols`).subscribe({
      next: (data) => {
        this.allSymbols = data;
        // If a symbol was already selected before symbols loaded, update info + volume
        if (this.selectedSymbol && !this.selectedSymbolInfo) {
          this.selectedSymbolInfo = data.find(s => s.name === this.selectedSymbol) ?? null;
          if (this.selectedSymbolInfo) {
            this.orderForm = {
              ...this.orderForm,
              volume: this.selectedSymbolInfo.minVolume,
            };
          }
        }
      },
      error: () => {}
    });
  }

  onSymbolChange(symbol: string) {
    if (symbol === this.selectedSymbol) return;

    // Cleanup previous subscription
    this.cleanupPriceSub();

    this.selectedSymbol = symbol;
    this.selectedSymbolInfo = this.allSymbols.find(s => s.name === symbol) ?? null;
    this.orderForm = {
      ...this.orderForm,
      symbol,
      volume: this.selectedSymbolInfo?.minVolume ?? 0.01,
      price: null,
      stopLoss: null,
      takeProfit: null,
    };
    this.currentPrice = null;
    this.priceLevels = [];

    // Subscribe to live prices
    if (symbol) {
      this.signalR.subscribeToSymbol(symbol);
      this.subscribedSymbol = symbol;
      this.priceSub = this.signalR.priceUpdates$.subscribe(prices => {
        const price = prices.get(symbol);
        if (price) {
          this.currentPrice = price;
          // Update symbolInfo if loaded later
          if (!this.selectedSymbolInfo) {
            this.selectedSymbolInfo = this.allSymbols.find(s => s.name === symbol) ?? null;
          }
        }
      });
    }
  }

  onTimeframeChange(tf: string) {
    this.timeframe = tf;
  }

  onOrderFormChange(form: OrderForm) {
    this.orderForm = form;
  }

  submitOrder() {
    this.submitting = true;
    this.resultBanner = null;

    const body: any = {
      symbol: this.orderForm.symbol,
      direction: this.orderForm.direction,
      orderType: this.orderForm.orderType,
      volume: this.orderForm.volume,
    };
    if (this.orderForm.orderType !== 'Market' && this.orderForm.price) {
      body.price = this.orderForm.price;
    }
    if (this.orderForm.stopLoss) {
      body.stopLoss = this.orderForm.stopLoss;
    }
    if (this.orderForm.takeProfit) {
      body.takeProfit = this.orderForm.takeProfit;
    }

    this.http.post<OrderResult>(`${environment.apiUrl}/api/positions/open`, body).subscribe({
      next: (result) => {
        this.submitting = false;
        if (result.success) {
          this.resultBanner = {
            success: true,
            message: `Order placed. Position ID: ${result.positionId ?? result.orderId ?? '-'}`
          };
          // Reset SL/TP after successful order
          this.orderForm = { ...this.orderForm, stopLoss: null, takeProfit: null };
        } else {
          this.resultBanner = {
            success: false,
            message: result.errorMessage ?? 'Order failed'
          };
        }
      },
      error: (err) => {
        this.submitting = false;
        this.resultBanner = {
          success: false,
          message: err.error?.error ?? err.message ?? 'Order failed'
        };
      }
    });
  }

  private cleanupPriceSub() {
    if (this.priceSub) {
      this.priceSub.unsubscribe();
      this.priceSub = null;
    }
    if (this.subscribedSymbol) {
      this.signalR.unsubscribeFromSymbol(this.subscribedSymbol);
      this.subscribedSymbol = null;
    }
  }

  ngOnDestroy() {
    this.cleanupPriceSub();
  }
}
