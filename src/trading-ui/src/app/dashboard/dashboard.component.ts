import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Subscription } from 'rxjs';
import { environment } from '../../environments/environment';
import { SignalRService, PriceUpdate } from '../shared/services/signalr.service';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="dashboard">
      <header class="page-header">
        <h1>Dashboard</h1>
        <span class="connection-status" [class.connected]="isConnected">
          {{ isConnected ? 'Connected' : 'Disconnected' }}
        </span>
      </header>

      <div class="stats-grid">
        <div class="card stat-card">
          <div class="stat-label">Balance</div>
          <div class="stat-value">{{ account.balance | currency }}</div>
        </div>
        <div class="card stat-card">
          <div class="stat-label">Equity</div>
          <div class="stat-value">{{ account.equity | currency }}</div>
        </div>
        <div class="card stat-card">
          <div class="stat-label">Open P&L</div>
          <div class="stat-value" [class.positive]="totalPnL > 0" [class.negative]="totalPnL < 0">
            {{ totalPnL | currency }}
          </div>
        </div>
        <div class="card stat-card">
          <div class="stat-label">Free Margin</div>
          <div class="stat-value">{{ account.freeMargin | currency }}</div>
        </div>
      </div>

      @if (marginLevel > 0) {
        <div class="margin-bar" [class.margin-warning]="marginLevel <= 100" [class.margin-danger]="marginLevel <= 50">
          <div class="margin-info">
            <span class="margin-label">Margin Level</span>
            <span class="margin-value">{{ marginLevel | number:'1.1-1' }}%</span>
          </div>
          <div class="margin-track">
            <div class="margin-fill" [style.width.%]="marginFillWidth"></div>
            <div class="margin-marker margin-call-marker" title="Margin Call (100%)"></div>
            <div class="margin-marker stop-out-marker" title="Stop-Out (50%)"></div>
          </div>
          @if (marginLevel <= 100) {
            <span class="margin-status">
              {{ marginLevel <= 50 ? 'STOP-OUT' : 'MARGIN CALL' }}
            </span>
          }
        </div>
      }

      <div class="grid-2">
        <div class="card">
          <h3>Open Positions</h3>
          <div class="positions-list">
            @if (positions.length === 0) {
              <p class="empty-state">No open positions</p>
            } @else {
              <div class="table-scroll">
                <table>
                  <thead>
                    <tr>
                      <th>Symbol</th>
                      <th>Direction</th>
                      <th>Volume</th>
                      <th>Price</th>
                      <th>P&L</th>
                    </tr>
                  </thead>
                  <tbody>
                    @for (pos of positions; track pos.positionId) {
                      <tr>
                        <td data-label="Symbol">{{ pos.symbol }}</td>
                        <td data-label="Direction">
                          <span class="dir-badge" [class.buy]="pos.direction === 'Buy'" [class.sell]="pos.direction === 'Sell'">
                            {{ pos.direction }}
                          </span>
                        </td>
                        <td data-label="Volume">{{ pos.volume }}</td>
                        <td data-label="Price">
                          <span class="price-cell">
                            <span class="entry-price">{{ pos.entryPrice }}</span>
                            <span class="price-arrow">&#8594;</span>
                            <span class="current-price">{{ pos.currentPrice }}</span>
                          </span>
                        </td>
                        <td data-label="P&L">
                          <span class="pnl-badge" [class.positive]="pos.pnL > 0" [class.negative]="pos.pnL < 0">
                            {{ pos.pnL >= 0 ? '+' : '' }}{{ pos.pnL | currency }}
                          </span>
                        </td>
                      </tr>
                    }
                  </tbody>
                </table>
              </div>
            }
          </div>
        </div>

        <div class="card">
          <h3>Watchlist</h3>
          <div class="watchlist-widget">
            @if (watchlistSymbols.length === 0) {
              <p class="empty-state">No watchlist symbols</p>
            } @else {
              <div class="watchlist-list">
                @for (sym of watchlistSymbols; track sym) {
                  <div class="watchlist-row">
                    <span class="watchlist-name">{{ sym }}</span>
                    @if (livePrices.has(sym)) {
                      <span class="watchlist-prices">
                        <span class="bid">{{ livePrices.get(sym)!.bid }}</span>
                        <span class="spread">{{ getSpread(sym) }}</span>
                        <span class="ask">{{ livePrices.get(sym)!.ask }}</span>
                      </span>
                    } @else {
                      <span class="watchlist-prices loading">--</span>
                    }
                  </div>
                }
              </div>
            }
            @if (scheduleHours.length > 0) {
              <div class="next-scan">
                <span class="scan-label">Next scan</span>
                <span class="scan-value">{{ nextScanDisplay }}</span>
              </div>
            }
          </div>
        </div>
      </div>

      @if (pendingOrders.length > 0) {
        <div class="card pending-orders-card" style="margin-top: 16px;">
          <h3>Pending Orders</h3>
          <div class="pending-list">
            @for (order of pendingOrders; track order.approvalToken) {
              <div class="pending-item">
                <div class="pending-info">
                  <strong>{{ order.symbol }}</strong>
                  <span [class.buy]="order.direction === 'Buy'" [class.sell]="order.direction === 'Sell'">
                    {{ order.direction }}
                  </span>
                  <span>{{ order.volume }} lots</span>
                  <span class="text-muted">Entry: {{ order.entryPrice }} | SL: {{ order.stopLoss }} | TP: {{ order.takeProfit }}</span>
                  <span class="expires">Expires {{ order.expiresAt | date:'shortTime' }}</span>
                </div>
                <div class="pending-actions">
                  <button class="success small" (click)="approveOrder(order.approvalToken)" [disabled]="order.processing">
                    {{ order.processing ? '...' : 'Approve' }}
                  </button>
                  <button class="danger small" (click)="rejectOrder(order.approvalToken)" [disabled]="order.processing">
                    Reject
                  </button>
                </div>
              </div>
            }
          </div>
        </div>
      }

      <div class="grid-2" style="margin-top: 16px;">
        <div class="card">
          <h3>Recent Alerts</h3>
          <div class="alerts-list">
            @if (alerts.length === 0) {
              <p class="empty-state">No recent alerts</p>
            } @else {
              @for (alert of alerts; track alert.alertId) {
                <div class="alert-item">
                  <span class="alert-symbol">{{ alert.symbol }}</span>
                  <span class="alert-message">{{ alert.message }}</span>
                  <span class="alert-time">{{ alert.triggeredAt | date:'short' }}</span>
                </div>
              }
            }
          </div>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .page-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-bottom: 24px;
    }

    .connection-status {
      padding: 6px 12px;
      border-radius: 20px;
      font-size: 12px;
      background: var(--danger);
      color: white;

      &.connected {
        background: var(--success);
      }
    }

    .stats-grid {
      display: grid;
      grid-template-columns: repeat(4, 1fr);
      gap: 16px;
      margin-bottom: 24px;
    }

    .stat-card {
      text-align: center;
    }

    .stat-label {
      color: var(--text-muted);
      font-size: 14px;
      margin-bottom: 8px;
    }

    .stat-value {
      font-size: 24px;
      font-weight: 600;
    }

    .grid-2 {
      display: grid;
      grid-template-columns: repeat(2, 1fr);
      gap: 16px;
    }

    .card h3 {
      margin-bottom: 16px;
      font-size: 16px;
    }

    .empty-state {
      color: var(--text-muted);
      text-align: center;
      padding: 40px 0;
    }

    /* Position table enhancements */
    .dir-badge {
      display: inline-block;
      padding: 2px 8px;
      border-radius: 4px;
      font-size: 12px;
      font-weight: 600;

      &.buy {
        color: var(--success);
        background: rgba(34, 197, 94, 0.1);
      }
      &.sell {
        color: var(--danger);
        background: rgba(239, 68, 68, 0.1);
      }
    }

    .price-cell {
      display: inline-flex;
      align-items: center;
      gap: 4px;
      font-family: var(--font-mono);
      font-size: 13px;
    }

    .entry-price {
      color: var(--text-muted);
    }

    .price-arrow {
      color: var(--text-muted);
      font-size: 11px;
    }

    .current-price {
      font-weight: 600;
    }

    .pnl-badge {
      display: inline-block;
      padding: 2px 8px;
      border-radius: 4px;
      font-weight: 600;
      font-family: var(--font-mono);
      font-size: 13px;

      &.positive {
        color: var(--success);
        background: rgba(34, 197, 94, 0.1);
      }
      &.negative {
        color: var(--danger);
        background: rgba(239, 68, 68, 0.1);
      }
    }

    /* Watchlist with live prices */
    .watchlist-list {
      display: flex;
      flex-direction: column;
      gap: 0;
      margin-bottom: 12px;
    }

    .watchlist-row {
      display: flex;
      justify-content: space-between;
      align-items: center;
      padding: 8px 0;
      border-bottom: 1px solid var(--border);

      &:last-child {
        border-bottom: none;
      }
    }

    .watchlist-name {
      font-weight: 600;
      font-family: var(--font-mono);
      font-size: 13px;
    }

    .watchlist-prices {
      display: flex;
      align-items: center;
      gap: 6px;
      font-family: var(--font-mono);
      font-size: 13px;

      &.loading {
        color: var(--text-muted);
      }
    }

    .bid {
      color: var(--success);
    }

    .ask {
      color: var(--danger);
    }

    .spread {
      font-size: 10px;
      color: var(--text-muted);
      padding: 1px 4px;
      background: var(--surface-light);
      border-radius: 3px;
    }

    .alert-item {
      display: flex;
      gap: 12px;
      padding: 12px 0;
      border-bottom: 1px solid var(--border);

      &:last-child {
        border-bottom: none;
      }
    }

    .alert-symbol {
      font-weight: 600;
    }

    .alert-message {
      flex: 1;
      color: var(--text-muted);
    }

    .alert-time {
      font-size: 12px;
      color: var(--text-muted);
    }

    .next-scan {
      display: flex;
      align-items: center;
      gap: 8px;
      padding-top: 8px;
      border-top: 1px solid var(--border);
      font-size: 13px;
    }

    .scan-label {
      color: var(--text-muted);
    }

    .scan-value {
      font-weight: 600;
    }

    .watchlist-widget .empty-state {
      padding: 20px 0;
    }

    .pending-orders-card h3 {
      color: var(--warning, #eab308);
    }

    .pending-item {
      display: flex;
      justify-content: space-between;
      align-items: center;
      padding: 12px 0;
      border-bottom: 1px solid var(--border);
    }

    .pending-item:last-child {
      border-bottom: none;
    }

    .pending-info {
      display: flex;
      gap: 12px;
      align-items: center;
      flex-wrap: wrap;
      font-size: 14px;
    }

    .pending-actions {
      display: flex;
      gap: 8px;
      flex-shrink: 0;
    }

    .buy { color: var(--success); font-weight: 600; }
    .sell { color: var(--danger); font-weight: 600; }

    .expires {
      font-size: 12px;
      color: var(--text-muted);
    }

    button.small {
      padding: 4px 10px;
      font-size: 12px;
    }

    /* Margin level bar */
    .margin-bar {
      display: flex;
      align-items: center;
      gap: 16px;
      padding: 10px 16px;
      margin-bottom: 24px;
      background: var(--surface);
      border: 1px solid var(--border);
      border-radius: var(--radius);
      font-size: 13px;
    }

    .margin-bar.margin-warning {
      border-color: var(--warning, #eab308);
      background: rgba(234, 179, 8, 0.05);
    }

    .margin-bar.margin-danger {
      border-color: var(--danger);
      background: rgba(239, 68, 68, 0.08);
      animation: pulse-danger 1.5s ease-in-out infinite;
    }

    @keyframes pulse-danger {
      0%, 100% { opacity: 1; }
      50% { opacity: 0.85; }
    }

    .margin-info {
      display: flex;
      gap: 8px;
      align-items: center;
      white-space: nowrap;
    }

    .margin-label {
      color: var(--text-muted);
    }

    .margin-value {
      font-weight: 700;
      font-family: var(--font-mono);
    }

    .margin-warning .margin-value {
      color: var(--warning, #eab308);
    }

    .margin-danger .margin-value {
      color: var(--danger);
    }

    .margin-track {
      flex: 1;
      height: 6px;
      background: var(--surface-light);
      border-radius: 3px;
      position: relative;
      min-width: 80px;
    }

    .margin-fill {
      height: 100%;
      border-radius: 3px;
      background: var(--success);
      transition: width 0.3s ease;
    }

    .margin-warning .margin-fill {
      background: var(--warning, #eab308);
    }

    .margin-danger .margin-fill {
      background: var(--danger);
    }

    .margin-marker {
      position: absolute;
      top: -3px;
      width: 2px;
      height: 12px;
      border-radius: 1px;
    }

    .margin-call-marker {
      left: 33.3%;
      background: var(--warning, #eab308);
    }

    .stop-out-marker {
      left: 16.7%;
      background: var(--danger);
    }

    .margin-status {
      font-weight: 700;
      font-size: 11px;
      text-transform: uppercase;
      letter-spacing: 0.5px;
      padding: 2px 8px;
      border-radius: 4px;
      white-space: nowrap;
    }

    .margin-warning .margin-status {
      color: var(--warning, #eab308);
      background: rgba(234, 179, 8, 0.15);
    }

    .margin-danger .margin-status {
      color: var(--danger);
      background: rgba(239, 68, 68, 0.15);
    }

    @media (max-width: 768px) {
      .stats-grid {
        grid-template-columns: repeat(2, 1fr);
      }

      .grid-2 {
        grid-template-columns: 1fr;
      }

      .stat-value {
        font-size: 20px;
      }

      .pending-item {
        flex-direction: column;
        align-items: flex-start;
        gap: 8px;
      }

      .pending-actions {
        width: 100%;
      }

      .pending-actions button {
        flex: 1;
      }

      .price-cell {
        font-size: 12px;
      }

      .margin-bar {
        flex-wrap: wrap;
        gap: 8px;
      }

      .margin-track {
        order: 3;
        width: 100%;
        flex-basis: 100%;
      }
    }

    @media (max-width: 480px) {
      .stat-value {
        font-size: 18px;
      }
    }
  `]
})
export class DashboardComponent implements OnInit, OnDestroy {
  isConnected = false;

  account = {
    balance: 0,
    equity: 0,
    unrealizedPnL: 0,
    freeMargin: 0
  };

  positions: any[] = [];
  alerts: any[] = [];
  watchlistSymbols: string[] = [];
  scheduleHours: number[] = [];
  pendingOrders: any[] = [];
  livePrices = new Map<string, PriceUpdate>();
  marginLevel = 0;

  private subs: Subscription[] = [];
  private subscribedSymbols = new Set<string>();
  private pendingOrdersTimer: any;

  constructor(private signalR: SignalRService, private http: HttpClient) {}

  get totalPnL(): number {
    return this.positions.reduce((sum, p) => sum + (p.pnL || 0), 0);
  }

  /** Clamp margin level to 0-300% range for the progress bar (300% = 100% fill) */
  get marginFillWidth(): number {
    return Math.min(Math.max(this.marginLevel / 3, 0), 100);
  }

  getSpread(symbol: string): string {
    const p = this.livePrices.get(symbol);
    if (!p) return '';
    return (p.ask - p.bid).toFixed(5);
  }

  ngOnInit() {
    this.loadAccount();
    this.loadPositions();
    this.loadWatchlist();
    this.loadPendingOrders();

    this.subs.push(
      this.signalR.connectionState$.subscribe({
        next: connected => {
          this.isConnected = connected;
          // Re-subscribe to symbols when reconnected
          if (connected) {
            this.subscribedSymbols.forEach(sym => this.signalR.subscribeToSymbol(sym));
          }
        }
      })
    );

    // Merge individual position updates into existing positions
    this.subs.push(
      this.signalR.positions$.subscribe({
        next: updates => {
          for (const update of updates) {
            const pos = this.positions.find(p => p.positionId === update.positionId);
            if (pos) {
              pos.currentPrice = update.currentPrice;
              pos.pnL = update.pnL;
            }
          }
        }
      })
    );

    this.subs.push(
      this.signalR.alerts$.subscribe({
        next: alerts => { this.alerts = alerts.slice(0, 5); }
      })
    );

    this.subs.push(
      this.signalR.accountUpdates$.subscribe({
        next: update => {
          if (update) {
            this.account = {
              balance: update.balance,
              equity: update.equity,
              unrealizedPnL: update.unrealizedPnL,
              freeMargin: update.freeMargin
            };
            this.marginLevel = update.marginLevel ?? 0;
          }
        }
      })
    );

    // Live price updates for watchlist display
    this.subs.push(
      this.signalR.priceUpdates$.subscribe({
        next: prices => {
          this.livePrices = prices;
        }
      })
    );
  }

  loadAccount() {
    this.http.get<any>(`${environment.apiUrl}/api/positions/account`).subscribe({
      next: (data) => {
        this.account = data;
      },
      error: () => {
        setTimeout(() => this.loadAccount(), 10000);
      }
    });
  }

  loadPositions() {
    this.http.get<any[]>(`${environment.apiUrl}/api/positions`).subscribe({
      next: (data) => {
        this.positions = data.map(p => ({
          positionId: p.cTraderPositionId,
          symbol: p.symbol,
          direction: p.direction,
          volume: p.volume,
          notionalUsd: p.notionalUsd,
          entryPrice: p.entryPrice,
          currentPrice: p.currentPrice,
          pnL: p.unrealizedPnL
        }));
        // Subscribe to price streams for all open position symbols
        const symbols = [...new Set(data.map((p: any) => p.symbol))];
        symbols.forEach(sym => this.subscribeSymbol(sym));
      },
      error: () => {
        setTimeout(() => this.loadPositions(), 10000);
      }
    });
  }

  get nextScanDisplay(): string {
    if (this.scheduleHours.length === 0) return 'Not configured';
    const now = new Date();
    const currentUtcHour = now.getUTCHours();
    const nextHour = this.scheduleHours.find(h => h > currentUtcHour)
      ?? this.scheduleHours[0]; // wrap to next day
    const isToday = nextHour > currentUtcHour;
    return `${nextHour.toString().padStart(2, '0')}:00 UTC${isToday ? '' : ' (tomorrow)'}`;
  }

  loadWatchlist() {
    this.http.get<any>(`${environment.apiUrl}/api/watchlist`).subscribe({
      next: (data) => {
        this.watchlistSymbols = data.symbols.map((s: any) => s.symbol);
        this.scheduleHours = data.scheduleUtcHours;
        // Subscribe to price streams for watchlist symbols
        this.watchlistSymbols.forEach(sym => this.subscribeSymbol(sym));
      },
      error: () => {}
    });
  }

  loadPendingOrders() {
    this.http.get<any[]>(`${environment.apiUrl}/api/orders/pending`).subscribe({
      next: (data) => this.pendingOrders = data.map(o => ({ ...o, processing: false })),
      error: () => {}
    });
    this.pendingOrdersTimer = setTimeout(() => this.loadPendingOrders(), 30000);
  }

  approveOrder(token: string) {
    const order = this.pendingOrders.find(o => o.approvalToken === token);
    if (order) order.processing = true;
    this.http.post(`${environment.apiUrl}/api/orders/${token}/approve`, {}).subscribe({
      next: () => {
        this.loadPendingOrders();
        this.loadPositions();
      },
      error: () => { if (order) order.processing = false; }
    });
  }

  rejectOrder(token: string) {
    this.http.post(`${environment.apiUrl}/api/orders/${token}/reject`, {}).subscribe({
      next: () => this.loadPendingOrders(),
      error: () => {}
    });
  }

  private subscribeSymbol(symbol: string) {
    if (!this.subscribedSymbols.has(symbol)) {
      this.subscribedSymbols.add(symbol);
      this.signalR.subscribeToSymbol(symbol);
    }
  }

  ngOnDestroy() {
    this.subs.forEach(s => s.unsubscribe());
    clearTimeout(this.pendingOrdersTimer);
  }
}
