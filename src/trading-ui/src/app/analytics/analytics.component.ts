import { Component, OnInit, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { BaseChartDirective } from 'ng2-charts';
import { ChartConfiguration } from 'chart.js';
import { environment } from '../../environments/environment';

@Component({
  selector: 'app-analytics',
  standalone: true,
  imports: [CommonModule, BaseChartDirective],
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

      <div class="card" style="margin-bottom: 20px;">
        <h3>Equity Curve</h3>
        @if (equityChartData.datasets[0].data.length) {
          <canvas baseChart
            [data]="equityChartData"
            [options]="equityChartOptions"
            type="line">
          </canvas>
        } @else {
          <p class="empty-state">No equity data available</p>
        }
      </div>

      <div class="grid-2">
        <div class="card">
          <h3>Performance by Pair</h3>
          @if (overview.pairPerformance?.length) {
            <div class="table-scroll table-scroll--no-cards">
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
            </div>
          } @else {
            <p class="empty-state">No data available</p>
          }
        </div>

        <div class="card">
          <h3>Performance by Day of Week</h3>
          @if (dayChartData.labels?.length) {
            <canvas baseChart
              [data]="dayChartData"
              [options]="barChartOptions"
              type="bar">
            </canvas>
          } @else {
            <p class="empty-state">No data available</p>
          }
        </div>
      </div>

      <div class="card" style="margin-top: 20px;">
        <h3>Performance by Hour</h3>
        @if (hourChartData.labels?.length) {
          <canvas baseChart
            [data]="hourChartData"
            [options]="barChartOptions"
            type="bar">
          </canvas>
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

    @media (max-width: 1024px) {
      .stats-grid {
        grid-template-columns: repeat(3, 1fr);
      }
    }

    @media (max-width: 768px) {
      .stats-grid {
        grid-template-columns: repeat(2, 1fr);
      }

      .grid-2 {
        grid-template-columns: 1fr;
      }

      .stat-value {
        font-size: 18px;
      }
    }

    @media (max-width: 480px) {
      .stat-value {
        font-size: 16px;
      }
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

  private borderColor = '#333';
  private textMuted = '#888';

  equityChartData: ChartConfiguration<'line'>['data'] = {
    labels: [],
    datasets: [{
      data: [],
      borderColor: '#22c55e',
      backgroundColor: 'rgba(34, 197, 94, 0.05)',
      tension: 0.3,
      fill: false,
      pointRadius: 0,
      borderWidth: 2
    }]
  };

  equityChartOptions: ChartConfiguration<'line'>['options'] = {
    responsive: true,
    plugins: { legend: { display: false } },
    scales: {
      x: {
        grid: { color: this.borderColor },
        ticks: { color: this.textMuted, maxTicksLimit: 10 }
      },
      y: {
        grid: { color: this.borderColor },
        ticks: { color: this.textMuted }
      }
    }
  };

  dayChartData: ChartConfiguration<'bar'>['data'] = {
    labels: [],
    datasets: [{
      data: [],
      backgroundColor: [],
      borderWidth: 0
    }]
  };

  hourChartData: ChartConfiguration<'bar'>['data'] = {
    labels: [],
    datasets: [{
      data: [],
      backgroundColor: [],
      borderWidth: 0
    }]
  };

  barChartOptions: ChartConfiguration<'bar'>['options'] = {
    responsive: true,
    plugins: { legend: { display: false } },
    scales: {
      x: {
        grid: { color: this.borderColor },
        ticks: { color: this.textMuted }
      },
      y: {
        grid: { color: this.borderColor },
        ticks: { color: this.textMuted }
      }
    }
  };

  constructor(private http: HttpClient) {}

  ngOnInit() {
    if (typeof document !== 'undefined') {
      const styles = getComputedStyle(document.documentElement);
      this.borderColor = styles.getPropertyValue('--border').trim() || '#333';
      this.textMuted = styles.getPropertyValue('--text-muted').trim() || '#888';
      
      // Update chart options colors
      const eqScales = (this.equityChartOptions as any)?.scales;
      if (eqScales) {
        eqScales.x.grid.color = this.borderColor;
        eqScales.x.ticks.color = this.textMuted;
        eqScales.y.grid.color = this.borderColor;
        eqScales.y.ticks.color = this.textMuted;
      }
      const barScales = (this.barChartOptions as any)?.scales;
      if (barScales) {
        barScales.x.grid.color = this.borderColor;
        barScales.x.ticks.color = this.textMuted;
        barScales.y.grid.color = this.borderColor;
        barScales.y.ticks.color = this.textMuted;
      }
    }

    this.loadOverview();
    this.loadEquityCurve();
    this.loadDayOfWeekStats();
    this.loadHourlyStats();
  }

  loadOverview() {
    this.http.get<any>(`${environment.apiUrl}/api/analytics/overview`).subscribe({
      next: (data) => this.overview = data
    });
  }

  loadEquityCurve() {
    this.http.get<any[]>(`${environment.apiUrl}/api/analytics/equity-curve`).subscribe({
      next: (data) => {
        this.equityChartData = {
          labels: data.map(d => new Date(d.timestamp).toLocaleDateString()),
          datasets: [{
            data: data.map(d => d.equity),
            borderColor: '#22c55e',
            backgroundColor: 'rgba(34, 197, 94, 0.05)',
            tension: 0.3,
            fill: false,
            pointRadius: 0,
            borderWidth: 2
          }]
        };
      }
    });
  }

  loadDayOfWeekStats() {
    this.http.get<any[]>(`${environment.apiUrl}/api/analytics/by-day-of-week`).subscribe({
      next: (data) => {
        this.dayChartData = {
          labels: data.map(d => d.day),
          datasets: [{
            data: data.map(d => d.totalPnL),
            backgroundColor: data.map(d => d.totalPnL >= 0 ? '#22c55e' : '#ef4444'),
            borderWidth: 0
          }]
        };
      }
    });
  }

  loadHourlyStats() {
    this.http.get<any[]>(`${environment.apiUrl}/api/analytics/by-hour`).subscribe({
      next: (data) => {
        this.hourChartData = {
          labels: data.map(h => `${h.hour}:00`),
          datasets: [{
            data: data.map(h => h.totalPnL),
            backgroundColor: data.map(h => h.totalPnL >= 0 ? '#22c55e' : '#ef4444'),
            borderWidth: 0
          }]
        };
      }
    });
  }
}
