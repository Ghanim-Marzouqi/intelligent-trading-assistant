import { Component, OnInit, HostListener, ElementRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../environments/environment';
import { ConfirmDialogService } from '../shared/services/confirm-dialog.service';

interface SymbolInfo {
  name: string;
  minVolume: number;
  maxVolume: number;
  volumeStep: number;
  digits: number;
  category: string;
  description: string;
}

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

@Component({
  selector: 'app-watchlist',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="watchlist-page">
      <header class="page-header">
        <h1>Watchlist</h1>
        <button class="primary" (click)="toggleAddForm()">
          {{ showAddForm ? 'Cancel' : '+ Add Symbol' }}
        </button>
      </header>

      <!-- Schedule Settings Card -->
      <div class="card schedule-card">
        <div class="schedule-header">
          <h3>Scan Schedule</h3>
          @if (settingsDirty) {
            <div class="schedule-actions">
              <button class="small" (click)="discardSettings()">Discard</button>
              <button class="primary small" [disabled]="savingSettings" (click)="saveSettings()">
                {{ savingSettings ? 'Saving...' : 'Save' }}
              </button>
            </div>
          }
        </div>
        @if (settingsError) {
          <p class="error-text">{{ settingsError }}</p>
        }
        @if (settingsSaved) {
          <p class="success-text">Settings saved</p>
        }
        <div class="schedule-info">
          <div class="info-item">
            <span class="info-label">Scan Times (UTC)</span>
            <div class="hour-chips">
              @if (scheduleHours.length > 0) {
                @for (h of scheduleHours; track h) {
                  <span class="hour-chip">
                    {{ formatHour(h) }}
                    <span class="session-hint">{{ getSessionLabel(h) }}</span>
                    <button class="chip-remove" (click)="removeHour(h)">&times;</button>
                  </span>
                }
              } @else {
                <span class="text-muted">No scan times set</span>
              }
            </div>
            <div class="add-hour-row">
              <select class="hour-select" [(ngModel)]="newHour">
                <option [ngValue]="null" disabled>Add hour...</option>
                @for (h of availableHours; track h) {
                  <option [ngValue]="h">{{ formatHour(h) }} — {{ getSessionLabel(h) }}</option>
                }
              </select>
              <button class="small primary" [disabled]="newHour === null" (click)="addHour()">Add</button>
            </div>
          </div>
          <div class="info-item confidence-item">
            <span class="info-label">Auto-Prepare Min Confidence</span>
            <div class="confidence-control">
              <input
                type="range"
                min="0" max="100" step="5"
                [(ngModel)]="minConfidence"
                (ngModelChange)="onSettingsChange()" />
              <input
                type="number"
                class="confidence-input"
                min="0" max="100"
                [(ngModel)]="minConfidence"
                (ngModelChange)="onSettingsChange()" />
              <span class="confidence-pct">%</span>
            </div>
          </div>
        </div>

        <div class="risk-limits-section">
          <span class="info-label">Risk Limits</span>
          <div class="risk-limits-grid">
            <div class="risk-field">
              <label>Max Open Positions</label>
              <input type="number" min="1" max="20" step="1"
                [(ngModel)]="maxOpenPositions"
                (ngModelChange)="onSettingsChange()" />
            </div>
            <div class="risk-field">
              <label>Max Total Volume</label>
              <input type="number" min="0.01" max="100" step="0.5"
                [(ngModel)]="maxTotalVolume"
                (ngModelChange)="onSettingsChange()" />
            </div>
            <div class="risk-field">
              <label>Max Positions Per Symbol</label>
              <input type="number" min="1" max="10" step="1"
                [(ngModel)]="maxPositionsPerSymbol"
                (ngModelChange)="onSettingsChange()" />
            </div>
            <div class="risk-field">
              <label>Max Daily Loss %</label>
              <input type="number" min="0.5" max="20" step="0.5"
                [(ngModel)]="maxDailyLossPercent"
                (ngModelChange)="onSettingsChange()" />
            </div>
          </div>
        </div>
      </div>

      <!-- Add Symbol Section -->
      @if (showAddForm) {
        <div class="card add-card">
          <h3>Add Symbol to Watchlist</h3>
          <div class="add-form">
            <div class="form-group symbol-autocomplete">
              <label>Symbol</label>
              <div class="symbol-input-wrap">
                <input
                  type="text"
                  class="symbol-input"
                  [value]="selectedSymbolName || symbolSearch"
                  (input)="onSymbolSearchInput($event)"
                  (focus)="openSymbolDropdown()"
                  (keydown)="onSymbolKeydown($event)"
                  [placeholder]="selectedSymbolName ? '' : 'Search symbol...'"
                  autocomplete="off" />
                @if (selectedSymbolName) {
                  <button class="symbol-clear" (mousedown)="$event.preventDefault()" (click)="clearSymbol()">&times;</button>
                }
              </div>
              @if (showSymbolDropdown) {
                <div class="symbol-dropdown">
                  <div class="category-tabs">
                    @for (cat of symbolCategories; track cat) {
                      <button
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
                          [class.already-added]="isOnWatchlist(s.name)"
                          (mousedown)="$event.preventDefault()"
                          (click)="selectSymbol(s)">
                          <span class="symbol-name">{{ s.name }}</span>
                          <span class="symbol-desc">{{ s.description }}</span>
                          @if (isOnWatchlist(s.name)) {
                            <span class="added-badge">Added</span>
                          }
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
            <button
              class="primary add-btn"
              [disabled]="!selectedSymbolName || adding"
              (click)="addSymbol()">
              {{ adding ? 'Adding...' : 'Add' }}
            </button>
          </div>
          @if (addError) {
            <p class="error-text">{{ addError }}</p>
          }
        </div>
      }

      <!-- Watchlist Table -->
      <div class="card">
        <h3>Watched Symbols</h3>
        @if (loading) {
          <p class="empty-state">Loading...</p>
        } @else if (watchlist.length === 0) {
          <div class="empty-state">
            <p>No symbols on your watchlist yet.</p>
            <p class="text-muted">Add symbols to be analyzed on the scheduled scan times above.</p>
          </div>
        } @else {
          <div class="table-scroll table-scroll--no-cards">
            <table>
              <thead>
                <tr>
                  <th>Symbol</th>
                  <th>Added At</th>
                  <th>Actions</th>
                </tr>
              </thead>
              <tbody>
                @for (entry of watchlist; track entry.id) {
                  <tr>
                    <td><strong>{{ entry.symbol }}</strong></td>
                    <td>{{ entry.addedAt | date:'medium' }}</td>
                    <td>
                      <button class="danger small" (click)="removeSymbol(entry)">Remove</button>
                    </td>
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

    .schedule-card {
      margin-bottom: 20px;
    }

    .schedule-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-bottom: 12px;
    }

    .schedule-header h3 {
      margin-bottom: 0;
    }

    .schedule-actions {
      display: flex;
      gap: 8px;
    }

    .schedule-info {
      display: flex;
      gap: 40px;
    }

    .info-item {
      display: flex;
      flex-direction: column;
      gap: 8px;
    }

    .info-label {
      font-size: 12px;
      font-weight: 600;
      color: var(--text-muted);
      text-transform: uppercase;
      letter-spacing: 0.05em;
    }

    .hour-chips {
      display: flex;
      gap: 8px;
      align-items: center;
      flex-wrap: wrap;
    }

    .hour-chip {
      display: inline-flex;
      align-items: center;
      gap: 6px;
      padding: 4px 8px 4px 10px;
      background: var(--surface-light);
      border: 1px solid var(--border);
      border-radius: var(--radius);
      font-size: 13px;
      font-weight: 600;
      font-family: var(--font-mono);
    }

    .session-hint {
      font-size: 10px;
      font-weight: 400;
      color: var(--text-muted);
      font-family: inherit;
    }

    .chip-remove {
      background: none;
      border: none;
      color: var(--text-muted);
      font-size: 16px;
      cursor: pointer;
      padding: 0 2px;
      line-height: 1;
      border-radius: 3px;
      transition: color 0.15s, background 0.15s;
    }

    .chip-remove:hover {
      color: var(--danger);
      background: rgba(255, 0, 0, 0.1);
    }

    .add-hour-row {
      display: flex;
      gap: 8px;
      align-items: center;
    }

    .hour-select {
      padding: 6px 8px;
      background: var(--surface);
      border: 1px solid var(--border);
      border-radius: var(--radius);
      color: var(--text);
      font-size: 13px;
      min-width: 200px;
    }

    .confidence-item {
      min-width: 240px;
    }

    .confidence-control {
      display: flex;
      align-items: center;
      gap: 8px;
    }

    .confidence-control input[type="range"] {
      flex: 1;
      accent-color: var(--primary);
    }

    .confidence-input {
      width: 52px;
      padding: 4px 6px;
      text-align: center;
      background: var(--surface);
      border: 1px solid var(--border);
      border-radius: var(--radius);
      color: var(--text);
      font-size: 13px;
      font-family: var(--font-mono);
    }

    .confidence-pct {
      font-size: 13px;
      color: var(--text-muted);
    }

    .risk-limits-section {
      margin-top: 20px;
      padding-top: 16px;
      border-top: 1px solid var(--border);
      display: flex;
      flex-direction: column;
      gap: 10px;
    }

    .risk-limits-grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(180px, 1fr));
      gap: 12px;
    }

    .risk-field {
      display: flex;
      flex-direction: column;
      gap: 4px;
    }

    .risk-field label {
      font-size: 11px;
      font-weight: 500;
      color: var(--text-muted);
    }

    .risk-field input {
      width: 100%;
      padding: 6px 8px;
      background: var(--surface);
      border: 1px solid var(--border);
      border-radius: var(--radius);
      color: var(--text);
      font-size: 13px;
      font-family: var(--font-mono);
    }

    .success-text {
      color: var(--success, #22c55e);
      font-size: 13px;
      margin-bottom: 8px;
    }

    .text-muted {
      color: var(--text-muted);
    }

    .add-card {
      margin-bottom: 20px;
    }

    .add-card h3 {
      margin-bottom: 12px;
    }

    .add-form {
      display: flex;
      gap: 12px;
      align-items: flex-end;
    }

    .add-form .form-group {
      flex: 1;
    }

    .add-btn {
      height: 38px;
      min-width: 80px;
      flex-shrink: 0;
    }

    .form-group {
      display: flex;
      flex-direction: column;
      gap: 6px;
    }

    .form-group label {
      font-size: 12px;
      font-weight: 600;
      color: var(--text-muted);
      text-transform: uppercase;
      letter-spacing: 0.05em;
    }

    .error-text {
      color: var(--danger);
      font-size: 13px;
      margin-top: 8px;
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
    }

    /* Symbol Autocomplete */
    .symbol-autocomplete {
      position: relative;
    }
    .symbol-input-wrap {
      position: relative;
      display: flex;
      align-items: center;
    }
    .symbol-input {
      width: 100%;
      padding-right: 32px;
    }
    .symbol-clear {
      position: absolute;
      right: 8px;
      top: 50%;
      transform: translateY(-50%);
      background: none;
      border: none;
      color: var(--text-muted);
      font-size: 18px;
      cursor: pointer;
      padding: 0 4px;
      line-height: 1;
    }
    .symbol-clear:hover {
      color: var(--text);
    }
    .symbol-dropdown {
      position: absolute;
      top: 100%;
      left: 0;
      right: 0;
      z-index: 100;
      background: var(--surface);
      border: 1px solid var(--border);
      border-radius: var(--radius);
      margin-top: 4px;
      box-shadow: 0 8px 24px rgba(0, 0, 0, 0.4);
      overflow: hidden;
    }
    .category-tabs {
      display: flex;
      gap: 0;
      border-bottom: 1px solid var(--border);
      overflow-x: auto;
      scrollbar-width: none;
    }
    .category-tabs::-webkit-scrollbar { display: none; }
    .cat-tab {
      flex: 0 0 auto;
      padding: 8px 12px;
      font-size: 11px;
      font-weight: 600;
      text-transform: uppercase;
      letter-spacing: 0.04em;
      background: none;
      color: var(--text-muted);
      border: none;
      border-bottom: 2px solid transparent;
      border-radius: 0;
      cursor: pointer;
      white-space: nowrap;
      transition: color 0.15s, border-color 0.15s;
    }
    .cat-tab:hover {
      color: var(--text);
    }
    .cat-tab.active {
      color: var(--primary);
      border-bottom-color: var(--primary);
    }
    .symbol-list {
      max-height: 260px;
      overflow-y: auto;
    }
    .symbol-group-header {
      padding: 6px 12px;
      font-size: 10px;
      font-weight: 700;
      text-transform: uppercase;
      letter-spacing: 0.06em;
      color: var(--text-muted);
      background: var(--surface-light);
      position: sticky;
      top: 0;
      z-index: 1;
    }
    .symbol-option {
      display: flex;
      align-items: center;
      gap: 10px;
      padding: 8px 12px;
      cursor: pointer;
      transition: background 0.1s;
    }
    .symbol-option:hover {
      background: var(--surface-light);
    }
    .symbol-option.already-added {
      opacity: 0.5;
    }
    .symbol-name {
      font-weight: 600;
      font-size: 13px;
      min-width: 90px;
      font-family: var(--font-mono);
    }
    .symbol-desc {
      font-size: 12px;
      color: var(--text-muted);
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
      flex: 1;
    }
    .added-badge {
      font-size: 10px;
      font-weight: 600;
      text-transform: uppercase;
      color: var(--primary);
      letter-spacing: 0.04em;
      flex-shrink: 0;
    }
    .symbol-no-match {
      padding: 20px 12px;
      text-align: center;
      color: var(--text-muted);
      font-size: 13px;
    }

    @media (max-width: 768px) {
      .schedule-info {
        flex-direction: column;
        gap: 16px;
      }

      .hour-select {
        width: 100%;
        min-width: 0;
      }

      .add-form {
        flex-direction: column;
        align-items: stretch;
      }

      .add-btn {
        width: 100%;
      }

      .confidence-item {
        min-width: 0;
      }

      .risk-limits-grid {
        grid-template-columns: repeat(2, 1fr);
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

      .risk-limits-grid {
        grid-template-columns: 1fr;
      }
    }
  `]
})
export class WatchlistComponent implements OnInit {
  watchlist: WatchlistEntry[] = [];
  scheduleHours: number[] = [];
  minConfidence = 70;
  loading = true;

  // Risk limits
  maxOpenPositions = 3;
  maxTotalVolume = 10;
  maxPositionsPerSymbol = 3;
  maxDailyLossPercent = 5;

  // Settings editing state
  savedScheduleHours: number[] = [];
  savedMinConfidence = 70;
  savedMaxOpenPositions = 3;
  savedMaxTotalVolume = 10;
  savedMaxPositionsPerSymbol = 3;
  savedMaxDailyLossPercent = 5;
  settingsDirty = false;
  savingSettings = false;
  settingsError = '';
  settingsSaved = false;
  newHour: number | null = null;

  showAddForm = false;
  adding = false;
  addError = '';

  // Symbol autocomplete state
  symbols: SymbolInfo[] = [];
  symbolSearch = '';
  selectedSymbolName = '';
  showSymbolDropdown = false;
  activeCategory = 'All';

  private readonly sessionLabels: Record<number, string> = {
    0: 'Sydney',
    1: 'Sydney',
    2: 'Sydney',
    3: 'Tokyo Open',
    4: 'Tokyo',
    5: 'Tokyo',
    6: 'Tokyo',
    7: 'London Open',
    8: 'London',
    9: 'London',
    10: 'London',
    11: 'London',
    12: 'NY Open',
    13: 'NY / London',
    14: 'NY / London',
    15: 'NY / London',
    16: 'London Close',
    17: 'NY',
    18: 'NY',
    19: 'NY',
    20: 'NY Close',
    21: 'Sydney',
    22: 'Sydney',
    23: 'Sydney',
  };

  constructor(private http: HttpClient, private elRef: ElementRef, private dialog: ConfirmDialogService) {}

  ngOnInit() {
    this.loadWatchlist();
  }

  get symbolCategories(): string[] {
    const cats = new Set(this.symbols.map(s => s.category));
    return ['All', ...Array.from(cats).sort()];
  }

  get filteredSymbols(): SymbolInfo[] {
    const q = this.symbolSearch.toLowerCase();
    let list = this.symbols;
    if (this.activeCategory !== 'All') {
      list = list.filter(s => s.category === this.activeCategory);
    }
    if (q) {
      list = list.filter(s =>
        s.name.toLowerCase().includes(q) ||
        s.description.toLowerCase().includes(q)
      );
    }
    return list;
  }

  get groupedFilteredSymbols(): { category: string; symbols: SymbolInfo[] }[] {
    const filtered = this.filteredSymbols;
    const map = new Map<string, SymbolInfo[]>();
    for (const s of filtered) {
      const arr = map.get(s.category);
      if (arr) arr.push(s);
      else map.set(s.category, [s]);
    }
    return Array.from(map.entries())
      .sort((a, b) => a[0].localeCompare(b[0]))
      .map(([category, symbols]) => ({ category, symbols }));
  }

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent) {
    if (this.showSymbolDropdown && !this.elRef.nativeElement.querySelector('.symbol-autocomplete')?.contains(event.target as Node)) {
      this.showSymbolDropdown = false;
    }
  }

  isOnWatchlist(symbolName: string): boolean {
    return this.watchlist.some(w => w.symbol === symbolName);
  }

  openSymbolDropdown() {
    this.showSymbolDropdown = true;
    if (this.selectedSymbolName) {
      this.symbolSearch = '';
    }
  }

  onSymbolSearchInput(event: Event) {
    const input = event.target as HTMLInputElement;
    this.symbolSearch = input.value;
    if (this.selectedSymbolName) {
      this.selectedSymbolName = '';
    }
    this.showSymbolDropdown = true;
    this.addError = '';
  }

  selectSymbol(s: SymbolInfo) {
    if (this.isOnWatchlist(s.name)) return;
    this.selectedSymbolName = s.name;
    this.symbolSearch = '';
    this.showSymbolDropdown = false;
    this.addError = '';
  }

  clearSymbol() {
    this.selectedSymbolName = '';
    this.symbolSearch = '';
    this.addError = '';
  }

  setCategory(cat: string) {
    this.activeCategory = cat;
  }

  onSymbolKeydown(event: KeyboardEvent) {
    if (event.key === 'Escape') {
      this.showSymbolDropdown = false;
      return;
    }
    if (event.key === 'Enter') {
      const filtered = this.filteredSymbols;
      if (filtered.length === 1 && !this.isOnWatchlist(filtered[0].name)) {
        this.selectSymbol(filtered[0]);
      }
    }
  }

  toggleAddForm() {
    this.showAddForm = !this.showAddForm;
    if (this.showAddForm && this.symbols.length === 0) {
      this.loadSymbols();
    }
    if (!this.showAddForm) {
      this.clearSymbol();
      this.showSymbolDropdown = false;
      this.activeCategory = 'All';
    }
  }

  loadSymbols() {
    this.http.get<SymbolInfo[]>(`${environment.apiUrl}/api/positions/symbols`).subscribe({
      next: (data) => { this.symbols = data; }
    });
  }

  // Schedule editing helpers

  get availableHours(): number[] {
    return Array.from({ length: 24 }, (_, i) => i)
      .filter(h => !this.scheduleHours.includes(h));
  }

  formatHour(h: number): string {
    return `${h.toString().padStart(2, '0')}:00`;
  }

  getSessionLabel(h: number): string {
    return this.sessionLabels[h] ?? '';
  }

  addHour() {
    if (this.newHour === null || this.scheduleHours.includes(this.newHour)) return;
    this.scheduleHours = [...this.scheduleHours, this.newHour].sort((a, b) => a - b);
    this.newHour = null;
    this.onSettingsChange();
  }

  removeHour(h: number) {
    this.scheduleHours = this.scheduleHours.filter(x => x !== h);
    this.onSettingsChange();
  }

  onSettingsChange() {
    this.settingsSaved = false;
    this.settingsError = '';
    this.settingsDirty =
      JSON.stringify(this.scheduleHours) !== JSON.stringify(this.savedScheduleHours) ||
      this.minConfidence !== this.savedMinConfidence ||
      this.maxOpenPositions !== this.savedMaxOpenPositions ||
      this.maxTotalVolume !== this.savedMaxTotalVolume ||
      this.maxPositionsPerSymbol !== this.savedMaxPositionsPerSymbol ||
      this.maxDailyLossPercent !== this.savedMaxDailyLossPercent;
  }

  discardSettings() {
    this.scheduleHours = [...this.savedScheduleHours];
    this.minConfidence = this.savedMinConfidence;
    this.maxOpenPositions = this.savedMaxOpenPositions;
    this.maxTotalVolume = this.savedMaxTotalVolume;
    this.maxPositionsPerSymbol = this.savedMaxPositionsPerSymbol;
    this.maxDailyLossPercent = this.savedMaxDailyLossPercent;
    this.settingsDirty = false;
    this.settingsError = '';
    this.settingsSaved = false;
  }

  saveSettings() {
    this.savingSettings = true;
    this.settingsError = '';
    this.settingsSaved = false;

    this.http.put<{
      scheduleUtcHours: number[];
      autoPrepareMinConfidence: number;
      maxOpenPositions: number;
      maxTotalVolume: number;
      maxPositionsPerSymbol: number;
      maxDailyLossPercent: number;
    }>(
      `${environment.apiUrl}/api/watchlist/settings`,
      {
        scheduleUtcHours: this.scheduleHours,
        autoPrepareMinConfidence: this.minConfidence,
        maxOpenPositions: this.maxOpenPositions,
        maxTotalVolume: this.maxTotalVolume,
        maxPositionsPerSymbol: this.maxPositionsPerSymbol,
        maxDailyLossPercent: this.maxDailyLossPercent
      }
    ).subscribe({
      next: (data) => {
        this.scheduleHours = data.scheduleUtcHours;
        this.minConfidence = data.autoPrepareMinConfidence;
        this.maxOpenPositions = data.maxOpenPositions;
        this.maxTotalVolume = data.maxTotalVolume;
        this.maxPositionsPerSymbol = data.maxPositionsPerSymbol;
        this.maxDailyLossPercent = data.maxDailyLossPercent;
        this.savedScheduleHours = [...this.scheduleHours];
        this.savedMinConfidence = this.minConfidence;
        this.savedMaxOpenPositions = this.maxOpenPositions;
        this.savedMaxTotalVolume = this.maxTotalVolume;
        this.savedMaxPositionsPerSymbol = this.maxPositionsPerSymbol;
        this.savedMaxDailyLossPercent = this.maxDailyLossPercent;
        this.settingsDirty = false;
        this.savingSettings = false;
        this.settingsSaved = true;
      },
      error: (err) => {
        this.savingSettings = false;
        this.settingsError = err.error?.error ?? 'Failed to save settings';
      }
    });
  }

  loadWatchlist() {
    this.http.get<WatchlistResponse>(`${environment.apiUrl}/api/watchlist`).subscribe({
      next: (data) => {
        this.watchlist = data.symbols;
        this.scheduleHours = data.scheduleUtcHours;
        this.minConfidence = data.autoPrepareMinConfidence;
        this.maxOpenPositions = data.maxOpenPositions;
        this.maxTotalVolume = data.maxTotalVolume;
        this.maxPositionsPerSymbol = data.maxPositionsPerSymbol;
        this.maxDailyLossPercent = data.maxDailyLossPercent;
        this.savedScheduleHours = [...data.scheduleUtcHours];
        this.savedMinConfidence = data.autoPrepareMinConfidence;
        this.savedMaxOpenPositions = data.maxOpenPositions;
        this.savedMaxTotalVolume = data.maxTotalVolume;
        this.savedMaxPositionsPerSymbol = data.maxPositionsPerSymbol;
        this.savedMaxDailyLossPercent = data.maxDailyLossPercent;
        this.settingsDirty = false;
        this.loading = false;
      },
      error: () => {
        this.loading = false;
      }
    });
  }

  addSymbol() {
    if (!this.selectedSymbolName || this.adding) return;
    this.adding = true;
    this.addError = '';

    this.http.post<WatchlistEntry>(`${environment.apiUrl}/api/watchlist`, {
      symbol: this.selectedSymbolName
    }).subscribe({
      next: () => {
        this.adding = false;
        this.clearSymbol();
        this.loadWatchlist();
      },
      error: (err) => {
        this.adding = false;
        this.addError = err.error?.error ?? 'Failed to add symbol';
      }
    });
  }

  async removeSymbol(entry: WatchlistEntry) {
    const ok = await this.dialog.confirm({
      title: 'Remove Symbol',
      message: `Remove ${entry.symbol} from watchlist?`,
      confirmText: 'Remove',
      variant: 'danger'
    });
    if (!ok) return;
    this.http.delete(`${environment.apiUrl}/api/watchlist/${entry.id}`).subscribe({
      next: () => this.loadWatchlist()
    });
  }
}
