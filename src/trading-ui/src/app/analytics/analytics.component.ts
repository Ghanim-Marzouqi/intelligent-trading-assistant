import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../environments/environment';

@Component({
  selector: 'app-analytics',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="analytics-page">
      <header class="page-header">
        <h1>Analytics</h1>
      </header>

      <div class="stats-grid">
        <div class="card stat-card">
          <div class="stat-label">Total Trades</div>
          <div class="stat-value">{{ overview.totalTrades }}</div>
        </div>
        <div class="card stat-card">
          <div class="stat-label">Win Rate</div>
          <div class="stat-value">{{ overview.winRate | number:'1.1-1' }}%</div>
        </div>
        <div class="card stat-card">
          <div class="stat-label">Avg Win</div>
          <div class="stat-value positive">{{ overview.averageWin | currency }}</div>
        </div>
        <div class="card stat-card">
          <div class="stat-label">Avg Loss</div>
          <div class="stat-value negative">{{ overview.averageLoss | currency }}</div>
        </div>
        <div class="card stat-card">
          <div class="stat-label">Profit Factor</div>
          <div class="stat-value">{{ overview.profitFactor | number:'1.2-2' }}</div>
        </div>
        <div class="card stat-card">
          <div class="stat-label">Largest Win</div>
          <div class="stat-value positive">{{ overview.largestWin | currency }}</div>
        </div>
      </div>

      <div class="grid-2">
        <div class="card">
          <h3>Performance by Pair</h3>
          @if (overview.pairPerformance?.length) {
            <table>
              <thead>
                <tr>
                  <th>Pair</th>
                  <th>Trades</th>
                  <th>Win Rate</th>
                  <th>P&L</th>
                </tr>
              </thead>
              <tbody>
                @for (pair of overview.pairPerformance; track pair.symbol) {
                  <tr>
                    <td><strong>{{ pair.symbol }}</strong></td>
                    <td>{{ pair.trades }}</td>
                    <td>{{ pair.winRate | number:'1.0-0' }}%</td>
                    <td [class.positive]="pair.totalPnL > 0" [class.negative]="pair.totalPnL < 0">
                      {{ pair.totalPnL | currency }}
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          } @else {
            <p class="empty-state">No data available</p>
          }
        </div>

        <div class="card">
          <h3>Performance by Day of Week</h3>
          @if (dayOfWeekStats.length) {
            <table>
              <thead>
                <tr>
                  <th>Day</th>
                  <th>Trades</th>
                  <th>Win Rate</th>
                  <th>P&L</th>
                </tr>
              </thead>
              <tbody>
                @for (day of dayOfWeekStats; track day.day) {
                  <tr>
                    <td>{{ day.day }}</td>
                    <td>{{ day.trades }}</td>
                    <td>{{ day.winRate | number:'1.0-0' }}%</td>
                    <td [class.positive]="day.totalPnL > 0" [class.negative]="day.totalPnL < 0">
                      {{ day.totalPnL | currency }}
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          } @else {
            <p class="empty-state">No data available</p>
          }
        </div>
      </div>

      <div class="card" style="margin-top: 20px;">
        <h3>Performance by Hour</h3>
        @if (hourlyStats.length) {
          <div class="hour-chart">
            @for (hour of hourlyStats; track hour.hour) {
              <div class="hour-bar" [title]="hour.hour + ':00 - ' + (hour.totalPnL | currency)">
                <div
                  class="bar"
                  [class.positive]="hour.totalPnL > 0"
                  [class.negative]="hour.totalPnL < 0"
                  [style.height.%]="getBarHeight(hour.totalPnL)"
                ></div>
                <span class="hour-label">{{ hour.hour }}</span>
              </div>
            }
          </div>
        } @else {
          <p class="empty-state">No data available</p>
        }
      </div>
    </div>
  `,
  styles: [`
    .page-header {
      margin-bottom: 24px;
    }

    .stats-grid {
      display: grid;
      grid-template-columns: repeat(6, 1fr);
      gap: 16px;
      margin-bottom: 24px;
    }

    .stat-card {
      text-align: center;
    }

    .stat-label {
      color: var(--text-muted);
      font-size: 12px;
      margin-bottom: 8px;
    }

    .stat-value {
      font-size: 20px;
      font-weight: 600;
    }

    .grid-2 {
      display: grid;
      grid-template-columns: repeat(2, 1fr);
      gap: 16px;
    }

    .card h3 {
      margin-bottom: 16px;
    }

    .empty-state {
      text-align: center;
      color: var(--text-muted);
      padding: 40px;
    }

    .hour-chart {
      display: flex;
      align-items: flex-end;
      height: 200px;
      gap: 4px;
      padding-top: 20px;
    }

    .hour-bar {
      flex: 1;
      display: flex;
      flex-direction: column;
      align-items: center;
      height: 100%;
      position: relative;
    }

    .bar {
      width: 100%;
      background: var(--surface-light);
      border-radius: 2px 2px 0 0;
      position: absolute;
      bottom: 20px;

      &.positive {
        background: var(--success);
      }

      &.negative {
        background: var(--danger);
      }
    }

    .hour-label {
      position: absolute;
      bottom: 0;
      font-size: 10px;
      color: var(--text-muted);
    }
  `]
})
export class AnalyticsComponent implements OnInit {
  overview: any = {
    totalTrades: 0,
    winRate: 0,
    averageWin: 0,
    averageLoss: 0,
    profitFactor: 0,
    largestWin: 0,
    pairPerformance: []
  };

  dayOfWeekStats: any[] = [];
  hourlyStats: any[] = [];
  maxPnL = 0;

  constructor(private http: HttpClient) {}

  ngOnInit() {
    this.loadOverview();
    this.loadDayOfWeekStats();
    this.loadHourlyStats();
  }

  loadOverview() {
    this.http.get<any>(`${environment.apiUrl}/api/analytics/overview`).subscribe({
      next: (data) => this.overview = data
    });
  }

  loadDayOfWeekStats() {
    this.http.get<any[]>(`${environment.apiUrl}/api/analytics/by-day-of-week`).subscribe({
      next: (data) => this.dayOfWeekStats = data
    });
  }

  loadHourlyStats() {
    this.http.get<any[]>(`${environment.apiUrl}/api/analytics/by-hour`).subscribe({
      next: (data) => {
        this.hourlyStats = data;
        this.maxPnL = Math.max(...data.map(h => Math.abs(h.totalPnL)), 1);
      }
    });
  }

  getBarHeight(pnL: number): number {
    return Math.abs(pnL) / this.maxPnL * 80;
  }
}
