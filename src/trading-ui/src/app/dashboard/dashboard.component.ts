import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../environments/environment';
import { SignalRService } from '../shared/services/signalr.service';

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
          <div class="stat-value" [class.positive]="account.unrealizedPnL > 0" [class.negative]="account.unrealizedPnL < 0">
            {{ account.unrealizedPnL | currency }}
          </div>
        </div>
        <div class="card stat-card">
          <div class="stat-label">Free Margin</div>
          <div class="stat-value">{{ account.freeMargin | currency }}</div>
        </div>
      </div>

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
                      <th>Notional</th>
                      <th>P&L</th>
                    </tr>
                  </thead>
                  <tbody>
                    @for (pos of positions; track pos.positionId) {
                      <tr>
                        <td data-label="Symbol">{{ pos.symbol }}</td>
                        <td data-label="Direction">{{ pos.direction }}</td>
                        <td data-label="Volume">{{ pos.volume }}</td>
                        <td data-label="Notional">{{ pos.notionalUsd | currency }}</td>
                        <td data-label="P&L" [class.positive]="pos.pnL > 0" [class.negative]="pos.pnL < 0">
                          {{ pos.pnL | currency }}
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
              <div class="watchlist-symbols">
                @for (sym of watchlistSymbols; track sym) {
                  <span class="watchlist-chip">{{ sym }}</span>
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

    .watchlist-symbols {
      display: flex;
      flex-wrap: wrap;
      gap: 8px;
      margin-bottom: 12px;
    }

    .watchlist-chip {
      display: inline-block;
      padding: 4px 12px;
      background: var(--surface-light);
      border: 1px solid var(--border);
      border-radius: var(--radius);
      font-size: 13px;
      font-weight: 600;
      font-family: var(--font-mono);
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

  constructor(private signalR: SignalRService, private http: HttpClient) {}

  ngOnInit() {
    // Load initial data via HTTP
    this.loadAccount();
    this.loadPositions();
    this.loadWatchlist();
    this.loadPendingOrders();

    // Connect SignalR for real-time updates
    this.signalR.connect();

    this.signalR.connectionState$.subscribe(connected => {
      this.isConnected = connected;
    });

    this.signalR.positions$.subscribe(positions => {
      if (positions.length > 0) {
        this.positions = positions;
      }
    });

    this.signalR.alerts$.subscribe(alerts => {
      this.alerts = alerts.slice(0, 5);
    });

    this.signalR.accountUpdates$.subscribe(update => {
      if (update) {
        this.account = update;
      }
    });
  }

  loadAccount() {
    this.http.get<any>(`${environment.apiUrl}/api/positions/account`).subscribe({
      next: (data) => {
        this.account = data;
      },
      error: () => {
        // Retry after 10s — account may not be synced yet
        setTimeout(() => this.loadAccount(), 10000);
      }
    });
  }

  loadPositions() {
    this.http.get<any[]>(`${environment.apiUrl}/api/positions`).subscribe({
      next: (data) => {
        this.positions = data.map(p => ({
          positionId: p.id,
          symbol: p.symbol,
          direction: p.direction,
          volume: p.volume,
          notionalUsd: p.notionalUsd,
          entryPrice: p.entryPrice,
          currentPrice: p.currentPrice,
          pnL: p.unrealizedPnL
        }));
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
      }
    });
  }

  loadPendingOrders() {
    this.http.get<any[]>(`${environment.apiUrl}/api/orders/pending`).subscribe({
      next: (data) => this.pendingOrders = data.map(o => ({ ...o, processing: false }))
    });
    // Poll every 30s for new pending orders
    setTimeout(() => this.loadPendingOrders(), 30000);
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
      next: () => this.loadPendingOrders()
    });
  }

  ngOnDestroy() {
    this.signalR.disconnect();
  }
}
