import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../environments/environment';

@Component({
  selector: 'app-journal',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="journal-page">
      <header class="page-header">
        <h1>Trade Journal</h1>
      </header>

      <div class="stats-grid">
        <div class="card stat-card">
          <div class="stat-label">Total Trades</div>
          <div class="stat-value">{{ stats.totalTrades }}</div>
        </div>
        <div class="card stat-card">
          <div class="stat-label">Win Rate</div>
          <div class="stat-value">{{ stats.winRate | number:'1.1-1' }}%</div>
        </div>
        <div class="card stat-card">
          <div class="stat-label">Profit Factor</div>
          <div class="stat-value">{{ stats.profitFactor | number:'1.2-2' }}</div>
        </div>
        <div class="card stat-card">
          <div class="stat-label">Total P&L</div>
          <div class="stat-value" [class.positive]="stats.totalPnL > 0" [class.negative]="stats.totalPnL < 0">
            {{ stats.totalPnL | currency }}
          </div>
        </div>
      </div>

      <div class="card">
        <h3>Recent Trades</h3>
        @if (trades.length === 0) {
          <p class="empty-state">No trades recorded yet</p>
        } @else {
          <table>
            <thead>
              <tr>
                <th>Date</th>
                <th>Symbol</th>
                <th>Direction</th>
                <th>Volume</th>
                <th>Entry</th>
                <th>Exit</th>
                <th>Pips</th>
                <th>R:R</th>
                <th>P&L</th>
                <th>Tags</th>
              </tr>
            </thead>
            <tbody>
              @for (trade of trades; track trade.id) {
                <tr (click)="selectTrade(trade)" class="clickable">
                  <td>{{ trade.closeTime | date:'shortDate' }}</td>
                  <td><strong>{{ trade.symbol }}</strong></td>
                  <td [class.buy]="trade.direction === 'Buy'" [class.sell]="trade.direction === 'Sell'">
                    {{ trade.direction }}
                  </td>
                  <td>{{ trade.volume }}</td>
                  <td>{{ trade.entryPrice }}</td>
                  <td>{{ trade.exitPrice }}</td>
                  <td [class.positive]="trade.pnLPips > 0" [class.negative]="trade.pnLPips < 0">
                    {{ trade.pnLPips | number:'1.1-1' }}
                  </td>
                  <td>{{ trade.riskRewardRatio ? (trade.riskRewardRatio | number:'1.2-2') : '-' }}</td>
                  <td [class.positive]="trade.netPnL > 0" [class.negative]="trade.netPnL < 0">
                    {{ trade.netPnL | currency }}
                  </td>
                  <td>
                    @for (tag of trade.tags; track tag.id) {
                      <span class="tag">{{ tag.name }}</span>
                    }
                  </td>
                </tr>
              }
            </tbody>
          </table>
        }
      </div>

      @if (selectedTrade) {
        <div class="card trade-detail" style="margin-top: 20px;">
          <h3>Trade Details</h3>
          <div class="detail-grid">
            <div class="detail-item">
              <span class="label">Symbol</span>
              <span class="value">{{ selectedTrade.symbol }}</span>
            </div>
            <div class="detail-item">
              <span class="label">Duration</span>
              <span class="value">{{ formatDuration(selectedTrade.duration) }}</span>
            </div>
            <div class="detail-item">
              <span class="label">Commission</span>
              <span class="value">{{ selectedTrade.commission | currency }}</span>
            </div>
            <div class="detail-item">
              <span class="label">Swap</span>
              <span class="value">{{ selectedTrade.swap | currency }}</span>
            </div>
          </div>

          <h4>Notes</h4>
          @if (selectedTrade.notes?.length) {
            @for (note of selectedTrade.notes; track note.id) {
              <div class="note">{{ note.content }}</div>
            }
          } @else {
            <p class="empty-state small">No notes</p>
          }
        </div>
      }
    </div>
  `,
  styles: [`
    .page-header {
      margin-bottom: 24px;
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

    .card h3 {
      margin-bottom: 16px;
    }

    .empty-state {
      text-align: center;
      color: var(--text-muted);
      padding: 40px;

      &.small {
        padding: 20px;
      }
    }

    .clickable {
      cursor: pointer;
      &:hover {
        background: var(--surface-light);
      }
    }

    .buy { color: var(--success); }
    .sell { color: var(--danger); }

    .tag {
      background: var(--surface-light);
      padding: 2px 8px;
      border-radius: 12px;
      font-size: 12px;
      margin-right: 4px;
    }

    .detail-grid {
      display: grid;
      grid-template-columns: repeat(4, 1fr);
      gap: 16px;
      margin-bottom: 20px;
    }

    .detail-item {
      .label {
        display: block;
        color: var(--text-muted);
        font-size: 12px;
        margin-bottom: 4px;
      }
      .value {
        font-weight: 500;
      }
    }

    h4 {
      margin: 16px 0 8px;
      font-size: 14px;
    }

    .note {
      background: var(--surface-light);
      padding: 12px;
      border-radius: 6px;
      margin-bottom: 8px;
    }
  `]
})
export class JournalComponent implements OnInit {
  trades: any[] = [];
  selectedTrade: any = null;
  stats = {
    totalTrades: 0,
    winRate: 0,
    profitFactor: 0,
    totalPnL: 0
  };

  constructor(private http: HttpClient) {}

  ngOnInit() {
    this.loadTrades();
    this.loadStats();
  }

  loadTrades() {
    this.http.get<any[]>(`${environment.apiUrl}/api/journal`).subscribe({
      next: (data) => this.trades = data
    });
  }

  loadStats() {
    this.http.get<any>(`${environment.apiUrl}/api/analytics/overview`).subscribe({
      next: (data) => {
        this.stats = {
          totalTrades: data.totalTrades,
          winRate: data.winRate,
          profitFactor: data.profitFactor,
          totalPnL: data.totalPnL
        };
      }
    });
  }

  selectTrade(trade: any) {
    this.http.get<any>(`${environment.apiUrl}/api/journal/${trade.id}`).subscribe({
      next: (data) => this.selectedTrade = data
    });
  }

  formatDuration(duration: string): string {
    // Parse ISO 8601 duration or time format
    if (!duration) return '-';
    return duration;
  }
}
