import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../environments/environment';

@Component({
  selector: 'app-positions',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="positions-page">
      <header class="page-header">
        <h1>Positions</h1>
      </header>

      <div class="card">
        <h3>Open Positions</h3>
        @if (loading) {
          <p class="loading">Loading...</p>
        } @else if (positions.length === 0) {
          <p class="empty-state">No open positions</p>
        } @else {
          <table>
            <thead>
              <tr>
                <th>Symbol</th>
                <th>Direction</th>
                <th>Volume</th>
                <th>Entry Price</th>
                <th>Current Price</th>
                <th>SL</th>
                <th>TP</th>
                <th>P&L</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              @for (pos of positions; track pos.id) {
                <tr>
                  <td><strong>{{ pos.symbol }}</strong></td>
                  <td [class.buy]="pos.direction === 'Buy'" [class.sell]="pos.direction === 'Sell'">
                    {{ pos.direction }}
                  </td>
                  <td>{{ pos.volume }}</td>
                  <td>{{ pos.entryPrice }}</td>
                  <td>{{ pos.currentPrice }}</td>
                  <td>{{ pos.stopLoss || '-' }}</td>
                  <td>{{ pos.takeProfit || '-' }}</td>
                  <td [class.positive]="pos.unrealizedPnL > 0" [class.negative]="pos.unrealizedPnL < 0">
                    {{ pos.unrealizedPnL | currency }}
                  </td>
                  <td>
                    <button class="danger small" (click)="closePosition(pos.id)">Close</button>
                  </td>
                </tr>
              }
            </tbody>
          </table>
        }
      </div>

      <div class="card" style="margin-top: 20px;">
        <h3>Position History</h3>
        @if (history.length === 0) {
          <p class="empty-state">No closed positions</p>
        } @else {
          <table>
            <thead>
              <tr>
                <th>Symbol</th>
                <th>Direction</th>
                <th>Volume</th>
                <th>Entry</th>
                <th>Exit</th>
                <th>P&L</th>
                <th>Closed At</th>
              </tr>
            </thead>
            <tbody>
              @for (pos of history; track pos.id) {
                <tr>
                  <td>{{ pos.symbol }}</td>
                  <td>{{ pos.direction }}</td>
                  <td>{{ pos.volume }}</td>
                  <td>{{ pos.entryPrice }}</td>
                  <td>{{ pos.closePrice }}</td>
                  <td [class.positive]="pos.realizedPnL > 0" [class.negative]="pos.realizedPnL < 0">
                    {{ pos.realizedPnL | currency }}
                  </td>
                  <td>{{ pos.closeTime | date:'short' }}</td>
                </tr>
              }
            </tbody>
          </table>
        }
      </div>
    </div>
  `,
  styles: [`
    .page-header {
      margin-bottom: 24px;
    }

    .loading, .empty-state {
      text-align: center;
      color: var(--text-muted);
      padding: 40px;
    }

    .card h3 {
      margin-bottom: 16px;
    }

    .buy { color: var(--success); }
    .sell { color: var(--danger); }

    button.small {
      padding: 4px 8px;
      font-size: 12px;
    }
  `]
})
export class PositionsComponent implements OnInit {
  positions: any[] = [];
  history: any[] = [];
  loading = true;

  constructor(private http: HttpClient) {}

  ngOnInit() {
    this.loadPositions();
    this.loadHistory();
  }

  loadPositions() {
    this.http.get<any[]>(`${environment.apiUrl}/api/positions`).subscribe({
      next: (data) => {
        this.positions = data;
        this.loading = false;
      },
      error: () => {
        this.loading = false;
      }
    });
  }

  loadHistory() {
    this.http.get<any[]>(`${environment.apiUrl}/api/positions/history`).subscribe({
      next: (data) => {
        this.history = data;
      }
    });
  }

  closePosition(id: number) {
    if (confirm('Are you sure you want to close this position?')) {
      this.http.post(`${environment.apiUrl}/api/positions/${id}/close`, {}).subscribe({
        next: () => {
          this.loadPositions();
        }
      });
    }
  }
}
