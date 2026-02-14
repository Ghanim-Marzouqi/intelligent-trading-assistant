import { Component, DestroyRef, ElementRef, HostListener, inject, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { Subscription } from 'rxjs';
import { environment } from '../../environments/environment';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TradeChartComponent, CandleData, TradeLevels } from './trade-chart.component';
import { SignalRService } from '../shared/services/signalr.service';

interface SymbolInfo {
  name: string;
  minVolume: number;
  maxVolume: number;
  volumeStep: number;
  digits: number;
  category: string;
  description: string;
}

interface TradeSuggestion {
  orderType: string;
  direction: string;
  entry: number;
  stopLoss: number;
  takeProfit: number;
  lotSize: number;
  riskPercent: number;
  riskRewardRatio: number;
  pipsAtRisk: number;
  pipsToTarget: number;
  riskAmount: number;
  potentialReward: number;
  rationale: string;
  marginRequired?: number;
  leverageWarning?: string;
}

interface MarketSessionInfo {
  isMarketOpen: boolean;
  activeSessions: string[];
  primarySession: string;
  tradingAdvice: string;
  nextOpen?: string;
}

interface MarketAnalysis {
  pair: string;
  bias?: string;
  confidence: number;
  keyLevels?: { support: number; resistance: number };
  riskEvents?: string[];
  recommendation?: string;
  reasoning: string;
  trade?: TradeSuggestion;
  marketSession?: MarketSessionInfo;
}

interface TradeReview {
  tradeId: number;
  assessment: string;
  strengths?: string[];
  weaknesses?: string[];
  improvements?: string[];
  score: number;
}

@Component({
  selector: 'app-ai-analysis',
  standalone: true,
  imports: [CommonModule, FormsModule, TradeChartComponent],
  template: `
    <div class="ai-page">
      <header class="page-header">
        <h1>
          <svg class="header-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <path d="M12 2a4 4 0 0 1 4 4v1a1 1 0 0 1-1 1H9a1 1 0 0 1-1-1V6a4 4 0 0 1 4-4z"/><path d="M9 8v1a3 3 0 0 0 6 0V8"/><path d="M12 14v3"/><circle cx="12" cy="20" r="2"/><path d="M5 11h2"/><path d="M17 11h2"/>
          </svg>
          AI Analysis
        </h1>
      </header>

      <!-- Market Analysis Section -->
      <div class="card analysis-card">
        <div class="card-header">
          <svg class="section-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <polyline points="22 12 18 12 15 21 9 3 6 12 2 12"/>
          </svg>
          <h3>Market Analysis</h3>
        </div>

        <div class="form-row">
          <div class="symbol-autocomplete">
            <div class="symbol-input-wrap">
              <input
                type="text"
                class="input symbol-input"
                [value]="symbol || symbolSearch"
                (input)="onSymbolSearchInput($event)"
                (focus)="openSymbolDropdown()"
                (keydown)="onSymbolKeydown($event)"
                [placeholder]="symbol ? '' : 'Search symbol...'"
                autocomplete="off" />
              @if (symbol) {
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
          <select [(ngModel)]="timeframe" class="input select-tf">
            <option value="M15">M15</option>
            <option value="H1">H1</option>
            <option value="H4">H4</option>
            <option value="D1">D1</option>
          </select>
          <button (click)="analyze()" [disabled]="analyzing" class="btn-analyze">
            @if (analyzing) {
              <svg class="spinner" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <circle cx="12" cy="12" r="10" stroke-dasharray="31.4" stroke-dashoffset="10"/>
              </svg>
              Analyzing...
            } @else {
              <svg class="btn-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                <circle cx="11" cy="11" r="8"/><line x1="21" y1="21" x2="16.65" y2="16.65"/>
              </svg>
              Analyze
            }
          </button>
        </div>

        @if (analysisError) {
          <div class="error-banner">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="10"/><line x1="15" y1="9" x2="9" y2="15"/><line x1="9" y1="9" x2="15" y2="15"/></svg>
            {{ analysisError }}
          </div>
        }

        @if (analysis) {
          <div class="analysis-result">

            <!-- Session Bar -->
            @if (analysis.marketSession) {
              <div class="session-bar" [class.session-open]="analysis.marketSession.isMarketOpen" [class.session-closed]="!analysis.marketSession.isMarketOpen">
                <div class="session-status">
                  <span class="session-dot" [class.dot-open]="analysis.marketSession.isMarketOpen" [class.dot-closed]="!analysis.marketSession.isMarketOpen"></span>
                  <span class="session-label">{{ analysis.marketSession.isMarketOpen ? 'Market Open' : 'Market Closed' }}</span>
                </div>
                <span class="session-primary">{{ analysis.marketSession.primarySession }}</span>
                <span class="session-advice">{{ analysis.marketSession.tradingAdvice }}</span>
                @if (!analysis.marketSession.isMarketOpen && analysis.marketSession.nextOpen) {
                  <span class="session-next">Opens: {{ analysis.marketSession.nextOpen | date:'EEE HH:mm' }} UTC</span>
                }
              </div>
            }

            <!-- Candlestick Chart -->
            @if (candles.length > 0) {
              <div class="chart-wrapper">
                <app-trade-chart [candles]="candles" [tradeLevels]="tradeLevels" [livePrice]="liveChartPrice" [timeframe]="timeframe"></app-trade-chart>
              </div>
            } @else if (loadingCandles) {
              <div class="chart-loading">
                <svg class="spinner" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <circle cx="12" cy="12" r="10" stroke-dasharray="31.4" stroke-dashoffset="10"/>
                </svg>
                Loading chart data...
              </div>
            }

            <!-- Header Row: Symbol, Bias Badge, Confidence, Recommendation -->
            <div class="result-header">
              <div class="symbol-block">
                <span class="pair">{{ analysis.pair }}</span>
                <span class="timeframe-tag">{{ timeframe }}</span>
              </div>
              <div class="badges">
                <span class="badge bias-badge" [class]="'bias-' + analysis.bias?.toLowerCase()">
                  @if (analysis.bias?.toLowerCase() === 'bullish') {
                    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><polyline points="18 15 12 9 6 15"/></svg>
                  } @else if (analysis.bias?.toLowerCase() === 'bearish') {
                    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><polyline points="6 9 12 15 18 9"/></svg>
                  } @else {
                    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><line x1="5" y1="12" x2="19" y2="12"/></svg>
                  }
                  {{ analysis.bias }}
                </span>
                <span class="badge rec-badge" [class]="'rec-' + analysis.recommendation?.toLowerCase()">
                  {{ analysis.recommendation }}
                </span>
              </div>
            </div>

            <!-- Confidence Gauge -->
            <div class="confidence-row">
              <span class="confidence-label">Confidence</span>
              <div class="confidence-bar">
                <div class="confidence-fill"
                     [style.width.%]="analysis.confidence * 100"
                     [class]="'fill-' + getConfidenceLevel(analysis.confidence)">
                </div>
              </div>
              <span class="confidence-value">{{ (analysis.confidence * 100) | number:'1.0-0' }}%</span>
            </div>

            <!-- Key Levels -->
            <div class="levels-row">
              <div class="level-card support">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="6 9 12 15 18 9"/></svg>
                <div>
                  <span class="level-label">Support</span>
                  <span class="level-value mono">{{ analysis.keyLevels?.support | number:'1.5-5' }}</span>
                </div>
              </div>
              <div class="level-card resistance">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="18 15 12 9 6 15"/></svg>
                <div>
                  <span class="level-label">Resistance</span>
                  <span class="level-value mono">{{ analysis.keyLevels?.resistance | number:'1.5-5' }}</span>
                </div>
              </div>
            </div>

            <!-- Trade Suggestion Panel -->
            @if (analysis.trade) {
              <div class="trade-panel">
                <div class="trade-panel-header">
                  <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                    <polyline points="22 7 13.5 15.5 8.5 10.5 2 17"/><polyline points="16 7 22 7 22 13"/>
                  </svg>
                  <h4>Trade Suggestion</h4>
                  <span class="trade-type-badge">{{ analysis.trade.orderType }}</span>
                  <span class="trade-dir-badge"
                        [class.dir-buy]="analysis.trade.direction.toLowerCase() === 'buy'"
                        [class.dir-sell]="analysis.trade.direction.toLowerCase() === 'sell'">
                    {{ analysis.trade.direction }}
                  </span>
                </div>

                <div class="trade-levels-grid">
                  <div class="trade-level entry-level">
                    <span class="trade-level-label">Entry</span>
                    <span class="trade-level-value mono">{{ analysis.trade.entry | number:'1.5-5' }}</span>
                  </div>
                  <div class="trade-level sl-level">
                    <span class="trade-level-label">Stop Loss</span>
                    <span class="trade-level-value mono">{{ analysis.trade.stopLoss | number:'1.5-5' }}</span>
                    <span class="trade-level-pips">{{ analysis.trade.pipsAtRisk | number:'1.1-1' }} pips</span>
                  </div>
                  <div class="trade-level tp-level">
                    <span class="trade-level-label">Take Profit</span>
                    <span class="trade-level-value mono">{{ analysis.trade.takeProfit | number:'1.5-5' }}</span>
                    <span class="trade-level-pips">{{ analysis.trade.pipsToTarget | number:'1.1-1' }} pips</span>
                  </div>
                </div>

                <div class="trade-meta-row">
                  <div class="trade-meta-item">
                    <span class="meta-label">Lot Size</span>
                    <span class="meta-value mono">{{ analysis.trade.lotSize | number:'1.2-2' }}</span>
                  </div>
                  <div class="trade-meta-item">
                    <span class="meta-label">Risk</span>
                    <span class="meta-value">{{ analysis.trade.riskPercent }}% (\${{ analysis.trade.riskAmount | number:'1.2-2' }})</span>
                  </div>
                  <div class="trade-meta-item">
                    <span class="meta-label">R:R Ratio</span>
                    <span class="meta-value mono">1:{{ analysis.trade.riskRewardRatio | number:'1.1-1' }}</span>
                  </div>
                  <div class="trade-meta-item">
                    <span class="meta-label">Potential</span>
                    <span class="meta-value positive">\${{ analysis.trade.potentialReward | number:'1.2-2' }}</span>
                  </div>
                </div>

                @if (analysis.trade.marginRequired) {
                  <div class="margin-info-row">
                    <div class="margin-info-item">
                      <span class="meta-label">Margin Required</span>
                      <span class="meta-value mono">\${{ analysis.trade.marginRequired | number:'1.2-2' }}</span>
                    </div>
                    @if (analysis.trade.leverageWarning) {
                      <div class="leverage-warning">
                        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"/><line x1="12" y1="9" x2="12" y2="13"/><line x1="12" y1="17" x2="12.01" y2="17"/></svg>
                        {{ analysis.trade.leverageWarning }}
                      </div>
                    }
                  </div>
                }

                @if (analysis.trade.rationale) {
                  <div class="trade-rationale">{{ analysis.trade.rationale }}</div>
                }

                @if (tradeResult) {
                  <div class="trade-result" [class.trade-success]="tradeResult.success" [class.trade-error]="!tradeResult.success">
                    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                      @if (tradeResult.success) {
                        <polyline points="20 6 9 17 4 12"/>
                      } @else {
                        <circle cx="12" cy="12" r="10"/><line x1="15" y1="9" x2="9" y2="15"/><line x1="9" y1="9" x2="15" y2="15"/>
                      }
                    </svg>
                    {{ tradeResult.message }}
                  </div>
                }

                <div class="trade-actions">
                  @if (showTradeForm) {
                    <div class="trade-form">
                      <div class="trade-form-header">
                        <h5>Adjust & Confirm Order</h5>
                        <button class="btn-close-form" (click)="cancelTrade()">&times;</button>
                      </div>
                      <div class="trade-form-grid">
                        <div class="form-field">
                          <label>Direction</label>
                          <select [(ngModel)]="tradeForm.direction" class="form-input">
                            <option value="Buy">Buy</option>
                            <option value="Sell">Sell</option>
                          </select>
                        </div>
                        <div class="form-field">
                          <label>Order Type</label>
                          <select [(ngModel)]="tradeForm.orderType" class="form-input">
                            <option value="Market">Market</option>
                            <option value="Limit">Limit</option>
                            <option value="Stop">Stop</option>
                          </select>
                        </div>
                        <div class="form-field">
                          <label>Volume (lots)</label>
                          <input type="number" [(ngModel)]="tradeForm.volume" class="form-input mono" step="0.01" min="0.01" />
                        </div>
                        <div class="form-field">
                          <label>Entry Price</label>
                          <input type="number" [(ngModel)]="tradeForm.entry" class="form-input mono" step="0.00001" />
                        </div>
                        <div class="form-field">
                          <label>Stop Loss</label>
                          <input type="number" [(ngModel)]="tradeForm.stopLoss" class="form-input mono" step="0.00001" />
                        </div>
                        <div class="form-field">
                          <label>Take Profit</label>
                          <input type="number" [(ngModel)]="tradeForm.takeProfit" class="form-input mono" step="0.00001" />
                        </div>
                      </div>
                      <div class="trade-form-actions">
                        @if (submittingTrade) {
                          <button class="btn-place-order" disabled>
                            <svg class="spinner" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                              <circle cx="12" cy="12" r="10" stroke-dasharray="31.4" stroke-dashoffset="10"/>
                            </svg>
                            Placing Order...
                          </button>
                        } @else {
                          <button class="btn-place-order"
                                  [class.btn-buy]="tradeForm.direction === 'Buy'"
                                  [class.btn-sell]="tradeForm.direction === 'Sell'"
                                  (click)="submitTrade()">
                            {{ tradeForm.direction }} {{ tradeForm.volume }} {{ analysis.pair }}
                          </button>
                          <button class="btn-cancel" (click)="cancelTrade()">Cancel</button>
                        }
                      </div>
                    </div>
                  } @else if (isMarketClosed) {
                    <button class="btn-open-trade btn-market-closed" disabled>
                      <svg class="btn-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                        <circle cx="12" cy="12" r="10"/><line x1="4.93" y1="4.93" x2="19.07" y2="19.07"/>
                      </svg>
                      Market Closed
                    </button>
                  } @else {
                    <button class="btn-open-trade" (click)="openTradeForm()">
                      <svg class="btn-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                        <polyline points="22 7 13.5 15.5 8.5 10.5 2 17"/><polyline points="16 7 22 7 22 13"/>
                      </svg>
                      Open Trade
                    </button>
                  }
                </div>
              </div>
            }

            <!-- Reasoning -->
            <div class="reasoning-block">
              <div class="reasoning-header">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z"/></svg>
                AI Reasoning
              </div>
              <p class="reasoning-text">{{ analysis.reasoning }}</p>
            </div>

            <!-- Risk Events -->
            @if (analysis.riskEvents?.length) {
              <div class="risk-block">
                <div class="risk-header">
                  <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"/><line x1="12" y1="9" x2="12" y2="13"/><line x1="12" y1="17" x2="12.01" y2="17"/></svg>
                  Risk Events
                </div>
                <div class="risk-tags">
                  @for (event of analysis.riskEvents; track $index) {
                    <span class="risk-tag">{{ event }}</span>
                  }
                </div>
              </div>
            }
          </div>
        }
      </div>

      <!-- Trade Review Section -->
      <div class="card review-card">
        <div class="card-header">
          <svg class="section-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/><line x1="16" y1="13" x2="8" y2="13"/><line x1="16" y1="17" x2="8" y2="17"/><polyline points="10 9 9 9 8 9"/>
          </svg>
          <h3>Trade Review</h3>
        </div>

        <div class="form-row">
          <input
            type="number"
            [(ngModel)]="tradeId"
            placeholder="Trade ID"
            class="input"
            (keyup.enter)="reviewTrade()"
          />
          <button (click)="reviewTrade()" [disabled]="reviewing" class="btn-analyze">
            @if (reviewing) {
              <svg class="spinner" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <circle cx="12" cy="12" r="10" stroke-dasharray="31.4" stroke-dashoffset="10"/>
              </svg>
              Reviewing...
            } @else {
              Review
            }
          </button>
        </div>

        @if (review) {
          <div class="review-result">
            <!-- Score Ring -->
            <div class="score-ring-container">
              <svg class="score-ring" viewBox="0 0 120 120">
                <circle cx="60" cy="60" r="50" fill="none" stroke="var(--border)" stroke-width="8"/>
                <circle cx="60" cy="60" r="50" fill="none"
                        [attr.stroke]="getScoreColor(review.score)"
                        stroke-width="8"
                        stroke-linecap="round"
                        [attr.stroke-dasharray]="314"
                        [attr.stroke-dashoffset]="314 - (314 * review.score / 10)"
                        transform="rotate(-90 60 60)"/>
                <text x="60" y="55" text-anchor="middle" fill="var(--text-bright)" font-size="28" font-weight="700">{{ review.score }}</text>
                <text x="60" y="75" text-anchor="middle" fill="var(--text-muted)" font-size="12">/10</text>
              </svg>
            </div>

            <p class="assessment-text">{{ review.assessment }}</p>

            <div class="review-columns">
              @if (review.strengths?.length) {
                <div class="review-section strengths">
                  <h4>
                    <svg viewBox="0 0 24 24" fill="none" stroke="var(--success)" stroke-width="2"><polyline points="20 6 9 17 4 12"/></svg>
                    Strengths
                  </h4>
                  <ul>
                    @for (s of review.strengths; track $index) {
                      <li>{{ s }}</li>
                    }
                  </ul>
                </div>
              }

              @if (review.weaknesses?.length) {
                <div class="review-section weaknesses">
                  <h4>
                    <svg viewBox="0 0 24 24" fill="none" stroke="var(--danger)" stroke-width="2"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>
                    Weaknesses
                  </h4>
                  <ul>
                    @for (w of review.weaknesses; track $index) {
                      <li>{{ w }}</li>
                    }
                  </ul>
                </div>
              }
            </div>

            @if (review.improvements?.length) {
              <div class="review-section improvements">
                <h4>
                  <svg viewBox="0 0 24 24" fill="none" stroke="var(--primary)" stroke-width="2"><circle cx="12" cy="12" r="10"/><line x1="12" y1="8" x2="12" y2="16"/><line x1="8" y1="12" x2="16" y2="12"/></svg>
                  Improvements
                </h4>
                <ul>
                  @for (imp of review.improvements; track $index) {
                    <li>{{ imp }}</li>
                  }
                </ul>
              </div>
            }
          </div>
        }
      </div>
    </div>
  `,
  styles: [`
    .page-header {
      margin-bottom: 24px;
    }

    .page-header h1 {
      display: flex;
      align-items: center;
      gap: 10px;
      font-size: 22px;
      font-weight: 700;
      color: var(--text-bright);
    }

    .header-icon {
      width: 26px;
      height: 26px;
      color: var(--primary);
    }

    .card-header {
      display: flex;
      align-items: center;
      gap: 10px;
      margin-bottom: 16px;
    }

    .card-header h3 {
      font-size: 16px;
      font-weight: 600;
      color: var(--text-bright);
    }

    .section-icon {
      width: 20px;
      height: 20px;
      color: var(--primary);
    }

    .analysis-card {
      margin-bottom: 20px;
    }

    .form-row {
      display: flex;
      gap: 8px;
      margin-bottom: 16px;
    }

    .input {
      padding: 10px 14px;
      border: 1px solid var(--border);
      border-radius: 8px;
      background: var(--surface-light);
      color: var(--text);
      font-size: 14px;
      flex: 1;
      transition: border-color 0.2s, box-shadow 0.2s;

      &:focus {
        outline: none;
        border-color: var(--primary);
        box-shadow: 0 0 0 3px var(--primary-glow);
      }
    }

    .select-tf {
      flex: 0 0 auto;
      width: 80px;
    }

    .btn-analyze {
      display: flex;
      align-items: center;
      gap: 6px;
      padding: 10px 20px;
      background: var(--primary);
      color: white;
      border: none;
      border-radius: 8px;
      cursor: pointer;
      font-size: 14px;
      font-weight: 600;
      white-space: nowrap;
      transition: all 0.2s;

      &:hover:not(:disabled) {
        background: var(--primary-dark);
        box-shadow: 0 0 20px var(--primary-glow);
      }

      &:disabled {
        opacity: 0.6;
        cursor: not-allowed;
      }
    }

    .btn-icon { width: 16px; height: 16px; }

    .spinner {
      width: 16px;
      height: 16px;
      animation: spin 1s linear infinite;
    }

    @keyframes spin {
      to { transform: rotate(360deg); }
    }

    .error-banner {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 12px 16px;
      background: var(--danger-glow);
      border: 1px solid var(--danger);
      border-radius: 8px;
      color: var(--danger);
      font-size: 14px;
      margin-bottom: 16px;

      svg { width: 18px; height: 18px; flex-shrink: 0; }
    }

    .analysis-result {
      border-top: 1px solid var(--border);
      padding-top: 20px;
    }

    /* Session Bar */
    .session-bar {
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 10px 16px;
      border-radius: 8px;
      margin-bottom: 16px;
      font-size: 13px;
      flex-wrap: wrap;
    }

    .session-open {
      background: rgba(34, 197, 94, 0.08);
      border: 1px solid rgba(34, 197, 94, 0.3);
    }

    .session-closed {
      background: rgba(239, 68, 68, 0.08);
      border: 1px solid rgba(239, 68, 68, 0.3);
    }

    .session-status {
      display: flex;
      align-items: center;
      gap: 6px;
      font-weight: 700;
    }

    .session-dot {
      width: 8px;
      height: 8px;
      border-radius: 50%;
    }

    .dot-open {
      background: var(--success);
      box-shadow: 0 0 6px var(--success);
      animation: pulse-dot 2s ease-in-out infinite;
    }

    .dot-closed {
      background: var(--danger);
    }

    @keyframes pulse-dot {
      0%, 100% { opacity: 1; }
      50% { opacity: 0.4; }
    }

    .session-label {
      color: var(--text-bright);
    }

    .session-primary {
      color: var(--primary);
      font-weight: 600;
    }

    .session-advice {
      color: var(--text-muted);
      flex: 1;
    }

    .session-next {
      color: var(--warning);
      font-weight: 600;
      font-size: 12px;
    }

    /* Chart */
    .chart-wrapper {
      margin-bottom: 20px;
    }

    .chart-loading {
      display: flex;
      align-items: center;
      justify-content: center;
      gap: 8px;
      height: 200px;
      color: var(--text-muted);
      font-size: 14px;
      border: 1px solid var(--border);
      border-radius: 8px;
      background: var(--surface-light);
      margin-bottom: 20px;
    }

    .result-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      margin-bottom: 20px;
    }

    .symbol-block {
      display: flex;
      align-items: center;
      gap: 10px;
    }

    .pair {
      font-weight: 700;
      font-size: 20px;
      color: var(--text-bright);
      font-family: var(--font-mono);
    }

    .timeframe-tag {
      padding: 2px 8px;
      background: var(--surface-light);
      border-radius: 4px;
      font-size: 11px;
      font-weight: 600;
      color: var(--text-muted);
      text-transform: uppercase;
    }

    .badges {
      display: flex;
      gap: 8px;
    }

    .badge {
      display: inline-flex;
      align-items: center;
      gap: 4px;
      padding: 5px 12px;
      border-radius: 6px;
      font-size: 12px;
      font-weight: 700;
      text-transform: uppercase;
      letter-spacing: 0.03em;

      svg { width: 14px; height: 14px; }
    }

    .bias-bullish { background: var(--success-glow); color: var(--success); border: 1px solid var(--success); }
    .bias-bearish { background: var(--danger-glow); color: var(--danger); border: 1px solid var(--danger); }
    .bias-neutral { background: var(--warning-glow); color: var(--warning); border: 1px solid var(--warning); }

    .rec-buy { background: var(--success-glow); color: var(--success); border: 1px solid var(--success); }
    .rec-sell { background: var(--danger-glow); color: var(--danger); border: 1px solid var(--danger); }
    .rec-wait { background: var(--surface-light); color: var(--text-muted); border: 1px solid var(--border-light); }
    .rec-reduce_exposure { background: var(--warning-glow); color: var(--warning); border: 1px solid var(--warning); }

    .confidence-row {
      display: flex;
      align-items: center;
      gap: 12px;
      margin-bottom: 20px;
    }

    .confidence-label {
      font-size: 12px;
      font-weight: 600;
      color: var(--text-muted);
      text-transform: uppercase;
      width: 80px;
    }

    .confidence-bar {
      flex: 1;
      height: 8px;
      background: var(--surface-light);
      border-radius: 4px;
      overflow: hidden;
    }

    .confidence-fill {
      height: 100%;
      border-radius: 4px;
      transition: width 0.6s ease;
    }

    .fill-low { background: var(--danger); }
    .fill-medium { background: var(--warning); }
    .fill-high { background: var(--success); }

    .confidence-value {
      font-size: 14px;
      font-weight: 700;
      color: var(--text-bright);
      font-family: var(--font-mono);
      width: 40px;
      text-align: right;
    }

    .levels-row {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 12px;
      margin-bottom: 20px;
    }

    .level-card {
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 14px 16px;
      border-radius: 8px;
      border: 1px solid var(--border);
      background: var(--surface-light);

      svg { width: 20px; height: 20px; flex-shrink: 0; }
    }

    .level-card.support svg { color: var(--success); }
    .level-card.resistance svg { color: var(--danger); }

    .level-label {
      color: var(--text-muted);
      font-size: 11px;
      font-weight: 600;
      text-transform: uppercase;
      display: block;
    }

    .level-value {
      font-weight: 700;
      font-size: 16px;
      color: var(--text-bright);
    }

    .mono {
      font-family: var(--font-mono);
    }

    /* Trade Suggestion Panel */
    .trade-panel {
      margin-bottom: 20px;
      padding: 16px;
      background: var(--surface-light);
      border-radius: 8px;
      border: 1px solid var(--border);
    }

    .trade-panel-header {
      display: flex;
      align-items: center;
      gap: 8px;
      margin-bottom: 14px;

      svg { width: 18px; height: 18px; color: var(--primary); }
      h4 {
        font-size: 13px;
        font-weight: 700;
        text-transform: uppercase;
        color: var(--primary);
        margin-right: auto;
      }
    }

    .trade-type-badge {
      padding: 3px 8px;
      font-size: 11px;
      font-weight: 600;
      text-transform: uppercase;
      border-radius: 4px;
      background: var(--surface);
      color: var(--text-muted);
      border: 1px solid var(--border);
    }

    .trade-dir-badge {
      padding: 3px 10px;
      font-size: 11px;
      font-weight: 700;
      text-transform: uppercase;
      border-radius: 4px;
    }

    .dir-buy {
      background: var(--success-glow);
      color: var(--success);
      border: 1px solid var(--success);
    }

    .dir-sell {
      background: var(--danger-glow);
      color: var(--danger);
      border: 1px solid var(--danger);
    }

    .trade-levels-grid {
      display: grid;
      grid-template-columns: 1fr 1fr 1fr;
      gap: 10px;
      margin-bottom: 14px;
    }

    .trade-level {
      padding: 10px 12px;
      border-radius: 6px;
      background: var(--surface);
      border: 1px solid var(--border);
    }

    .trade-level-label {
      display: block;
      font-size: 10px;
      font-weight: 600;
      text-transform: uppercase;
      color: var(--text-muted);
      margin-bottom: 4px;
    }

    .trade-level-value {
      display: block;
      font-size: 15px;
      font-weight: 700;
      color: var(--text-bright);
    }

    .trade-level-pips {
      display: block;
      font-size: 11px;
      color: var(--text-muted);
      margin-top: 2px;
    }

    .entry-level { border-left: 3px solid var(--primary); }
    .sl-level { border-left: 3px solid var(--danger); }
    .tp-level { border-left: 3px solid var(--success); }

    .trade-meta-row {
      display: grid;
      grid-template-columns: repeat(4, 1fr);
      gap: 10px;
      margin-bottom: 14px;
    }

    .trade-meta-item {
      text-align: center;
    }

    .meta-label {
      display: block;
      font-size: 10px;
      font-weight: 600;
      text-transform: uppercase;
      color: var(--text-muted);
      margin-bottom: 4px;
    }

    .meta-value {
      font-size: 14px;
      font-weight: 700;
      color: var(--text-bright);
    }

    .positive { color: var(--success); }

    .trade-rationale {
      font-size: 13px;
      line-height: 1.6;
      color: var(--text);
      padding-top: 12px;
      border-top: 1px solid var(--border);
    }

    .trade-result {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 10px 14px;
      border-radius: 6px;
      font-size: 13px;
      font-weight: 600;
      margin-top: 12px;

      svg { width: 16px; height: 16px; flex-shrink: 0; }
    }

    .trade-success {
      background: rgba(34, 197, 94, 0.1);
      border: 1px solid rgba(34, 197, 94, 0.3);
      color: var(--success);
    }

    .trade-error {
      background: var(--danger-glow);
      border: 1px solid var(--danger);
      color: var(--danger);
    }

    .trade-actions {
      display: flex;
      gap: 8px;
      margin-top: 14px;
      padding-top: 14px;
      border-top: 1px solid var(--border);
    }

    .btn-open-trade {
      display: flex;
      align-items: center;
      gap: 6px;
      padding: 10px 20px;
      background: var(--primary);
      color: white;
      border: none;
      border-radius: 8px;
      cursor: pointer;
      font-size: 14px;
      font-weight: 600;
      white-space: nowrap;
      transition: all 0.2s;
      flex: 1;
      justify-content: center;

      &:hover:not(:disabled) {
        background: var(--primary-dark);
        box-shadow: 0 0 20px var(--primary-glow);
      }

      &:disabled {
        opacity: 0.7;
        cursor: not-allowed;
      }
    }

    /* Margin Info */
    .margin-info-row {
      display: flex;
      align-items: center;
      gap: 16px;
      margin-bottom: 14px;
      padding: 10px 12px;
      background: var(--surface);
      border-radius: 6px;
      border: 1px solid var(--border);
    }

    .margin-info-item {
      display: flex;
      flex-direction: column;
      gap: 2px;
    }

    .leverage-warning {
      display: flex;
      align-items: center;
      gap: 6px;
      color: var(--warning);
      font-size: 12px;
      font-weight: 600;
      flex: 1;

      svg { width: 16px; height: 16px; flex-shrink: 0; }
    }

    /* Trade Form */
    .trade-form {
      width: 100%;
      border: 1px solid var(--border);
      border-radius: 8px;
      background: var(--surface);
      padding: 16px;
    }

    .trade-form-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-bottom: 14px;

      h5 {
        font-size: 13px;
        font-weight: 700;
        text-transform: uppercase;
        color: var(--primary);
        margin: 0;
      }
    }

    .btn-close-form {
      background: none;
      border: none;
      color: var(--text-muted);
      font-size: 20px;
      cursor: pointer;
      padding: 0 4px;
      line-height: 1;

      &:hover { color: var(--text); }
    }

    .trade-form-grid {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 12px;
      margin-bottom: 14px;
    }

    .form-field {
      display: flex;
      flex-direction: column;
      gap: 4px;

      label {
        font-size: 11px;
        font-weight: 600;
        text-transform: uppercase;
        color: var(--text-muted);
      }
    }

    .form-input {
      padding: 8px 10px;
      border: 1px solid var(--border);
      border-radius: 6px;
      background: var(--surface-light);
      color: var(--text-bright);
      font-size: 14px;
      font-weight: 600;
      transition: border-color 0.2s, box-shadow 0.2s;

      &:focus {
        outline: none;
        border-color: var(--primary);
        box-shadow: 0 0 0 3px var(--primary-glow);
      }
    }

    select.form-input {
      cursor: pointer;
    }

    .trade-form-actions {
      display: flex;
      gap: 8px;
      padding-top: 14px;
      border-top: 1px solid var(--border);
    }

    .btn-place-order {
      flex: 1;
      padding: 10px 20px;
      color: white;
      border: none;
      border-radius: 8px;
      cursor: pointer;
      font-size: 14px;
      font-weight: 600;
      white-space: nowrap;
      transition: all 0.2s;
      display: flex;
      align-items: center;
      justify-content: center;
      gap: 6px;
      background: var(--primary);

      &:hover:not(:disabled) {
        filter: brightness(1.1);
        box-shadow: 0 0 20px var(--primary-glow);
      }

      &:disabled {
        opacity: 0.7;
        cursor: not-allowed;
      }
    }

    .btn-market-closed {
      background: var(--surface-light) !important;
      color: var(--text-muted) !important;
      border: 1px solid var(--border);
      cursor: not-allowed;
      opacity: 0.8;
    }

    .btn-place-order.btn-buy {
      background: var(--success);

      &:hover:not(:disabled) {
        box-shadow: 0 0 20px rgba(34, 197, 94, 0.3);
      }
    }

    .btn-place-order.btn-sell {
      background: var(--danger);

      &:hover:not(:disabled) {
        box-shadow: 0 0 20px rgba(239, 68, 68, 0.3);
      }
    }

    .btn-cancel {
      padding: 10px 20px;
      background: transparent;
      color: var(--text-muted);
      border: 1px solid var(--border);
      border-radius: 8px;
      cursor: pointer;
      font-size: 14px;
      font-weight: 600;
      white-space: nowrap;
      transition: all 0.2s;

      &:hover {
        color: var(--text);
        border-color: var(--text-muted);
      }
    }

    .reasoning-block {
      margin-bottom: 16px;
      padding: 16px;
      background: var(--surface-light);
      border-radius: 8px;
      border-left: 3px solid var(--primary);
    }

    .reasoning-header {
      display: flex;
      align-items: center;
      gap: 8px;
      font-size: 12px;
      font-weight: 700;
      color: var(--primary);
      text-transform: uppercase;
      margin-bottom: 8px;

      svg { width: 14px; height: 14px; }
    }

    .reasoning-text {
      color: var(--text);
      font-size: 14px;
      line-height: 1.7;
    }

    .risk-block {
      padding: 14px 16px;
      background: var(--warning-glow);
      border-radius: 8px;
      border: 1px solid rgba(245, 158, 11, 0.3);
    }

    .risk-header {
      display: flex;
      align-items: center;
      gap: 8px;
      font-size: 12px;
      font-weight: 700;
      color: var(--warning);
      text-transform: uppercase;
      margin-bottom: 10px;

      svg { width: 16px; height: 16px; }
    }

    .risk-tags {
      display: flex;
      flex-wrap: wrap;
      gap: 6px;
    }

    .risk-tag {
      padding: 4px 10px;
      background: rgba(245, 158, 11, 0.1);
      border: 1px solid rgba(245, 158, 11, 0.3);
      border-radius: 4px;
      font-size: 13px;
      color: var(--text);
    }

    /* Trade Review */
    .review-card {
      margin-bottom: 20px;
    }

    .review-result {
      border-top: 1px solid var(--border);
      padding-top: 20px;
    }

    .score-ring-container {
      display: flex;
      justify-content: center;
      margin-bottom: 20px;
    }

    .score-ring {
      width: 120px;
      height: 120px;
    }

    .assessment-text {
      text-align: center;
      margin-bottom: 20px;
      font-size: 15px;
      line-height: 1.7;
      color: var(--text);
    }

    .review-columns {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 16px;
      margin-bottom: 16px;
    }

    .review-section {
      padding: 14px 16px;
      border-radius: 8px;
      border: 1px solid var(--border);
      background: var(--surface-light);
    }

    .review-section h4 {
      display: flex;
      align-items: center;
      gap: 6px;
      font-size: 13px;
      font-weight: 700;
      text-transform: uppercase;
      margin-bottom: 10px;

      svg { width: 16px; height: 16px; }
    }

    .strengths h4 { color: var(--success); }
    .weaknesses h4 { color: var(--danger); }
    .improvements h4 { color: var(--primary); }

    .review-section ul {
      list-style: none;
      margin: 0;
      padding: 0;
    }

    .review-section li {
      position: relative;
      padding-left: 14px;
      margin-bottom: 6px;
      font-size: 13px;
      line-height: 1.5;
      color: var(--text);

      &::before {
        content: '';
        position: absolute;
        left: 0;
        top: 8px;
        width: 4px;
        height: 4px;
        border-radius: 50%;
      }
    }

    .strengths li::before { background: var(--success); }
    .weaknesses li::before { background: var(--danger); }
    .improvements li::before { background: var(--primary); }

    /* Symbol Autocomplete */
    .symbol-autocomplete {
      position: relative;
      flex: 1;
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
      border-radius: 8px;
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
      .form-row {
        flex-direction: column;
      }

      .select-tf {
        width: 100%;
      }

      .btn-analyze {
        width: 100%;
        justify-content: center;
      }

      .levels-row {
        grid-template-columns: 1fr;
      }

      .trade-levels-grid {
        grid-template-columns: 1fr;
      }

      .trade-meta-row {
        grid-template-columns: repeat(2, 1fr);
      }

      .review-columns {
        grid-template-columns: 1fr;
      }

      .trade-form-grid {
        grid-template-columns: 1fr;
      }

      .result-header {
        flex-direction: column;
        align-items: flex-start;
        gap: 12px;
      }

      .session-bar {
        flex-direction: column;
        align-items: flex-start;
        gap: 6px;
      }

      .margin-info-row {
        flex-direction: column;
        gap: 10px;
      }

      .trade-form-actions {
        flex-direction: column;
      }

      .btn-place-order, .btn-cancel {
        width: 100%;
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

      .trade-meta-row {
        grid-template-columns: repeat(2, 1fr);
        gap: 8px;
      }

      .pair {
        font-size: 16px;
      }

      .badge {
        padding: 4px 8px;
        font-size: 11px;
      }
    }
  `]
})
export class AiAnalysisComponent implements OnInit, OnDestroy {
  symbol = '';
  timeframe = 'H4';
  tradeId: number | null = null;

  analyzing = false;
  reviewing = false;
  loadingCandles = false;

  analysis: MarketAnalysis | null = null;
  review: TradeReview | null = null;
  analysisError = '';
  candles: CandleData[] = [];
  tradeLevels: TradeLevels | null = null;
  liveChartPrice: { bid: number; ask: number; timestamp: Date } | null = null;

  // Trade form state
  showTradeForm = false;
  submittingTrade = false;
  tradeResult: { success: boolean; message: string } | null = null;
  tradeForm = {
    direction: '',
    orderType: '',
    volume: 0,
    entry: 0,
    stopLoss: 0,
    takeProfit: 0
  };

  private destroyRef = inject(DestroyRef);
  private signalR = inject(SignalRService);
  private priceSub: Subscription | null = null;
  private subscribedSymbol: string | null = null;

  // Symbol autocomplete state
  symbols: SymbolInfo[] = [];
  symbolSearch = '';
  showSymbolDropdown = false;
  activeCategory = 'All';

  constructor(private http: HttpClient, private elRef: ElementRef) {}

  ngOnInit() {
    this.loadSymbols();
  }

  loadSymbols() {
    this.http.get<SymbolInfo[]>(`${environment.apiUrl}/api/positions/symbols`)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (data) => { this.symbols = data; } });
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
    if (this.symbol) {
      this.symbolSearch = '';
    }
  }

  onSymbolSearchInput(event: Event) {
    const input = event.target as HTMLInputElement;
    this.symbolSearch = input.value;
    if (this.symbol) {
      this.symbol = '';
    }
    this.showSymbolDropdown = true;
  }

  selectSymbol(s: SymbolInfo) {
    this.symbol = s.name;
    this.symbolSearch = '';
    this.showSymbolDropdown = false;
  }

  clearSymbol() {
    this.symbol = '';
    this.symbolSearch = '';
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
      } else if (this.symbol) {
        this.showSymbolDropdown = false;
        this.analyze();
      }
    }
  }

  getConfidenceLevel(confidence: number): string {
    if (confidence < 0.4) return 'low';
    if (confidence < 0.7) return 'medium';
    return 'high';
  }

  getScoreColor(score: number): string {
    if (score <= 3) return 'var(--danger)';
    if (score <= 6) return 'var(--warning)';
    return 'var(--success)';
  }

  ngOnDestroy() {
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
    this.liveChartPrice = null;
  }

  private subscribeToLivePrices() {
    if (!this.symbol) return;

    this.signalR.subscribeToSymbol(this.symbol);
    this.subscribedSymbol = this.symbol;
    this.priceSub = this.signalR.priceUpdates$.subscribe(prices => {
      const price = prices.get(this.symbol);
      if (price) {
        this.liveChartPrice = { bid: price.bid, ask: price.ask, timestamp: price.timestamp };
      }
    });
  }

  analyze() {
    if (!this.symbol) return;
    this.analyzing = true;
    this.analysisError = '';
    this.candles = [];
    this.tradeLevels = null;
    this.showTradeForm = false;
    this.submittingTrade = false;
    this.tradeResult = null;
    this.cleanupPriceSub();
    this.http.get<MarketAnalysis>(
      `${environment.apiUrl}/api/ai/analyze/${this.symbol}?timeframe=${this.timeframe}`
    )
    .pipe(takeUntilDestroyed(this.destroyRef))
    .subscribe({
      next: (data) => {
        this.analysis = data;
        this.analyzing = false;

        // Set trade levels for chart if trade suggestion exists
        if (data.trade) {
          this.tradeLevels = {
            entry: data.trade.entry,
            stopLoss: data.trade.stopLoss,
            takeProfit: data.trade.takeProfit,
            direction: data.trade.direction
          };
        }

        // Fetch candles for chart
        this.loadCandles();
      },
      error: (err) => {
          this.analyzing = false;
          this.analysisError = err.error?.message || err.message || 'Analysis failed';
      }
    });
  }

  private loadCandles() {
    this.loadingCandles = true;
    this.http.get<CandleData[]>(
      `${environment.apiUrl}/api/ai/candles/${this.symbol}?timeframe=${this.timeframe}&count=100`
    )
    .pipe(takeUntilDestroyed(this.destroyRef))
    .subscribe({
      next: (data) => {
        this.candles = data;
        this.loadingCandles = false;
        this.subscribeToLivePrices();
      },
      error: () => {
        this.loadingCandles = false;
      }
    });
  }

  get isMarketClosed(): boolean {
    return this.analysis?.marketSession?.isMarketOpen === false;
  }

  openTradeForm() {
    if (!this.analysis?.trade) return;
    const trade = this.analysis.trade;
    const capitalize = (s: string) => s.charAt(0).toUpperCase() + s.slice(1).toLowerCase();

    this.tradeForm = {
      direction: capitalize(trade.direction),
      orderType: capitalize(trade.orderType),
      volume: trade.lotSize,
      entry: trade.entry,
      stopLoss: trade.stopLoss,
      takeProfit: trade.takeProfit
    };
    this.showTradeForm = true;
    this.tradeResult = null;
  }

  submitTrade() {
    if (!this.analysis) return;

    this.submittingTrade = true;
    this.tradeResult = null;

    const body = {
      symbol: this.analysis.pair,
      direction: this.tradeForm.direction,
      orderType: this.tradeForm.orderType,
      volume: this.tradeForm.volume,
      price: this.tradeForm.orderType.toLowerCase() !== 'market' ? this.tradeForm.entry : null,
      stopLoss: this.tradeForm.stopLoss,
      takeProfit: this.tradeForm.takeProfit
    };

    this.http.post<{ success: boolean; positionId?: number; orderId?: number; errorMessage?: string }>(
      `${environment.apiUrl}/api/positions/open`, body
    )
    .pipe(takeUntilDestroyed(this.destroyRef))
    .subscribe({
      next: (res) => {
        this.submittingTrade = false;
        this.showTradeForm = false;
        if (res.success) {
          const id = res.positionId ?? res.orderId;
          this.tradeResult = { success: true, message: `Order placed successfully${id ? ' (ID: ' + id + ')' : ''}` };
        } else {
          this.tradeResult = { success: false, message: res.errorMessage ?? 'Order failed' };
        }
      },
      error: (err) => {
        this.submittingTrade = false;
        this.showTradeForm = false;
        this.tradeResult = { success: false, message: err.error?.error || err.error?.message || err.message || 'Order failed' };
      }
    });
  }

  cancelTrade() {
    this.showTradeForm = false;
    this.tradeResult = null;
  }

  reviewTrade() {
    if (!this.tradeId) return;
    this.reviewing = true;
    this.analysisError = '';
    this.http.get<TradeReview>(
      `${environment.apiUrl}/api/ai/review/${this.tradeId}`
    )
    .pipe(takeUntilDestroyed(this.destroyRef))
    .subscribe({
      next: (data) => {
        this.review = data;
        this.reviewing = false;
      },
      error: (err) => {
          this.reviewing = false;
          this.analysisError = err.error?.message || err.message || 'Trade review failed';
      }
    });
  }
}
