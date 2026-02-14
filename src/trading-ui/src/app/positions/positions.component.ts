import { Component, OnInit, OnDestroy, HostListener, ElementRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { Subscription } from 'rxjs';
import { environment } from '../../environments/environment';
import { SignalRService, PriceUpdate } from '../shared/services/signalr.service';
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
          <div class="table-scroll">
            <table>
              <thead>
                <tr>
                  <th>Symbol</th>
                  <th>Direction</th>
                  <th>Volume</th>
                  <th>Entry</th>
                  <th>Current</th>
                  <th>SL / TP</th>
                  <th>P&L</th>
                  <th class="th-actions">Actions</th>
                </tr>
              </thead>
              <tbody>
                @for (pos of positions; track pos.id) {
                  <tr [class.row-expanded]="editingPositionId === pos.id">
                    <td data-label="Symbol">
                      <span class="symbol-badge">{{ pos.symbol }}</span>
                    </td>
                    <td data-label="Direction">
                      <span class="dir-badge" [class.dir-buy]="pos.direction === 'Buy'" [class.dir-sell]="pos.direction === 'Sell'">
                        <svg viewBox="0 0 16 16" fill="currentColor" class="dir-icon">
                          @if (pos.direction === 'Buy') {
                            <path d="M8 3l5 8H3z"/>
                          } @else {
                            <path d="M8 13l5-8H3z"/>
                          }
                        </svg>
                        {{ pos.direction }}
                      </span>
                    </td>
                    <td data-label="Volume">
                      <span class="mono">{{ pos.volume }}</span>
                    </td>
                    <td data-label="Entry">
                      <span class="mono">{{ pos.entryPrice }}</span>
                    </td>
                    <td data-label="Current">
                      <span class="mono">{{ pos.currentPrice }}</span>
                    </td>
                    <td data-label="SL / TP">
                      <div class="sl-tp-cell">
                        <span class="sl-val" [class.sl-set]="pos.stopLoss">
                          {{ pos.stopLoss || '—' }}
                        </span>
                        <span class="sl-tp-sep">/</span>
                        <span class="tp-val" [class.tp-set]="pos.takeProfit">
                          {{ pos.takeProfit || '—' }}
                        </span>
                      </div>
                    </td>
                    <td data-label="P&L">
                      <span class="pnl-badge" [class.pnl-pos]="pos.unrealizedPnL > 0" [class.pnl-neg]="pos.unrealizedPnL < 0">
                        {{ pos.unrealizedPnL >= 0 ? '+' : '' }}{{ pos.unrealizedPnL | currency }}
                      </span>
                    </td>
                    <td data-label="Actions" class="actions-cell">
                      <div class="action-btns">
                        <button
                          class="act-btn act-modify"
                          [class.act-active]="editingPositionId === pos.id"
                          (click)="editingPositionId === pos.id ? cancelModify() : startModify(pos)"
                          title="Modify SL/TP">
                          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                            <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/>
                            <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"/>
                          </svg>
                        </button>
                        <button
                          class="act-btn act-close"
                          (click)="closePosition(pos.id)"
                          title="Close position">
                          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                            <line x1="18" y1="6" x2="6" y2="18"/>
                            <line x1="6" y1="6" x2="18" y2="18"/>
                          </svg>
                        </button>
                      </div>
                    </td>
                  </tr>
                  @if (editingPositionId === pos.id) {
                    <tr class="edit-row">
                      <td colspan="8">
                        <div class="edit-panel">
                          <div class="edit-field">
                            <label>Stop Loss</label>
                            <div class="edit-input-wrap">
                              <input type="number" [(ngModel)]="editSL" [step]="0.00001" placeholder="Not set" />
                              @if (editSL) {
                                <button class="edit-clear" (click)="editSL = null">&times;</button>
                              }
                            </div>
                          </div>
                          <div class="edit-field">
                            <label>Take Profit</label>
                            <div class="edit-input-wrap">
                              <input type="number" [(ngModel)]="editTP" [step]="0.00001" placeholder="Not set" />
                              @if (editTP) {
                                <button class="edit-clear" (click)="editTP = null">&times;</button>
                              }
                            </div>
                          </div>
                          <div class="edit-actions">
                            <button class="edit-save" (click)="saveModify(pos.id)" [disabled]="modifying">
                              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round">
                                <polyline points="20 6 9 17 4 12"/>
                              </svg>
                              {{ modifying ? 'Saving...' : 'Save Changes' }}
                            </button>
                            <button class="edit-cancel" (click)="cancelModify()">Cancel</button>
                          </div>
                        </div>
                      </td>
                    </tr>
                  }
                }
              </tbody>
            </table>
          </div>
        }
      </div>

      <div class="card" style="margin-top: 20px;">
        <h3>Position History</h3>
        @if (history.length === 0) {
          <p class="empty-state">No closed positions</p>
        } @else {
          <div class="table-scroll">
            <table>
              <thead>
                <tr>
                  <th>Symbol</th>
                  <th>Direction</th>
                  <th>Volume (lots)</th>
                  <th>Notional</th>
                  <th>Entry</th>
                  <th>Exit</th>
                  <th>P&L</th>
                  <th>Closed At</th>
                </tr>
              </thead>
              <tbody>
                @for (pos of history; track pos.id) {
                  <tr>
                    <td data-label="Symbol">{{ pos.symbol }}</td>
                    <td data-label="Direction">{{ pos.direction }}</td>
                    <td data-label="Volume">{{ pos.volume }}</td>
                    <td data-label="Notional">{{ pos.notionalUsd | currency }}</td>
                    <td data-label="Entry">{{ pos.entryPrice }}</td>
                    <td data-label="Exit">{{ pos.closePrice }}</td>
                    <td data-label="P&L" [class.positive]="pos.realizedPnL > 0" [class.negative]="pos.realizedPnL < 0">
                      {{ pos.realizedPnL | currency }}
                    </td>
                    <td data-label="Closed At">{{ pos.closeTime | date:'short' }}</td>
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

    /* Symbol & Direction badges */
    .symbol-badge {
      font-weight: 700;
      font-family: var(--font-mono);
      font-size: 13px;
      color: var(--text-bright);
    }
    .dir-badge {
      display: inline-flex;
      align-items: center;
      gap: 4px;
      font-weight: 600;
      font-size: 12px;
      padding: 3px 8px;
      border-radius: 4px;
    }
    .dir-icon {
      width: 10px;
      height: 10px;
    }
    .dir-buy {
      color: #22c55e;
      background: rgba(34, 197, 94, 0.1);
    }
    .dir-sell {
      color: #ef4444;
      background: rgba(239, 68, 68, 0.1);
    }
    .mono {
      font-family: var(--font-mono);
      font-size: 13px;
    }

    /* SL / TP cell */
    .sl-tp-cell {
      display: flex;
      align-items: center;
      gap: 4px;
      font-family: var(--font-mono);
      font-size: 12px;
    }
    .sl-tp-sep {
      color: var(--text-muted);
      opacity: 0.4;
    }
    .sl-val { color: var(--text-muted); }
    .tp-val { color: var(--text-muted); }
    .sl-val.sl-set { color: #ef4444; }
    .tp-val.tp-set { color: #22c55e; }

    /* PnL badge */
    .pnl-badge {
      font-family: var(--font-mono);
      font-weight: 600;
      font-size: 13px;
      padding: 2px 6px;
      border-radius: 4px;
    }
    .pnl-pos {
      color: #22c55e;
      background: rgba(34, 197, 94, 0.08);
    }
    .pnl-neg {
      color: #ef4444;
      background: rgba(239, 68, 68, 0.08);
    }

    /* Action buttons */
    .th-actions {
      width: 90px;
      text-align: center;
    }
    .actions-cell {
      text-align: center;
    }
    .action-btns {
      display: flex;
      align-items: center;
      justify-content: center;
      gap: 4px;
    }
    .act-btn {
      display: flex;
      align-items: center;
      justify-content: center;
      width: 32px;
      height: 32px;
      border-radius: 6px;
      border: 1px solid var(--border);
      background: var(--surface-light);
      color: var(--text-muted);
      cursor: pointer;
      transition: all 0.15s;
      padding: 0;
    }
    .act-btn svg {
      width: 15px;
      height: 15px;
    }
    .act-modify:hover, .act-modify.act-active {
      background: rgba(59, 130, 246, 0.1);
      border-color: rgba(59, 130, 246, 0.4);
      color: #3b82f6;
    }
    .act-close:hover {
      background: rgba(239, 68, 68, 0.1);
      border-color: rgba(239, 68, 68, 0.4);
      color: #ef4444;
    }
    .row-expanded {
      border-bottom: none !important;
    }
    .row-expanded td {
      border-bottom: none !important;
    }

    /* Edit row */
    .edit-row td {
      padding: 0 !important;
      border-top: none !important;
    }
    .edit-panel {
      display: flex;
      align-items: flex-end;
      gap: 16px;
      padding: 12px 16px 16px;
      background: var(--surface-light);
      border-top: 1px dashed var(--border);
    }
    .edit-field {
      display: flex;
      flex-direction: column;
      gap: 4px;
      flex: 0 0 160px;
    }
    .edit-field label {
      font-size: 11px;
      font-weight: 600;
      color: var(--text-muted);
      text-transform: uppercase;
      letter-spacing: 0.04em;
    }
    .edit-input-wrap {
      position: relative;
      display: flex;
      align-items: center;
    }
    .edit-input-wrap input {
      width: 100%;
      padding: 7px 28px 7px 10px;
      font-size: 13px;
      font-family: var(--font-mono);
      background: var(--surface);
      border: 1px solid var(--border);
      border-radius: var(--radius);
      color: var(--text);
    }
    .edit-input-wrap input:focus {
      outline: none;
      border-color: var(--primary);
    }
    .edit-clear {
      position: absolute;
      right: 6px;
      background: none;
      border: none;
      color: var(--text-muted);
      font-size: 16px;
      cursor: pointer;
      padding: 0 4px;
      line-height: 1;
    }
    .edit-clear:hover { color: var(--text); }
    .edit-actions {
      display: flex;
      gap: 8px;
      margin-left: auto;
    }
    .edit-save {
      display: inline-flex;
      align-items: center;
      gap: 6px;
      padding: 7px 16px;
      font-size: 13px;
      font-weight: 600;
      background: var(--primary);
      color: white;
      border: none;
      border-radius: var(--radius);
      cursor: pointer;
      transition: background 0.15s;
    }
    .edit-save svg {
      width: 14px;
      height: 14px;
    }
    .edit-save:hover { background: #2563eb; }
    .edit-save:disabled {
      opacity: 0.5;
      cursor: not-allowed;
    }
    .edit-cancel {
      padding: 7px 14px;
      font-size: 13px;
      font-weight: 500;
      background: var(--surface);
      color: var(--text-muted);
      border: 1px solid var(--border);
      border-radius: var(--radius);
      cursor: pointer;
      transition: all 0.15s;
    }
    .edit-cancel:hover {
      background: var(--surface-light);
      color: var(--text);
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

    @media (max-width: 768px) {
      .form-grid {
        grid-template-columns: 1fr;
      }

      .live-price-bar {
        gap: 16px;
      }

      .submit-btn {
        width: 100%;
      }

      .form-actions {
        justify-content: stretch;
      }

      .confirm-buttons {
        flex-direction: column;
      }

      .confirm-buttons button {
        width: 100%;
      }

      /* Mobile action buttons: full-width with text labels */
      .action-btns {
        display: flex;
        gap: 8px;
        width: 100%;
      }
      .act-btn {
        width: auto;
        height: 40px;
        flex: 1;
        border-radius: var(--radius);
        font-size: 13px;
        font-weight: 600;
        gap: 6px;
      }
      .act-btn svg {
        width: 14px;
        height: 14px;
      }
      .act-modify::after {
        content: 'Modify';
      }
      .act-close::after {
        content: 'Close';
      }
      .act-modify {
        border-color: rgba(59, 130, 246, 0.3);
        color: #3b82f6;
        background: rgba(59, 130, 246, 0.08);
      }
      .act-close {
        border-color: rgba(239, 68, 68, 0.3);
        color: #ef4444;
        background: rgba(239, 68, 68, 0.08);
      }

      /* Edit row: merge with position card above */
      .row-expanded {
        border-bottom-left-radius: 0 !important;
        border-bottom-right-radius: 0 !important;
        margin-bottom: 0 !important;
        border-bottom: none !important;
      }
      .edit-row {
        display: block !important;
        border: 1px solid var(--border) !important;
        border-top: none !important;
        border-radius: 0 0 var(--radius-lg) var(--radius-lg) !important;
        margin-bottom: 12px !important;
        padding: 0 !important;
        background: var(--surface) !important;
      }
      .edit-row td {
        display: block !important;
        padding: 0 !important;
        border: none !important;
      }
      .edit-row td::before {
        display: none !important;
      }
      .edit-panel {
        flex-direction: column;
        align-items: stretch;
        border-top: 1px dashed var(--border);
        border-radius: 0 0 var(--radius-lg) var(--radius-lg);
      }
      .edit-field {
        flex: 1 1 auto;
      }
      .edit-actions {
        margin-left: 0;
        flex-direction: row;
      }
      .edit-save, .edit-cancel {
        flex: 1;
        text-align: center;
        justify-content: center;
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

      .live-price-bar {
        gap: 12px;
        padding: 10px 12px;
      }

      .price-value {
        font-size: 14px;
      }
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

  editingPositionId: number | null = null;
  editSL: number | null = null;
  editTP: number | null = null;
  modifying = false;

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
  private positionSub: Subscription | null = null;
  private positionPriceSubs: Subscription[] = [];
  private positionSubscribedSymbols: string[] = [];

  // Symbol autocomplete state
  symbolSearch = '';
  showSymbolDropdown = false;
  activeCategory = 'All';

  constructor(
    private http: HttpClient,
    private signalR: SignalRService,
    private elRef: ElementRef,
    private dialog: ConfirmDialogService
  ) {}

  ngOnInit() {
    this.loadPositions();
    this.loadHistory();

    // Subscribe to live position updates from SignalR
    // Note: SignalR sends cTraderPositionId, not DB id
    this.positionSub = this.signalR.positions$.subscribe(updates => {
      for (const update of updates) {
        const pos = this.positions.find(p => p.cTraderPositionId === update.positionId);
        if (pos) {
          pos.currentPrice = update.currentPrice;
          pos.unrealizedPnL = update.pnL;
        }
      }
    });
  }

  ngOnDestroy() {
    this.cleanupPriceSub();
    this.positionSub?.unsubscribe();
    this.cleanupPositionSubs();
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
      next: (data) => { this.symbols = data; },
      error: () => {}
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
        this.subscribeToPositionPrices();
      },
      error: () => {
        this.loading = false;
      }
    });
  }

  private subscribeToPositionPrices() {
    // Clean up previous position price subscriptions
    this.cleanupPositionSubs();

    // Get unique symbols from open positions
    const symbols = [...new Set(this.positions.map(p => p.symbol))];

    for (const symbol of symbols) {
      this.signalR.subscribeToSymbol(symbol);
      this.positionSubscribedSymbols.push(symbol);
    }

    if (symbols.length > 0) {
      const sub = this.signalR.priceUpdates$.subscribe(prices => {
        for (const pos of this.positions) {
          const price = prices.get(pos.symbol);
          if (price) {
            pos.currentPrice = pos.direction === 'Buy' ? price.bid : price.ask;
          }
        }
      });
      this.positionPriceSubs.push(sub);
    }
  }

  private cleanupPositionSubs() {
    for (const sub of this.positionPriceSubs) {
      sub.unsubscribe();
    }
    this.positionPriceSubs = [];
    for (const sym of this.positionSubscribedSymbols) {
      // Only unsubscribe if it's not the currently-selected trade form symbol
      if (sym !== this.subscribedSymbol) {
        this.signalR.unsubscribeFromSymbol(sym);
      }
    }
    this.positionSubscribedSymbols = [];
  }

  loadHistory() {
    this.http.get<any[]>(`${environment.apiUrl}/api/positions/history`).subscribe({
      next: (data) => {
        this.history = data;
      },
      error: () => {}
    });
  }

  async closePosition(id: number) {
    const ok = await this.dialog.confirm({
      title: 'Close Position',
      message: 'Are you sure you want to close this position?',
      confirmText: 'Close',
      variant: 'danger'
    });
    if (!ok) return;
    this.http.post(`${environment.apiUrl}/api/positions/${id}/close`, {}).subscribe({
      next: () => {
        this.loadPositions();
        this.loadHistory();
      },
      error: () => {}
    });
  }

  startModify(pos: any) {
    this.editingPositionId = pos.id;
    this.editSL = pos.stopLoss || null;
    this.editTP = pos.takeProfit || null;
  }

  cancelModify() {
    this.editingPositionId = null;
    this.editSL = null;
    this.editTP = null;
  }

  saveModify(id: number) {
    this.modifying = true;
    this.http.post(`${environment.apiUrl}/api/positions/${id}/modify`, {
      stopLoss: this.editSL || null,
      takeProfit: this.editTP || null
    }).subscribe({
      next: () => {
        this.modifying = false;
        this.editingPositionId = null;
        this.resultBanner = { success: true, message: 'Position modified successfully' };
        this.loadPositions();
      },
      error: (err) => {
        this.modifying = false;
        this.resultBanner = {
          success: false,
          message: err.error?.error ?? err.error ?? 'Failed to modify position'
        };
      }
    });
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
