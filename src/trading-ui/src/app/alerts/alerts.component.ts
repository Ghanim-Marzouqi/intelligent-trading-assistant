import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../environments/environment';

@Component({
  selector: 'app-alerts',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="alerts-page">
      <header class="page-header">
        <h1>Alerts</h1>
        <button class="primary" (click)="showCreateForm = true">+ New Alert</button>
      </header>

      @if (showCreateForm) {
        <div class="card form-card">
          <h3>Create Alert</h3>
          <form (ngSubmit)="createAlert()">
            <div class="form-row">
              <div class="form-group">
                <label>Symbol</label>
                <input type="text" [(ngModel)]="newAlert.symbol" name="symbol" placeholder="EURUSD" required>
              </div>
              <div class="form-group">
                <label>Name</label>
                <input type="text" [(ngModel)]="newAlert.name" name="name" placeholder="Price above 1.0900" required>
              </div>
            </div>
            <div class="form-group">
              <label>Description (optional)</label>
              <input type="text" [(ngModel)]="newAlert.description" name="description">
            </div>
            <div class="form-actions">
              <button type="button" (click)="showCreateForm = false">Cancel</button>
              <button type="submit" class="primary">Create Alert</button>
            </div>
          </form>
        </div>
      }

      <div class="card">
        <h3>Active Alerts</h3>
        @if (alerts.length === 0) {
          <p class="empty-state">No active alerts</p>
        } @else {
          <table>
            <thead>
              <tr>
                <th>Symbol</th>
                <th>Name</th>
                <th>Description</th>
                <th>Triggers</th>
                <th>Last Triggered</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              @for (alert of alerts; track alert.id) {
                <tr>
                  <td><strong>{{ alert.symbol }}</strong></td>
                  <td>{{ alert.name }}</td>
                  <td>{{ alert.description || '-' }}</td>
                  <td>{{ alert.triggerCount }}</td>
                  <td>{{ alert.lastTriggeredAt ? (alert.lastTriggeredAt | date:'short') : 'Never' }}</td>
                  <td>
                    <button class="small" (click)="toggleAlert(alert)">
                      {{ alert.isActive ? 'Disable' : 'Enable' }}
                    </button>
                    <button class="danger small" (click)="deleteAlert(alert.id)">Delete</button>
                  </td>
                </tr>
              }
            </tbody>
          </table>
        }
      </div>

      <div class="card" style="margin-top: 20px;">
        <h3>Alert History</h3>
        @if (history.length === 0) {
          <p class="empty-state">No alerts triggered yet</p>
        } @else {
          <table>
            <thead>
              <tr>
                <th>Symbol</th>
                <th>Message</th>
                <th>Price</th>
                <th>Triggered At</th>
              </tr>
            </thead>
            <tbody>
              @for (trigger of history; track trigger.id) {
                <tr>
                  <td>{{ trigger.symbol }}</td>
                  <td>{{ trigger.message }}</td>
                  <td>{{ trigger.triggerPrice }}</td>
                  <td>{{ trigger.triggeredAt | date:'medium' }}</td>
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
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-bottom: 24px;
    }

    .form-card {
      margin-bottom: 20px;
    }

    .form-card h3 {
      margin-bottom: 16px;
    }

    .form-row {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 16px;
    }

    .form-group {
      margin-bottom: 16px;
    }

    .form-group label {
      display: block;
      margin-bottom: 6px;
      font-size: 14px;
      color: var(--text-muted);
    }

    .form-group input {
      width: 100%;
    }

    .form-actions {
      display: flex;
      gap: 12px;
      justify-content: flex-end;
    }

    .card h3 {
      margin-bottom: 16px;
    }

    .empty-state {
      text-align: center;
      color: var(--text-muted);
      padding: 40px;
    }

    button.small {
      padding: 4px 8px;
      font-size: 12px;
      margin-right: 8px;
    }
  `]
})
export class AlertsComponent implements OnInit {
  alerts: any[] = [];
  history: any[] = [];
  showCreateForm = false;
  newAlert = { symbol: '', name: '', description: '' };

  constructor(private http: HttpClient) {}

  ngOnInit() {
    this.loadAlerts();
    this.loadHistory();
  }

  loadAlerts() {
    this.http.get<any[]>(`${environment.apiUrl}/api/alerts`).subscribe({
      next: (data) => this.alerts = data
    });
  }

  loadHistory() {
    this.http.get<any[]>(`${environment.apiUrl}/api/alerts/history`).subscribe({
      next: (data) => this.history = data
    });
  }

  createAlert() {
    this.http.post(`${environment.apiUrl}/api/alerts`, this.newAlert).subscribe({
      next: () => {
        this.loadAlerts();
        this.showCreateForm = false;
        this.newAlert = { symbol: '', name: '', description: '' };
      }
    });
  }

  toggleAlert(alert: any) {
    this.http.put(`${environment.apiUrl}/api/alerts/${alert.id}`, {
      isActive: !alert.isActive
    }).subscribe({
      next: () => this.loadAlerts()
    });
  }

  deleteAlert(id: number) {
    if (confirm('Are you sure you want to delete this alert?')) {
      this.http.delete(`${environment.apiUrl}/api/alerts/${id}`).subscribe({
        next: () => this.loadAlerts()
      });
    }
  }
}
