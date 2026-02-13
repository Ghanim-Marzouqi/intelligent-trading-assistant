import { Component, OnInit, HostListener, ElementRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../environments/environment';
import { ConfirmDialogService } from '../shared/services/confirm-dialog.service';

interface SymbolInfo {
  name: string;
  category: string;
  description: string;
}

interface AlertCondition {
  type: number;
  operator: number;
  value: number;
  combineWith: number | null;
}

const conditionTypeLabels: Record<number, string> = {
  0: 'Price Level',
  1: 'Price Change',
  2: 'Indicator Value',
  3: 'Indicator Crossover',
  4: 'Time'
};

const operatorLabels: Record<number, string> = {
  0: '>',
  1: '<',
  2: '>=',
  3: '<=',
  4: 'Crosses Above',
  5: 'Crosses Below',
  6: '='
};

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
              <div class="form-group symbol-autocomplete">
                <label>Symbol</label>
                <div class="symbol-input-wrap">
                  <input
                    type="text"
                    class="symbol-input"
                    [value]="newAlert.symbol || symbolSearch"
                    (input)="onSymbolSearchInput($event)"
                    (focus)="openSymbolDropdown()"
                    (keydown)="onSymbolKeydown($event)"
                    [placeholder]="newAlert.symbol ? '' : 'Search symbol...'"
                    autocomplete="off" />
                  @if (newAlert.symbol) {
                    <button class="symbol-clear" type="button" (mousedown)="$event.preventDefault()" (click)="clearSymbol()">&times;</button>
                  }
                </div>
                @if (showSymbolDropdown) {
                  <div class="symbol-dropdown">
                    <div class="category-tabs">
                      @for (cat of symbolCategories; track cat) {
                        <button
                          type="button"
                          class="cat-tab"
                          [class.active]="activeCategory === cat"
                          (mousedown)="$event.preventDefault()"
                          (click)="setCategory(cat)">{{ cat }}</button>
                      }
                    </div>
                    <div class="symbol-list">
                      @for (group of groupedFilteredSymbols; track group.category) {
                        @if (activeCategory === 'All') {
                          <div class="symbol-group-header">{{ group.category }}</div>
                        }
                        @for (s of group.symbols; track s.name) {
                          <div
                            class="symbol-option"
                            (mousedown)="$event.preventDefault()"
                            (click)="selectSymbol(s)">
                            <span class="symbol-name">{{ s.name }}</span>
                            <span class="symbol-desc">{{ s.description }}</span>
                          </div>
                        }
                      }
                      @if (groupedFilteredSymbols.length === 0) {
                        <div class="symbol-no-match">No symbols found</div>
                      }
                    </div>
                  </div>
                }
              </div>
              <div class="form-group">
                <label>Name</label>
                <input type="text" [(ngModel)]="newAlert.name" name="name" placeholder="Price above 1.0900" required>
              </div>
            </div>
            <div class="form-row">
              <div class="form-group">
                <label>Description (optional)</label>
                <input type="text" [(ngModel)]="newAlert.description" name="description">
              </div>
              <div class="form-group">
                <label>Type</label>
                <select [(ngModel)]="newAlert.type" name="type">
                  <option [ngValue]="0">Price</option>
                  <option [ngValue]="1">Indicator</option>
                  <option [ngValue]="2">Composite</option>
                  <option [ngValue]="3">Time Based</option>
                </select>
              </div>
            </div>

            <!-- Conditions Builder -->
            <div class="conditions-section">
              <div class="section-header">
                <label>Conditions</label>
                <button type="button" class="small" (click)="addCondition()">+ Add Condition</button>
              </div>
              @for (cond of newAlert.conditions; track $index; let i = $index) {
                @if (i > 0) {
                  <div class="combiner-row">
                    <select [(ngModel)]="newAlert.conditions[i].combineWith" [name]="'combineWith' + i">
                      <option [ngValue]="0">AND</option>
                      <option [ngValue]="1">OR</option>
                    </select>
                  </div>
                }
                <div class="condition-row">
                  <select [(ngModel)]="cond.type" [name]="'condType' + i" class="cond-type">
                    <option [ngValue]="0">Price Level</option>
                    <option [ngValue]="1">Price Change</option>
                    <option [ngValue]="2">Indicator Value</option>
                    <option [ngValue]="3">Indicator Crossover</option>
                    <option [ngValue]="4">Time</option>
                  </select>
                  <select [(ngModel)]="cond.operator" [name]="'condOp' + i" class="cond-op">
                    <option [ngValue]="0">></option>
                    <option [ngValue]="1"><</option>
                    <option [ngValue]="2">>=</option>
                    <option [ngValue]="3"><=</option>
                    <option [ngValue]="4">Crosses Above</option>
                    <option [ngValue]="5">Crosses Below</option>
                  </select>
                  <input type="number" [(ngModel)]="cond.value" [name]="'condVal' + i" step="any" placeholder="Value" class="cond-value">
                  <button type="button" class="danger small cond-remove" (click)="removeCondition(i)">&times;</button>
                </div>
              }
              @if (newAlert.conditions.length === 0) {
                <p class="text-muted conditions-hint">No conditions added. Alert will be manually managed.</p>
              }
            </div>

            <!-- Options Section -->
            <div class="options-section">
              <label class="section-label">Options</label>
              <div class="options-grid">
                <label class="toggle-label">
                  <input type="checkbox" [(ngModel)]="newAlert.autoPrepareOrder" name="autoPrepareOrder">
                  <span>Auto-Prepare Order</span>
                  <span class="hint">Automatically prepare an order when alert triggers</span>
                </label>
                <label class="toggle-label">
                  <input type="checkbox" [(ngModel)]="newAlert.aiEnrichEnabled" name="aiEnrichEnabled">
                  <span>AI Enrichment</span>
                  <span class="hint">Run AI analysis when alert triggers</span>
                </label>
                <label class="toggle-label">
                  <input type="checkbox" [(ngModel)]="newAlert.notifyTelegram" name="notifyTelegram">
                  <span>Notify Telegram</span>
                </label>
                <label class="toggle-label">
                  <input type="checkbox" [(ngModel)]="newAlert.notifyDashboard" name="notifyDashboard">
                  <span>Notify Dashboard</span>
                </label>
              </div>
              <div class="form-group max-triggers-group">
                <label>Max Triggers <span class="optional">(empty = unlimited)</span></label>
                <input type="number" [(ngModel)]="newAlert.maxTriggers" name="maxTriggers" min="1" placeholder="Unlimited">
              </div>
            </div>

            <div class="form-actions">
              <button type="button" (click)="cancelCreate()">Cancel</button>
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
          <div class="table-scroll">
            <table>
              <thead>
                <tr>
                  <th>Symbol</th>
                  <th>Name</th>
                  <th>Conditions</th>
                  <th>Options</th>
                  <th>Triggers</th>
                  <th>Last Triggered</th>
                  <th>Actions</th>
                </tr>
              </thead>
              <tbody>
                @for (alert of alerts; track alert.id) {
                  <tr>
                    <td data-label="Symbol"><strong>{{ alert.symbol }}</strong></td>
                    <td data-label="Name">
                      {{ alert.name }}
                      @if (alert.description) {
                        <br><span class="text-muted text-sm">{{ alert.description }}</span>
                      }
                    </td>
                    <td data-label="Conditions">
                      @if (alert.conditions && alert.conditions.length > 0) {
                        <span class="badge">{{ alert.conditions.length }} rule(s)</span>
                        <div class="conditions-preview">
                          @for (c of alert.conditions; track $index; let i = $index) {
                            @if (i > 0) {
                              <span class="combiner-text">{{ c.combineWith === 1 ? 'OR' : 'AND' }}</span>
                            }
                            <span class="condition-text">{{ conditionTypeLabel(c.type) }} {{ operatorLabel(c.operator) }} {{ c.value }}</span>
                          }
                        </div>
                      } @else {
                        <span class="text-muted">-</span>
                      }
                    </td>
                    <td data-label="Options">
                      <div class="option-badges">
                        @if (alert.autoPrepareOrder) {
                          <span class="badge badge-auto">Auto-Order</span>
                        }
                        @if (alert.aiEnrichEnabled) {
                          <span class="badge badge-ai">AI</span>
                        }
                        @if (alert.maxTriggers) {
                          <span class="badge badge-max">Max: {{ alert.maxTriggers }}</span>
                        }
                      </div>
                    </td>
                    <td data-label="Triggers">{{ alert.triggerCount }}</td>
                    <td data-label="Last Triggered">{{ alert.lastTriggeredAt ? (alert.lastTriggeredAt | date:'short') : 'Never' }}</td>
                    <td data-label="Actions">
                      <div class="card-actions">
                        <button class="small" (click)="toggleAlert(alert)">
                          {{ alert.isActive ? 'Disable' : 'Enable' }}
                        </button>
                        <button class="danger small" (click)="deleteAlert(alert.id)">Delete</button>
                      </div>
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
      </div>

      <div class="card" style="margin-top: 20px;">
        <h3>Alert History</h3>
        @if (history.length === 0) {
          <p class="empty-state">No alerts triggered yet</p>
        } @else {
          <div class="table-scroll table-scroll--no-cards">
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
          </div>
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

    .form-group input, .form-group select {
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

    .text-muted {
      color: var(--text-muted);
    }

    .text-sm {
      font-size: 12px;
    }

    .optional {
      font-weight: 400;
      font-size: 12px;
    }

    /* Conditions Section */
    .conditions-section {
      margin-bottom: 20px;
      padding: 16px;
      background: var(--surface-light);
      border-radius: var(--radius);
      border: 1px solid var(--border);
    }

    .section-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-bottom: 12px;
    }

    .section-header label {
      font-size: 14px;
      font-weight: 600;
      color: var(--text);
    }

    .condition-row {
      display: flex;
      gap: 8px;
      align-items: center;
      margin-bottom: 8px;
    }

    .cond-type {
      width: 160px;
    }

    .cond-op {
      width: 140px;
    }

    .cond-value {
      width: 120px;
    }

    .cond-remove {
      flex-shrink: 0;
      padding: 4px 8px;
    }

    .combiner-row {
      display: flex;
      justify-content: center;
      margin: 4px 0;
    }

    .combiner-row select {
      width: 80px;
      text-align: center;
      font-size: 12px;
      font-weight: 600;
    }

    .conditions-hint {
      font-size: 13px;
      margin: 0;
    }

    /* Options Section */
    .options-section {
      margin-bottom: 20px;
      padding: 16px;
      background: var(--surface-light);
      border-radius: var(--radius);
      border: 1px solid var(--border);
    }

    .section-label {
      display: block;
      font-size: 14px;
      font-weight: 600;
      color: var(--text);
      margin-bottom: 12px;
    }

    .options-grid {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 12px;
      margin-bottom: 16px;
    }

    .toggle-label {
      display: flex;
      align-items: center;
      gap: 8px;
      font-size: 14px;
      cursor: pointer;
      flex-wrap: wrap;
    }

    .toggle-label input[type="checkbox"] {
      width: auto;
      margin: 0;
    }

    .toggle-label .hint {
      width: 100%;
      font-size: 11px;
      color: var(--text-muted);
      padding-left: 24px;
    }

    .max-triggers-group {
      max-width: 200px;
    }

    .max-triggers-group label {
      font-size: 14px;
    }

    /* Badges */
    .badge {
      display: inline-block;
      padding: 2px 8px;
      border-radius: 10px;
      font-size: 11px;
      font-weight: 600;
      background: var(--surface-light);
      border: 1px solid var(--border);
      color: var(--text-muted);
    }

    .badge-auto {
      background: rgba(var(--primary-rgb, 59, 130, 246), 0.15);
      border-color: var(--primary);
      color: var(--primary);
    }

    .badge-ai {
      background: rgba(168, 85, 247, 0.15);
      border-color: #a855f7;
      color: #a855f7;
    }

    .badge-max {
      background: rgba(234, 179, 8, 0.15);
      border-color: #eab308;
      color: #eab308;
    }

    .option-badges {
      display: flex;
      gap: 4px;
      flex-wrap: wrap;
    }

    .conditions-preview {
      margin-top: 4px;
      font-size: 11px;
      color: var(--text-muted);
      line-height: 1.6;
    }

    .combiner-text {
      font-weight: 600;
      padding: 0 4px;
      color: var(--primary);
    }

    .condition-text {
      font-family: var(--font-mono);
    }

    /* Symbol Autocomplete */
    .symbol-autocomplete { position: relative; }
    .symbol-input-wrap { position: relative; display: flex; align-items: center; }
    .symbol-input { width: 100%; padding-right: 32px; }
    .symbol-clear {
      position: absolute; right: 8px; top: 50%; transform: translateY(-50%);
      background: none; border: none; color: var(--text-muted); font-size: 18px;
      cursor: pointer; padding: 0 4px; line-height: 1;
    }
    .symbol-clear:hover { color: var(--text); }
    .symbol-dropdown {
      position: absolute; top: 100%; left: 0; right: 0; z-index: 100;
      background: var(--surface); border: 1px solid var(--border);
      border-radius: var(--radius); margin-top: 4px;
      box-shadow: 0 8px 24px rgba(0,0,0,0.4); overflow: hidden;
    }
    .category-tabs {
      display: flex; gap: 0; border-bottom: 1px solid var(--border);
      overflow-x: auto; scrollbar-width: none;
    }
    .category-tabs::-webkit-scrollbar { display: none; }
    .cat-tab {
      flex: 0 0 auto; padding: 8px 12px; font-size: 11px; font-weight: 600;
      text-transform: uppercase; letter-spacing: 0.04em; background: none;
      color: var(--text-muted); border: none; border-bottom: 2px solid transparent;
      border-radius: 0; cursor: pointer; white-space: nowrap;
    }
    .cat-tab:hover { color: var(--text); }
    .cat-tab.active { color: var(--primary); border-bottom-color: var(--primary); }
    .symbol-list { max-height: 260px; overflow-y: auto; }
    .symbol-group-header {
      padding: 6px 12px; font-size: 10px; font-weight: 700;
      text-transform: uppercase; letter-spacing: 0.06em; color: var(--text-muted);
      background: var(--surface-light); position: sticky; top: 0; z-index: 1;
    }
    .symbol-option {
      display: flex; align-items: center; gap: 10px; padding: 8px 12px;
      cursor: pointer; transition: background 0.1s;
    }
    .symbol-option:hover { background: var(--surface-light); }
    .symbol-name { font-weight: 600; font-size: 13px; min-width: 90px; font-family: var(--font-mono); }
    .symbol-desc { font-size: 12px; color: var(--text-muted); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .symbol-no-match { padding: 20px 12px; text-align: center; color: var(--text-muted); font-size: 13px; }

    @media (max-width: 768px) {
      .form-row {
        grid-template-columns: 1fr;
      }

      .condition-row {
        flex-wrap: wrap;
      }

      .cond-type, .cond-op, .cond-value {
        width: 100%;
        flex: 1;
        min-width: 0;
      }

      .options-grid {
        grid-template-columns: 1fr;
      }

      .form-actions {
        flex-direction: column;
      }

      .form-actions button {
        width: 100%;
      }

      button.small {
        margin-right: 0;
      }
    }

    @media (max-width: 480px) {
      .symbol-dropdown {
        position: fixed;
        bottom: 0;
        left: 0;
        right: 0;
        top: auto;
        max-height: 60vh;
        margin-top: 0;
        border-radius: 12px 12px 0 0;
        z-index: 200;
      }

      .symbol-list {
        max-height: 50vh;
      }
    }
  `]
})
export class AlertsComponent implements OnInit {
  alerts: any[] = [];
  history: any[] = [];
  showCreateForm = false;
  newAlert = this.freshAlert();

  // Symbol autocomplete
  symbols: SymbolInfo[] = [];
  symbolSearch = '';
  showSymbolDropdown = false;
  activeCategory = 'All';

  constructor(private http: HttpClient, private elRef: ElementRef, private dialog: ConfirmDialogService) {}

  ngOnInit() {
    this.loadAlerts();
    this.loadHistory();
  }

  freshAlert() {
    return {
      symbol: '',
      name: '',
      description: '',
      type: 0,
      autoPrepareOrder: false,
      aiEnrichEnabled: true,
      notifyTelegram: true,
      notifyDashboard: true,
      maxTriggers: null as number | null,
      conditions: [] as AlertCondition[]
    };
  }

  conditionTypeLabel(type: number): string {
    return conditionTypeLabels[type] ?? `Type ${type}`;
  }

  operatorLabel(op: number): string {
    return operatorLabels[op] ?? `Op ${op}`;
  }

  addCondition() {
    this.newAlert.conditions.push({
      type: 0,
      operator: 0,
      value: 0,
      combineWith: this.newAlert.conditions.length > 0 ? 0 : null
    });
  }

  removeCondition(index: number) {
    this.newAlert.conditions.splice(index, 1);
    // First condition should never have a combineWith
    if (this.newAlert.conditions.length > 0) {
      this.newAlert.conditions[0].combineWith = null;
    }
  }

  // Symbol autocomplete
  get symbolCategories(): string[] {
    const cats = new Set(this.symbols.map(s => s.category));
    return ['All', ...Array.from(cats).sort()];
  }

  get filteredSymbols(): SymbolInfo[] {
    const q = this.symbolSearch.toLowerCase();
    let list = this.symbols;
    if (this.activeCategory !== 'All') list = list.filter(s => s.category === this.activeCategory);
    if (q) list = list.filter(s => s.name.toLowerCase().includes(q) || s.description.toLowerCase().includes(q));
    return list;
  }

  get groupedFilteredSymbols(): { category: string; symbols: SymbolInfo[] }[] {
    const map = new Map<string, SymbolInfo[]>();
    for (const s of this.filteredSymbols) {
      const arr = map.get(s.category);
      if (arr) arr.push(s); else map.set(s.category, [s]);
    }
    return Array.from(map.entries()).sort((a, b) => a[0].localeCompare(b[0]))
      .map(([category, symbols]) => ({ category, symbols }));
  }

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent) {
    if (this.showSymbolDropdown && !this.elRef.nativeElement.querySelector('.symbol-autocomplete')?.contains(event.target as Node)) {
      this.showSymbolDropdown = false;
    }
  }

  openSymbolDropdown() {
    this.showSymbolDropdown = true;
    if (this.newAlert.symbol) this.symbolSearch = '';
    if (this.symbols.length === 0) this.loadSymbols();
  }

  onSymbolSearchInput(event: Event) {
    this.symbolSearch = (event.target as HTMLInputElement).value;
    if (this.newAlert.symbol) this.newAlert.symbol = '';
    this.showSymbolDropdown = true;
  }

  selectSymbol(s: SymbolInfo) {
    this.newAlert.symbol = s.name;
    this.symbolSearch = '';
    this.showSymbolDropdown = false;
  }

  clearSymbol() {
    this.newAlert.symbol = '';
    this.symbolSearch = '';
  }

  setCategory(cat: string) { this.activeCategory = cat; }

  onSymbolKeydown(event: KeyboardEvent) {
    if (event.key === 'Escape') { this.showSymbolDropdown = false; return; }
    if (event.key === 'Enter') {
      const f = this.filteredSymbols;
      if (f.length === 1) this.selectSymbol(f[0]);
    }
  }

  loadSymbols() {
    this.http.get<SymbolInfo[]>(`${environment.apiUrl}/api/positions/symbols`).subscribe({
      next: (data) => this.symbols = data
    });
  }

  cancelCreate() {
    this.showCreateForm = false;
    this.newAlert = this.freshAlert();
    this.symbolSearch = '';
    this.showSymbolDropdown = false;
    this.activeCategory = 'All';
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
    const body: any = {
      symbol: this.newAlert.symbol,
      name: this.newAlert.name,
      description: this.newAlert.description || null,
      type: this.newAlert.type,
      autoPrepareOrder: this.newAlert.autoPrepareOrder,
      aiEnrichEnabled: this.newAlert.aiEnrichEnabled,
      notifyTelegram: this.newAlert.notifyTelegram,
      notifyDashboard: this.newAlert.notifyDashboard,
      maxTriggers: this.newAlert.maxTriggers || null,
      conditions: this.newAlert.conditions.length > 0 ? this.newAlert.conditions : null
    };

    this.http.post(`${environment.apiUrl}/api/alerts`, body).subscribe({
      next: () => {
        this.loadAlerts();
        this.showCreateForm = false;
        this.newAlert = this.freshAlert();
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

  async deleteAlert(id: number) {
    const ok = await this.dialog.confirm({
      title: 'Delete Alert',
      message: 'Are you sure you want to delete this alert?',
      confirmText: 'Delete',
      variant: 'danger'
    });
    if (!ok) return;
    this.http.delete(`${environment.apiUrl}/api/alerts/${id}`).subscribe({
      next: () => this.loadAlerts()
    });
  }
}
