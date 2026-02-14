import { Component, Input, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { PriceUpdate } from '../shared/services/signalr.service';
import { SymbolInfo } from './symbol-bar.component';

export interface OrderForm {
  symbol: string;
  direction: 'Buy' | 'Sell';
  orderType: 'Market' | 'Limit' | 'Stop';
  volume: number;
  price: number | null;
  stopLoss: number | null;
  takeProfit: number | null;
}

@Component({
  selector: 'app-trade-panel',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="trade-panel">
      <h3 class="panel-title">Trade</h3>

      <!-- Market Session -->
      @if (symbolInfo) {
        <div class="session-bar" [class.session-open]="session.isOpen" [class.session-closed]="!session.isOpen">
          <span class="session-dot" [class.dot-open]="session.isOpen" [class.dot-closed]="!session.isOpen"></span>
          <span class="session-label">{{ session.isOpen ? 'Market Open' : 'Market Closed' }}</span>
          <span class="session-detail">{{ session.detail }}</span>
        </div>
      }

      <!-- Live Price -->
      @if (currentPrice) {
        <div class="price-display">
          <div class="price-row">
            <span class="price-label">Bid</span>
            <span class="price-val sell-color">{{ currentPrice.bid.toFixed(symbolInfo?.digits ?? 5) }}</span>
          </div>
          <div class="price-row">
            <span class="price-label">Spread</span>
            <span class="price-val">{{ spread }}</span>
          </div>
          <div class="price-row">
            <span class="price-label">Ask</span>
            <span class="price-val buy-color">{{ currentPrice.ask.toFixed(symbolInfo?.digits ?? 5) }}</span>
          </div>
        </div>
      }

      <!-- Direction -->
      <div class="direction-toggle">
        <button
          class="dir-btn buy-btn"
          [class.active]="orderForm.direction === 'Buy'"
          (click)="setDirection('Buy')">
          BUY
        </button>
        <button
          class="dir-btn sell-btn"
          [class.active]="orderForm.direction === 'Sell'"
          (click)="setDirection('Sell')">
          SELL
        </button>
      </div>

      <!-- Volume -->
      <div class="form-group">
        <label>Volume (lots)</label>
        <div class="volume-control">
          <button class="vol-btn" (click)="stepVolume(-1)">&minus;</button>
          <input
            type="number"
            [ngModel]="orderForm.volume"
            (ngModelChange)="updateField('volume', $event)"
            [min]="symbolInfo?.minVolume ?? 0.01"
            [max]="symbolInfo?.maxVolume ?? 100"
            [step]="symbolInfo?.volumeStep ?? 0.01" />
          <button class="vol-btn" (click)="stepVolume(1)">+</button>
        </div>
        @if (symbolInfo) {
          <span class="hint">
            Min: {{ symbolInfo.minVolume }} &middot;
            Max: {{ symbolInfo.maxVolume }} &middot;
            Step: {{ symbolInfo.volumeStep }}
          </span>
        }
      </div>

      <!-- Order Type -->
      <div class="form-group">
        <label>Type</label>
        <div class="type-selector">
          @for (type of orderTypes; track type) {
            <button
              class="type-btn"
              [class.active]="orderForm.orderType === type"
              (click)="updateField('orderType', type)">
              {{ type }}
            </button>
          }
        </div>
      </div>

      <!-- Price (non-Market) -->
      @if (orderForm.orderType !== 'Market') {
        <div class="form-group">
          <label>Price</label>
          <input
            type="number"
            [ngModel]="orderForm.price"
            (ngModelChange)="updateField('price', $event)"
            [step]="priceStep"
            placeholder="Enter price" />
        </div>
      }

      <!-- SL / TP -->
      <div class="sl-tp-row">
        <div class="form-group">
          <label>Stop Loss</label>
          <input
            type="number"
            [ngModel]="orderForm.stopLoss"
            (ngModelChange)="updateField('stopLoss', $event)"
            [step]="priceStep"
            placeholder="SL" />
        </div>
        <div class="form-group">
          <label>Take Profit</label>
          <input
            type="number"
            [ngModel]="orderForm.takeProfit"
            (ngModelChange)="updateField('takeProfit', $event)"
            [step]="priceStep"
            placeholder="TP" />
        </div>
      </div>

      <!-- Submit -->
      <button
        class="submit-btn"
        [class.buy-submit]="orderForm.direction === 'Buy' && isValid"
        [class.sell-submit]="orderForm.direction === 'Sell' && isValid"
        [disabled]="!isValid || submitting"
        (click)="orderSubmitted.emit()">
        {{ submitLabel }}
      </button>
    </div>
  `,
  styles: [`
    .trade-panel {
      display: flex;
      flex-direction: column;
      gap: 12px;
    }

    .panel-title {
      font-size: 14px;
      font-weight: 700;
      color: var(--text-bright);
      margin: 0;
    }

    .session-bar {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 8px 10px;
      border-radius: var(--radius);
      font-size: 11px;
      flex-wrap: wrap;
    }
    .session-open {
      background: rgba(34, 197, 94, 0.08);
      border: 1px solid rgba(34, 197, 94, 0.25);
    }
    .session-closed {
      background: rgba(239, 68, 68, 0.08);
      border: 1px solid rgba(239, 68, 68, 0.25);
    }
    .session-dot {
      width: 7px;
      height: 7px;
      border-radius: 50%;
      flex-shrink: 0;
    }
    .dot-open { background: #22c55e; box-shadow: 0 0 6px rgba(34,197,94,0.5); }
    .dot-closed { background: #ef4444; }
    .session-label {
      font-weight: 700;
      color: var(--text-bright);
    }
    .session-detail {
      color: var(--text-muted);
    }

    .price-display {
      display: flex;
      justify-content: space-between;
      padding: 10px 12px;
      background: var(--surface-light);
      border-radius: var(--radius);
      border: 1px solid var(--border);
    }
    .price-row {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 2px;
    }
    .price-label {
      font-size: 10px;
      color: var(--text-muted);
      text-transform: uppercase;
      letter-spacing: 0.05em;
    }
    .price-val {
      font-family: var(--font-mono);
      font-size: 14px;
      font-weight: 600;
    }
    .buy-color { color: #22c55e; }
    .sell-color { color: #ef4444; }

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
      font-weight: 700;
      font-size: 14px;
      background: var(--surface-light);
      color: var(--text-muted);
      border: none;
      border-radius: 0;
      cursor: pointer;
      transition: all 0.2s;
    }
    .buy-btn.active {
      background: #22c55e;
      color: white;
      box-shadow: 0 0 12px rgba(34, 197, 94, 0.3);
    }
    .sell-btn.active {
      background: #ef4444;
      color: white;
      box-shadow: 0 0 12px rgba(239, 68, 68, 0.3);
    }

    .form-group {
      display: flex;
      flex-direction: column;
      gap: 4px;
    }
    .form-group label {
      font-size: 11px;
      font-weight: 600;
      color: var(--text-muted);
      text-transform: uppercase;
      letter-spacing: 0.04em;
    }
    .form-group input {
      padding: 8px 10px;
      font-size: 13px;
      background: var(--surface-light);
      border: 1px solid var(--border);
      border-radius: var(--radius);
      color: var(--text);
    }
    .hint {
      font-size: 10px;
      color: var(--text-muted);
    }
    .form-group input:focus {
      outline: none;
      border-color: var(--primary);
    }

    .volume-control {
      display: flex;
      align-items: stretch;
      border: 1px solid var(--border);
      border-radius: var(--radius);
      overflow: hidden;
    }
    .volume-control input {
      flex: 1;
      border: none;
      border-radius: 0;
      text-align: center;
      font-family: var(--font-mono);
      min-width: 0;
    }
    .vol-btn {
      padding: 8px 14px;
      font-size: 16px;
      font-weight: 700;
      background: var(--surface-light);
      color: var(--text-muted);
      border: none;
      cursor: pointer;
      transition: background 0.15s;
    }
    .vol-btn:hover {
      background: var(--surface);
      color: var(--text);
    }

    .type-selector {
      display: flex;
      gap: 0;
      border-radius: var(--radius);
      overflow: hidden;
      border: 1px solid var(--border);
    }
    .type-btn {
      flex: 1;
      padding: 7px;
      font-size: 12px;
      font-weight: 500;
      background: var(--surface-light);
      color: var(--text-muted);
      border: none;
      border-radius: 0;
      cursor: pointer;
      transition: all 0.2s;
    }
    .type-btn:not(:last-child) {
      border-right: 1px solid var(--border);
    }
    .type-btn.active {
      background: var(--primary);
      color: white;
    }

    .sl-tp-row {
      display: flex;
      flex-wrap: wrap;
      gap: 8px;
    }
    .sl-tp-row .form-group {
      flex: 1 1 100px;
    }

    .submit-btn {
      padding: 12px;
      font-size: 14px;
      font-weight: 700;
      border: none;
      border-radius: var(--radius);
      cursor: pointer;
      transition: all 0.2s;
      text-transform: uppercase;
      letter-spacing: 0.03em;
    }
    .submit-btn:disabled {
      opacity: 0.4;
      cursor: not-allowed;
    }
    .buy-submit {
      background: #22c55e;
      color: white;
    }
    .buy-submit:not(:disabled):hover {
      background: #16a34a;
    }
    .sell-submit {
      background: #ef4444;
      color: white;
    }
    .sell-submit:not(:disabled):hover {
      background: #dc2626;
    }
  `]
})
export class TradePanelComponent {
  @Input() symbolInfo: SymbolInfo | null = null;
  @Input() currentPrice: PriceUpdate | null = null;
  @Input() orderForm: OrderForm = {
    symbol: '', direction: 'Buy', orderType: 'Market',
    volume: 0.01, price: null, stopLoss: null, takeProfit: null,
  };
  @Input() submitting = false;
  @Output() orderFormChanged = new EventEmitter<OrderForm>();
  @Output() orderSubmitted = new EventEmitter<void>();

  orderTypes: ('Market' | 'Limit' | 'Stop')[] = ['Market', 'Limit', 'Stop'];

  private static readonly FOREX_SESSIONS = [
    { name: 'Sydney', open: 22, close: 7 },
    { name: 'Tokyo', open: 0, close: 9 },
    { name: 'London', open: 8, close: 17 },
    { name: 'New York', open: 13, close: 22 },
  ];

  get session(): { isOpen: boolean; detail: string } {
    if (!this.symbolInfo) return { isOpen: false, detail: '' };

    const cat = this.symbolInfo.category;
    if (cat === 'Crypto') {
      return { isOpen: true, detail: '24/7' };
    }

    const now = new Date();
    const utcDay = now.getUTCDay();
    const utcHour = now.getUTCHours();

    // Weekend: Fri 22:00 UTC → Sun 22:00 UTC
    const isWeekend =
      utcDay === 6 ||
      (utcDay === 0 && utcHour < 22) ||
      (utcDay === 5 && utcHour >= 22);

    if (isWeekend) {
      return { isOpen: false, detail: 'Opens Sun 22:00 UTC' };
    }

    const active: string[] = [];
    for (const s of TradePanelComponent.FOREX_SESSIONS) {
      const inSession = s.open < s.close
        ? utcHour >= s.open && utcHour < s.close
        : utcHour >= s.open || utcHour < s.close;
      if (inSession) active.push(s.name);
    }

    if (active.length === 0) {
      return { isOpen: false, detail: 'Between sessions' };
    }

    let label: string;
    if (active.includes('London') && active.includes('New York')) {
      label = 'London/NY Overlap';
    } else if (active.includes('Tokyo') && active.includes('London')) {
      label = 'Tokyo/London Overlap';
    } else {
      label = active.join(', ');
    }
    return { isOpen: true, detail: label };
  }

  get priceStep(): number {
    if (!this.symbolInfo) return 0.00001;
    return Math.pow(10, -this.symbolInfo.digits);
  }

  get spread(): string {
    if (!this.currentPrice || !this.symbolInfo) return '-';
    const diff = this.currentPrice.ask - this.currentPrice.bid;
    return diff.toFixed(this.symbolInfo.digits);
  }

  get isValid(): boolean {
    if (!this.orderForm.symbol || !this.symbolInfo) return false;
    if (this.orderForm.volume < this.symbolInfo.minVolume) return false;
    if (this.orderForm.volume > this.symbolInfo.maxVolume) return false;
    if (this.orderForm.orderType === 'Market' && !this.session.isOpen) return false;
    if (this.orderForm.orderType !== 'Market' && !this.orderForm.price) return false;
    return true;
  }

  get submitLabel(): string {
    if (this.submitting) return 'Placing...';
    if (!this.symbolInfo) return 'Loading...';
    if (this.orderForm.orderType === 'Market' && !this.session.isOpen) return 'Market Closed';
    return this.orderForm.direction + ' ' + this.orderForm.symbol;
  }

  setDirection(dir: 'Buy' | 'Sell') {
    this.updateField('direction', dir);
  }

  stepVolume(dir: number) {
    const step = this.symbolInfo?.volumeStep ?? 0.01;
    const newVol = Math.max(
      this.symbolInfo?.minVolume ?? 0.01,
      Math.min(
        this.symbolInfo?.maxVolume ?? 100,
        +(this.orderForm.volume + dir * step).toFixed(4)
      )
    );
    this.updateField('volume', newVol);
  }

  updateField(field: string, value: any) {
    this.orderFormChanged.emit({ ...this.orderForm, [field]: value });
  }
}
