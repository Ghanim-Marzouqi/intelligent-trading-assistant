import { Component, DestroyRef, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../environments/environment';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

interface MarketAnalysis {
  pair: string;
  bias: string;
  confidence: number;
  keyLevels: { support: number; resistance: number };
  riskEvents: string[];
  recommendation: string;
  reasoning: string;
}

interface TradeReview {
  tradeId: number;
  assessment: string;
  strengths: string[];
  weaknesses: string[];
  improvements: string[];
  score: number;
}

@Component({
  selector: 'app-ai-analysis',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="ai-page">
      <header class="page-header">
        <h1>AI Analysis</h1>
      </header>

      <div class="grid-2">
        <div class="card">
          <h3>Market Analysis</h3>
          <div class="form-row">
            <input
              type="text"
              [(ngModel)]="symbol"
              placeholder="e.g. EURUSD"
              class="input"
            />
            <select [(ngModel)]="timeframe" class="input">
              <option value="M15">M15</option>
              <option value="H1">H1</option>
              <option value="H4">H4</option>
              <option value="D1">D1</option>
            </select>
            <button (click)="analyze()" [disabled]="analyzing" class="btn">
              {{ analyzing ? 'Analyzing...' : 'Analyze' }}
            </button>
          </div>

          @if (analysis) {
            <div class="analysis-result">
              <div class="result-header">
                <span class="pair">{{ analysis.pair }}</span>
                <span class="bias" [class]="analysis.bias?.toLowerCase()">
                  {{ analysis.bias }}
                </span>
                <span class="confidence">{{ (analysis.confidence * 100) | number:'1.0-0' }}% confidence</span>
              </div>

              <div class="levels">
                <div class="level">
                  <span class="level-label">Support</span>
                  <span class="level-value">{{ analysis.keyLevels?.support }}</span>
                </div>
                <div class="level">
                  <span class="level-label">Resistance</span>
                  <span class="level-value">{{ analysis.keyLevels?.resistance }}</span>
                </div>
              </div>

              <div class="recommendation">
                <strong>Recommendation:</strong> {{ analysis.recommendation }}
              </div>

              <div class="reasoning">{{ analysis.reasoning }}</div>

              @if (analysis.riskEvents?.length) {
                <div class="risk-events">
                  <strong>Risk Events:</strong>
                  <ul>
                    @for (event of analysis.riskEvents; track event) {
                      <li>{{ event }}</li>
                    }
                  </ul>
                </div>
              }
            </div>
          }
        </div>

        <div class="card">
          <h3>Trade Review</h3>
          <div class="form-row">
            <input
              type="number"
              [(ngModel)]="tradeId"
              placeholder="Trade ID"
              class="input"
            />
            <button (click)="reviewTrade()" [disabled]="reviewing" class="btn">
              {{ reviewing ? 'Reviewing...' : 'Review' }}
            </button>
          </div>

          @if (review) {
            <div class="review-result">
              <div class="score-badge">
                <span class="score">{{ review.score }}/10</span>
              </div>

              <div class="assessment">{{ review.assessment }}</div>

              @if (review.strengths?.length) {
                <div class="section">
                  <h4>Strengths</h4>
                  <ul class="positive-list">
                    @for (s of review.strengths; track s) {
                      <li>{{ s }}</li>
                    }
                  </ul>
                </div>
              }

              @if (review.weaknesses?.length) {
                <div class="section">
                  <h4>Weaknesses</h4>
                  <ul class="negative-list">
                    @for (w of review.weaknesses; track w) {
                      <li>{{ w }}</li>
                    }
                  </ul>
                </div>
              }

              @if (review.improvements?.length) {
                <div class="section">
                  <h4>Improvements</h4>
                  <ul>
                    @for (imp of review.improvements; track imp) {
                      <li>{{ imp }}</li>
                    }
                  </ul>
                </div>
              }
            </div>
          }
        </div>
      </div>
    </div>
  `,
  styles: [`
    .page-header {
      margin-bottom: 24px;
    }

    .grid-2 {
      display: grid;
      grid-template-columns: repeat(2, 1fr);
      gap: 16px;
    }

    .card h3 {
      margin-bottom: 16px;
    }

    .form-row {
      display: flex;
      gap: 8px;
      margin-bottom: 16px;
    }

    .input {
      padding: 10px 12px;
      border: 1px solid var(--border);
      border-radius: 6px;
      background: var(--background);
      color: var(--text);
      font-size: 14px;
      flex: 1;

      &:focus {
        outline: none;
        border-color: var(--primary);
      }
    }

    select.input {
      flex: 0 0 auto;
      width: 80px;
    }

    .btn {
      padding: 10px 20px;
      background: var(--primary);
      color: white;
      border: none;
      border-radius: 6px;
      cursor: pointer;
      font-size: 14px;
      white-space: nowrap;

      &:hover:not(:disabled) {
        opacity: 0.9;
      }

      &:disabled {
        opacity: 0.6;
        cursor: not-allowed;
      }
    }

    .analysis-result,
    .review-result {
      border-top: 1px solid var(--border);
      padding-top: 16px;
    }

    .result-header {
      display: flex;
      align-items: center;
      gap: 12px;
      margin-bottom: 16px;
    }

    .pair {
      font-weight: 600;
      font-size: 18px;
    }

    .bias {
      padding: 4px 10px;
      border-radius: 4px;
      font-size: 12px;
      font-weight: 600;
      text-transform: uppercase;

      &.bullish {
        background: rgba(34, 197, 94, 0.15);
        color: var(--success);
      }

      &.bearish {
        background: rgba(239, 68, 68, 0.15);
        color: var(--danger);
      }

      &.neutral {
        background: rgba(234, 179, 8, 0.15);
        color: #eab308;
      }
    }

    .confidence {
      color: var(--text-muted);
      font-size: 14px;
    }

    .levels {
      display: flex;
      gap: 24px;
      margin-bottom: 12px;
    }

    .level-label {
      color: var(--text-muted);
      font-size: 12px;
      display: block;
    }

    .level-value {
      font-weight: 600;
    }

    .recommendation {
      margin-bottom: 12px;
    }

    .reasoning {
      color: var(--text-muted);
      font-size: 14px;
      line-height: 1.6;
      margin-bottom: 12px;
    }

    .risk-events ul {
      margin: 8px 0 0 16px;
      color: var(--text-muted);
    }

    .score-badge {
      text-align: center;
      margin-bottom: 16px;
    }

    .score {
      font-size: 32px;
      font-weight: 700;
    }

    .assessment {
      margin-bottom: 16px;
      line-height: 1.6;
    }

    .section {
      margin-bottom: 12px;
    }

    .section h4 {
      margin-bottom: 8px;
      font-size: 14px;
    }

    .positive-list li {
      color: var(--success);
    }

    .negative-list li {
      color: var(--danger);
    }

    ul {
      margin: 0 0 0 16px;
      padding: 0;
    }

    li {
      margin-bottom: 4px;
      font-size: 14px;
    }
  `]
})
export class AiAnalysisComponent {
  symbol = '';
  timeframe = 'H4';
  tradeId: number | null = null;

  analyzing = false;
  reviewing = false;

  analysis: MarketAnalysis | null = null;
  review: TradeReview | null = null;
  analysisError = '';
  private destroyRef = inject(DestroyRef);

  constructor(private http: HttpClient) {}

  analyze() {
    if (!this.symbol) return;
    this.analyzing = true;
    this.analysisError = '';
    this.http.get<MarketAnalysis>(
      `${environment.apiUrl}/api/ai/analyze/${this.symbol}?timeframe=${this.timeframe}`
    )
    .pipe(takeUntilDestroyed(this.destroyRef))
    .subscribe({
      next: (data) => {
        this.analysis = data;
        this.analyzing = false;
      },
      error: (err) => {
          this.analyzing = false;
          this.analysisError = err.message || 'Analysis failed';
      }
    });
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
          this.analysisError = err.message || 'Trade review failed';
      }
    });
  }
}
