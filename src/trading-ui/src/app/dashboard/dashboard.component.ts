import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
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
              <table>
                <thead>
                  <tr>
                    <th>Symbol</th>
                    <th>Direction</th>
                    <th>Volume</th>
                    <th>P&L</th>
                  </tr>
                </thead>
                <tbody>
                  @for (pos of positions; track pos.positionId) {
                    <tr>
                      <td>{{ pos.symbol }}</td>
                      <td>{{ pos.direction }}</td>
                      <td>{{ pos.volume }}</td>
                      <td [class.positive]="pos.pnL > 0" [class.negative]="pos.pnL < 0">
                        {{ pos.pnL | currency }}
                      </td>
                    </tr>
                  }
                </tbody>
              </table>
            }
          </div>
        </div>

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
  `]
})
export class DashboardComponent implements OnInit, OnDestroy {
  isConnected = false;

  account = {
    balance: 10000,
    equity: 10150,
    unrealizedPnL: 150,
    freeMargin: 9650
  };

  positions: any[] = [];
  alerts: any[] = [];

  constructor(private signalR: SignalRService) {}

  ngOnInit() {
    this.signalR.connect();

    this.signalR.connectionState$.subscribe(connected => {
      this.isConnected = connected;
    });

    this.signalR.positions$.subscribe(positions => {
      this.positions = positions;
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

  ngOnDestroy() {
    this.signalR.disconnect();
  }
}
