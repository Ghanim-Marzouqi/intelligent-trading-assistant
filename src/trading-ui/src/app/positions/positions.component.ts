import { Component, OnInit, OnDestroy, HostListener, ElementRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { Subscription } from 'rxjs';
import { environment } from '../../environments/environment';
import { SignalRService, PriceUpdate } from '../shared/services/signalr.service';

interface SymbolInfo {
  name: string;
  minVolume: number;
  maxVolume: number;
  volumeStep: number;
  digits: number;
  category: string;
  description: string;
}

interface OrderResult {
  success: boolean;
  orderId?: number;
  positionId?: number;
  errorMessage?: string;
  errorCode?: string;
}

@Component({
  selector: 'app-positions',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="positions-page">
      <header class="page-header">
        <h1>Positions</h1>
        <button class="primary" (click)="toggleTradeForm()">
          {{ showTradeForm ? 'Cancel' : '+ Open Position' }}
        </button>
      </header>

      <!-- Result Banner -->
      @if (resultBanner) {
        <div class="result-banner" [class.success]="resultBanner.success" [class.error]="!resultBanner.success">
          <span>{{ resultBanner.message }}</span>
          <button class="banner-close" (click)="resultBanner = null">&times;</button>
        </div>
      }

      <!-- Trade Form -->
      @if (showTradeForm) {
        <div class="card trade-form-card">
          <h3>Open Position</h3>

          <div class="form-grid">
            <!-- Symbol -->
            <div class="form-group symbol-autocomplete">
              <label>Symbol</label>
              <div class="symbol-input-wrap">
                <input
                  type="text"
                  class="symbol-input"
                  [value]="tradeForm.symbol || symbolSearch"
                  (input)="onSymbolSearchInput($event)"
                  (focus)="openSymbolDropdown()"
                  (keydown)="onSymbolKeydown($event)"
                  [placeholder]="tradeForm.symbol ? '' : 'Search symbol...'"
                  autocomplete="off" />
                @if (tradeForm.symbol) {
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

            <!-- Direction -->
            <div class="form-group">
              <label>Direction</label>
              <div class="direction-toggle">
                <button
                  class="dir-btn buy-btn"
                  [class.active]="tradeForm.direction === 'Buy'"
                  (click)="tradeForm.direction = 'Buy'">
                  Buy
                </button>
                <button
                  class="dir-btn sell-btn"
                  [class.active]="tradeForm.direction === 'Sell'"
                  (click)="tradeForm.direction = 'Sell'">
                  Sell
                </button>
              </div>
            </div>

            <!-- Order Type -->
            <div class="form-group">
              <label>Order Type</label>
              <div class="order-type-selector">
                @for (type of orderTypes; track type) {
                  <button
                    class="type-btn"
                    [class.active]="tradeForm.orderType === type"
                    (click)="tradeForm.orderType = type">
                    {{ type }}
                  </button>
                }
              </div>
            </div>

            <!-- Volume -->
            <div class="form-group">
              <label>Volume (lots)</label>
              <input
                type="number"
                [(ngModel)]="tradeForm.volume"
                [min]="selectedSymbol?.minVolume ?? 0.01"
                [max]="selectedSymbol?.maxVolume ?? 100"
                [step]="selectedSymbol?.volumeStep ?? 0.01"
                placeholder="0.01" />
              @if (selectedSymbol) {
                <span class="hint">
                  Min: {{ selectedSymbol.minVolume }} &middot;
                  Max: {{ selectedSymbol.maxVolume }} &middot;
                  Step: {{ selectedSymbol.volumeStep }}
                </span>
              }
            </div>

            <!-- Price (Limit/Stop only) -->
            @if (tradeForm.orderType !== 'Market') {
              <div class="form-group">
                <label>Price</label>
                <input
                  type="number"
                  [(ngModel)]="tradeForm.price"
                  [step]="priceStep"
                  placeholder="Enter price" />
              </div>
            }

            <!-- Stop Loss -->
            <div class="form-group">
              <label>Stop Loss <span class="optional">(optional)</span></label>
              <input
                type="number"
                [(ngModel)]="tradeForm.stopLoss"
                [step]="priceStep"
                placeholder="Stop loss" />
            </div>

            <!-- Take Profit -->
            <div class="form-group">
              <label>Take Profit <span class="optional">(optional)</span></label>
              <input
                type="number"
                [(ngModel)]="tradeForm.takeProfit"
                [step]="priceStep"
                placeholder="Take profit" />
            </div>
          </div>

          <!-- Live Price Display -->
          @if (livePrice) {
            <div class="live-price-bar">
              <div class="price-item">
                <span class="price-label">Bid</span>
                <span class="price-value sell-color">{{ livePrice.bid.toFixed(selectedSymbol?.digits ?? 5) }}</span>
              </div>
              <div class="price-item">
                <span class="price-label">Spread</span>
                <span class="price-value">{{ spread }}</span>
              </div>
              <div class="price-item">
                <span class="price-label">Ask</span>
                <span class="price-value buy-color">{{ livePrice.ask.toFixed(selectedSymbol?.digits ?? 5) }}</span>
              </div>
            </div>
          }

          <!-- Confirmation / Submit -->
          <div class="form-actions">
            @if (!confirming) {
              <button
                class="primary submit-btn"
                [disabled]="!isFormValid()"
                (click)="confirming = true">
                Review Order
              </button>
            } @else {
              <div class="confirm-panel">
                <p class="confirm-summary">
                  <strong>{{ tradeForm.orderType }}</strong>
                  <span [class.buy-color]="tradeForm.direction === 'Buy'" [class.sell-color]="tradeForm.direction === 'Sell'">
                    {{ tradeForm.direction }}
                  </span>
                  <strong>{{ tradeForm.volume }}</strong> lots of
                  <strong>{{ tradeForm.symbol }}</strong>
                  @if (tradeForm.orderType !== 'Market' && tradeForm.price) {
                    <span> &#64; {{ tradeForm.price }}</span>
                  }
                  @if (tradeForm.stopLoss) {
                    <span> &middot; SL: {{ tradeForm.stopLoss }}</span>
                  }
                  @if (tradeForm.takeProfit) {
                    <span> &middot; TP: {{ tradeForm.takeProfit }}</span>
                  }
                </p>
                <div class="confirm-buttons">
                  <button class="danger" (click)="confirming = false">Back</button>
                  <button
                    [class]="tradeForm.direction === 'Buy' ? 'success' : 'danger'"
                    [disabled]="submitting"
                    (click)="submitOrder()">
                    {{ submitting ? 'Placing...' : 'Confirm ' + tradeForm.direction }}
                  </button>
                </div>
              </div>
            }
          </div>
        </div>
      }

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
      display: flex;
      justify-content: space-between;
      align-items: center;
    }

    .loading, .empty-state {
      text-align: center;
      color: var(--text-muted);
      padding: 40px;
    }

    .card h3 {
      margin-bottom: 16px;
    }

    .buy, .buy-color { color: var(--success); }
    .sell, .sell-color { color: var(--danger); }

    button.small {
      padding: 4px 8px;
      font-size: 12px;
    }

    /* Result Banner */
    .result-banner {
      display: flex;
      justify-content: space-between;
      align-items: center;
      padding: 12px 16px;
      border-radius: var(--radius);
      margin-bottom: 16px;
      font-size: 14px;
    }
    .result-banner.success {
      background: var(--success-glow);
      border: 1px solid var(--success);
      color: var(--success);
    }
    .result-banner.error {
      background: var(--danger-glow);
      border: 1px solid var(--danger);
      color: var(--danger);
    }
    .banner-close {
      background: none;
      color: inherit;
      font-size: 18px;
      padding: 0 4px;
    }

    /* Trade Form */
    .trade-form-card {
      margin-bottom: 20px;
    }

    .form-grid {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 16px;
      margin-bottom: 16px;
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

    .optional {
      font-weight: 400;
      text-transform: none;
      letter-spacing: normal;
    }

    .hint {
      font-size: 11px;
      color: var(--text-muted);
    }

    /* Direction Toggle */
    .direction-toggle {
      display: flex;
      gap: 0;
      border-radius: var(--radius);
      overflow: hidden;
      border: 1px solid var(--border);
    }
    .dir-btn {
      flex: 1;
      padding: 10px;
      font-weight: 600;
      font-size: 14px;
      background: var(--surface-light);
      color: var(--text-muted);
      border-radius: 0;
      transition: all 0.2s;
    }
    .buy-btn.active {
      background: var(--success);
      color: white;
      box-shadow: 0 0 12px var(--success-glow);
    }
    .sell-btn.active {
      background: var(--danger);
      color: white;
      box-shadow: 0 0 12px var(--danger-glow);
    }

    /* Order Type Selector */
    .order-type-selector {
      display: flex;
      gap: 0;
      border-radius: var(--radius);
      overflow: hidden;
      border: 1px solid var(--border);
    }
    .type-btn {
      flex: 1;
      padding: 10px;
      font-size: 13px;
      font-weight: 500;
      background: var(--surface-light);
      color: var(--text-muted);
      border-radius: 0;
      transition: all 0.2s;
    }
    .type-btn.active {
      background: var(--primary);
      color: white;
    }

    /* Live Price Bar */
    .live-price-bar {
      display: flex;
      justify-content: center;
      gap: 32px;
      padding: 12px 16px;
      background: var(--surface-light);
      border-radius: var(--radius);
      margin-bottom: 16px;
      border: 1px solid var(--border);
    }
    .price-item {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 2px;
    }
    .price-label {
      font-size: 11px;
      color: var(--text-muted);
      text-transform: uppercase;
      letter-spacing: 0.05em;
    }
    .price-value {
      font-family: var(--font-mono);
      font-size: 16px;
      font-weight: 600;
    }

    /* Form Actions */
    .form-actions {
      display: flex;
      justify-content: flex-end;
    }
    .submit-btn {
      min-width: 140px;
    }
    .submit-btn:disabled {
      opacity: 0.5;
      cursor: not-allowed;
    }

    /* Confirmation Panel */
    .confirm-panel {
      width: 100%;
      padding: 16px;
      background: var(--surface-light);
      border-radius: var(--radius);
      border: 1px solid var(--border-light);
    }
    .confirm-summary {
      margin-bottom: 12px;
      font-size: 14px;
      line-height: 1.6;
    }
    .confirm-buttons {
      display: flex;
      gap: 8px;
      justify-content: flex-end;
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
    }
    .symbol-no-match {
      padding: 20px 12px;
      text-align: center;
      color: var(--text-muted);
      font-size: 13px;
    }
  `]
})
export class PositionsComponent implements OnInit, OnDestroy {
  positions: any[] = [];
  history: any[] = [];
  loading = true;

  showTradeForm = false;
  symbols: SymbolInfo[] = [];
  selectedSymbol: SymbolInfo | null = null;
  livePrice: PriceUpdate | null = null;
  confirming = false;
  submitting = false;
  resultBanner: { success: boolean; message: string } | null = null;

  orderTypes: ('Market' | 'Limit' | 'Stop')[] = ['Market', 'Limit', 'Stop'];

  tradeForm = {
    symbol: '',
    direction: 'Buy' as 'Buy' | 'Sell',
    orderType: 'Market' as 'Market' | 'Limit' | 'Stop',
    volume: 0.01,
    price: null as number | null,
    stopLoss: null as number | null,
    takeProfit: null as number | null,
  };

  private priceSub: Subscription | null = null;
  private subscribedSymbol: string | null = null;

  // Symbol autocomplete state
  symbolSearch = '';
  showSymbolDropdown = false;
  activeCategory = 'All';

  constructor(
    private http: HttpClient,
    private signalR: SignalRService,
    private elRef: ElementRef
  ) {}

  ngOnInit() {
    this.loadPositions();
    this.loadHistory();
  }

  ngOnDestroy() {
    this.cleanupPriceSub();
  }

  get priceStep(): number {
    if (!this.selectedSymbol) return 0.00001;
    return Math.pow(10, -this.selectedSymbol.digits);
  }

  get spread(): string {
    if (!this.livePrice || !this.selectedSymbol) return '-';
    const diff = this.livePrice.ask - this.livePrice.bid;
    return diff.toFixed(this.selectedSymbol.digits);
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

  openSymbolDropdown() {
    this.showSymbolDropdown = true;
    if (this.tradeForm.symbol) {
      this.symbolSearch = '';
    }
  }

  onSymbolSearchInput(event: Event) {
    const input = event.target as HTMLInputElement;
    this.symbolSearch = input.value;
    if (this.tradeForm.symbol) {
      this.tradeForm.symbol = '';
      this.selectedSymbol = null;
      this.cleanupPriceSub();
      this.livePrice = null;
    }
    this.showSymbolDropdown = true;
  }

  selectSymbol(s: SymbolInfo) {
    this.tradeForm.symbol = s.name;
    this.symbolSearch = '';
    this.showSymbolDropdown = false;
    this.onSymbolChange(s.name);
  }

  clearSymbol() {
    this.tradeForm.symbol = '';
    this.symbolSearch = '';
    this.selectedSymbol = null;
    this.cleanupPriceSub();
    this.livePrice = null;
    this.confirming = false;
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
      if (filtered.length === 1) {
        this.selectSymbol(filtered[0]);
      }
    }
  }

  toggleTradeForm() {
    this.showTradeForm = !this.showTradeForm;
    if (this.showTradeForm && this.symbols.length === 0) {
      this.loadSymbols();
    }
    if (!this.showTradeForm) {
      this.resetForm();
    }
  }

  loadSymbols() {
    this.http.get<SymbolInfo[]>(`${environment.apiUrl}/api/positions/symbols`).subscribe({
      next: (data) => { this.symbols = data; }
    });
  }

  onSymbolChange(symbol: string) {
    this.cleanupPriceSub();
    this.livePrice = null;
    this.confirming = false;

    this.selectedSymbol = this.symbols.find(s => s.name === symbol) ?? null;

    if (this.selectedSymbol) {
      this.tradeForm.volume = this.selectedSymbol.minVolume;
    }

    if (symbol) {
      this.signalR.subscribeToSymbol(symbol);
      this.subscribedSymbol = symbol;
      this.priceSub = this.signalR.priceUpdates$.subscribe(prices => {
        const price = prices.get(symbol);
        if (price) {
          this.livePrice = price;
        }
      });
    }
  }

  isFormValid(): boolean {
    if (!this.tradeForm.symbol || !this.tradeForm.direction || this.tradeForm.volume <= 0) {
      return false;
    }
    if (this.tradeForm.orderType !== 'Market' && !this.tradeForm.price) {
      return false;
    }
    return true;
  }

  submitOrder() {
    this.submitting = true;
    this.resultBanner = null;

    const body: any = {
      symbol: this.tradeForm.symbol,
      direction: this.tradeForm.direction,
      orderType: this.tradeForm.orderType,
      volume: this.tradeForm.volume,
    };
    if (this.tradeForm.orderType !== 'Market' && this.tradeForm.price) {
      body.price = this.tradeForm.price;
    }
    if (this.tradeForm.stopLoss) {
      body.stopLoss = this.tradeForm.stopLoss;
    }
    if (this.tradeForm.takeProfit) {
      body.takeProfit = this.tradeForm.takeProfit;
    }

    this.http.post<OrderResult>(`${environment.apiUrl}/api/positions/open`, body).subscribe({
      next: (result) => {
        this.submitting = false;
        this.confirming = false;
        if (result.success) {
          this.resultBanner = {
            success: true,
            message: `Order placed successfully. Position ID: ${result.positionId ?? result.orderId ?? '-'}`
          };
          this.showTradeForm = false;
          this.resetForm();
          this.loadPositions();
        } else {
          this.resultBanner = {
            success: false,
            message: result.errorMessage ?? 'Order failed'
          };
        }
      },
      error: (err) => {
        this.submitting = false;
        this.confirming = false;
        this.resultBanner = {
          success: false,
          message: err.error?.error ?? err.message ?? 'Order failed'
        };
      }
    });
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

  private resetForm() {
    this.tradeForm = {
      symbol: '',
      direction: 'Buy',
      orderType: 'Market',
      volume: 0.01,
      price: null,
      stopLoss: null,
      takeProfit: null,
    };
    this.selectedSymbol = null;
    this.livePrice = null;
    this.confirming = false;
    this.submitting = false;
    this.symbolSearch = '';
    this.showSymbolDropdown = false;
    this.activeCategory = 'All';
    this.cleanupPriceSub();
  }

  private cleanupPriceSub() {
    if (this.priceSub) {
      this.priceSub.unsubscribe();
      this.priceSub = null;
    }
    if (this.subscribedSymbol) {
      this.signalR.unsubscribeFromSymbol(this.subscribedSymbol);
      this.subscribedSymbol = null;
    }
  }
}
