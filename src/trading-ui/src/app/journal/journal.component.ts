import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../environments/environment';

@Component({
  selector: 'app-journal',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="journal-page">
      <header class="page-header">
        <h1>Trade Journal</h1>
      </header>

      <div class="stats-grid">
        <div class="card stat-card">
          <div class="stat-label">Total Trades</div>
          <div class="stat-value">{{ stats.totalTrades }}</div>
        </div>
        <div class="card stat-card">
          <div class="stat-label">Win Rate</div>
          <div class="stat-value">{{ stats.winRate | number:'1.1-1' }}%</div>
        </div>
        <div class="card stat-card">
          <div class="stat-label">Profit Factor</div>
          <div class="stat-value">{{ stats.profitFactor | number:'1.2-2' }}</div>
        </div>
        <div class="card stat-card">
          <div class="stat-label">Total P&L</div>
          <div class="stat-value" [class.positive]="stats.totalPnL > 0" [class.negative]="stats.totalPnL < 0">
            {{ stats.totalPnL | currency }}
          </div>
        </div>
      </div>

      <div class="card">
        <h3>Recent Trades</h3>
        @if (trades.length === 0) {
          <p class="empty-state">No trades recorded yet</p>
        } @else {
          <div class="table-scroll">
            <table>
              <thead>
                <tr>
                  <th>Date</th>
                  <th>Symbol</th>
                  <th>Direction</th>
                  <th>Volume</th>
                  <th>Entry</th>
                  <th>Exit</th>
                  <th>Pips</th>
                  <th>R:R</th>
                  <th>P&L</th>
                  <th>Tags</th>
                </tr>
              </thead>
              <tbody>
                @for (trade of trades; track trade.id) {
                  <tr (click)="selectTrade(trade)" class="clickable"
                      [class.selected-row]="selectedTrade?.id === trade.id">
                    <td data-label="Date">{{ trade.closeTime | date:'shortDate' }}</td>
                    <td data-label="Symbol"><strong>{{ trade.symbol }}</strong></td>
                    <td data-label="Direction" [class.buy]="trade.direction === 'Buy'" [class.sell]="trade.direction === 'Sell'">
                      {{ trade.direction }}
                    </td>
                    <td data-label="Volume">{{ trade.volume }}</td>
                    <td data-label="Entry">{{ trade.entryPrice }}</td>
                    <td data-label="Exit">{{ trade.exitPrice }}</td>
                    <td data-label="Pips" [class.positive]="trade.pnLPips > 0" [class.negative]="trade.pnLPips < 0">
                      {{ trade.pnLPips | number:'1.1-1' }}
                    </td>
                    <td data-label="R:R">{{ trade.riskRewardRatio ? (trade.riskRewardRatio | number:'1.2-2') : '-' }}</td>
                    <td data-label="P&L" [class.positive]="trade.netPnL > 0" [class.negative]="trade.netPnL < 0">
                      {{ trade.netPnL | currency }}
                    </td>
                    <td data-label="Tags">
                      @for (tag of trade.tags; track tag.id) {
                        <span class="tag">{{ tag.name }}</span>
                      }
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
      </div>

      @if (selectedTrade) {
        <div class="card trade-detail">
          <div class="detail-header">
            <h3>
              <span [class.buy]="selectedTrade.direction === 'Buy'"
                    [class.sell]="selectedTrade.direction === 'Sell'">
                {{ selectedTrade.direction }}
              </span>
              {{ selectedTrade.symbol }}
              <span class="volume-badge">{{ selectedTrade.volume }} lot</span>
              <span class="trade-id">#{{ selectedTrade.positionId }}</span>
            </h3>
          </div>

          <!-- Trade data grid -->
          <div class="detail-grid">
            <div class="detail-item">
              <span class="label">Direction</span>
              <span class="value" [class.buy]="selectedTrade.direction === 'Buy'"
                    [class.sell]="selectedTrade.direction === 'Sell'">
                {{ selectedTrade.direction }}
              </span>
            </div>
            <div class="detail-item">
              <span class="label">Volume</span>
              <span class="value">{{ selectedTrade.volume }}</span>
            </div>
            <div class="detail-item">
              <span class="label">Duration</span>
              <span class="value">{{ formatDuration(selectedTrade.duration) }}</span>
            </div>

            <div class="detail-item">
              <span class="label">Entry Price</span>
              <span class="value">{{ selectedTrade.entryPrice }}</span>
            </div>
            <div class="detail-item">
              <span class="label">Exit Price</span>
              <span class="value">{{ selectedTrade.exitPrice }}</span>
            </div>
            <div class="detail-item">
              <span class="label">P&L</span>
              <span class="value" [class.positive]="selectedTrade.pnL > 0"
                    [class.negative]="selectedTrade.pnL < 0">
                {{ selectedTrade.pnL | currency }}
              </span>
            </div>

            <div class="detail-item">
              <span class="label">Stop Loss</span>
              <span class="value">{{ selectedTrade.stopLoss ?? '-' }}</span>
            </div>
            <div class="detail-item">
              <span class="label">Take Profit</span>
              <span class="value">{{ selectedTrade.takeProfit ?? '-' }}</span>
            </div>
            <div class="detail-item">
              <span class="label">R:R</span>
              <span class="value">{{ selectedTrade.riskRewardRatio ? (selectedTrade.riskRewardRatio | number:'1.2-2') : '-' }}</span>
            </div>

            <div class="detail-item">
              <span class="label">Commission</span>
              <span class="value">{{ selectedTrade.commission | currency }}</span>
            </div>
            <div class="detail-item">
              <span class="label">Swap</span>
              <span class="value">{{ selectedTrade.swap | currency }}</span>
            </div>
            <div class="detail-item">
              <span class="label">Net P&L</span>
              <span class="value" [class.positive]="selectedTrade.netPnL > 0"
                    [class.negative]="selectedTrade.netPnL < 0">
                {{ selectedTrade.netPnL | currency }}
              </span>
            </div>

            <div class="detail-item">
              <span class="label">Opened</span>
              <span class="value">{{ selectedTrade.openTime | date:'medium' }}</span>
            </div>
            <div class="detail-item">
              <span class="label">Closed</span>
              <span class="value">{{ selectedTrade.closeTime | date:'medium' }}</span>
            </div>
            <div class="detail-item">
              <span class="label">Pips</span>
              <span class="value" [class.positive]="selectedTrade.pnLPips > 0"
                    [class.negative]="selectedTrade.pnLPips < 0">
                {{ selectedTrade.pnLPips | number:'1.1-1' }}
              </span>
            </div>
          </div>

          <!-- Review section -->
          <div class="review-section">
            <h4>Review</h4>
            <p class="section-hint">Rate this trade and record your strategy, setup, and emotional state for later analysis.</p>
            <div class="review-fields">
              <div class="review-field">
                <label>Rating</label>
                <div class="star-rating">
                  @for (star of [1, 2, 3, 4, 5]; track star) {
                    <span class="star" [class.filled]="star <= reviewForm.rating"
                          (click)="reviewForm.rating = star">&#9733;</span>
                  }
                </div>
              </div>
              <div class="review-field">
                <label>Strategy</label>
                <input type="text" [(ngModel)]="reviewForm.strategy"
                       placeholder="e.g. Breakout, Reversal" />
              </div>
              <div class="review-field">
                <label>Setup</label>
                <input type="text" [(ngModel)]="reviewForm.setup"
                       placeholder="e.g. H4 support bounce" />
              </div>
              <div class="review-field">
                <label>Emotion</label>
                <select [(ngModel)]="reviewForm.emotion">
                  <option value="">-- Select --</option>
                  <option value="Calm">Calm</option>
                  <option value="Confident">Confident</option>
                  <option value="Anxious">Anxious</option>
                  <option value="FOMO">FOMO</option>
                  <option value="Revenge">Revenge</option>
                  <option value="Frustrated">Frustrated</option>
                </select>
              </div>
            </div>
            <button class="btn btn-primary" (click)="saveReview()" [disabled]="savingReview">
              {{ savingReview ? 'Saving...' : 'Save Review' }}
            </button>
          </div>

          <!-- Tags section -->
          <div class="tags-section">
            <h4>Tags</h4>
            <p class="section-hint">Categorize trades for filtering (e.g. "news", "scalp", "london-session").</p>
            <div class="tags-list">
              @for (tag of selectedTrade.tags; track tag.id) {
                <span class="tag-pill">
                  {{ tag.name }}
                  <span class="tag-delete" (click)="deleteTag(tag.id)">&times;</span>
                </span>
              }
              @if (!selectedTrade.tags?.length) {
                <span class="no-tags">No tags</span>
              }
            </div>
            <div class="tag-input-row">
              <input type="text" [(ngModel)]="newTag" placeholder="Add tag..."
                     (keydown.enter)="addTag()" />
              <button class="btn btn-sm" (click)="addTag()" [disabled]="!newTag.trim()">Add</button>
            </div>
          </div>

          <!-- Notes section -->
          <div class="notes-section">
            <h4>Notes</h4>
            <p class="section-hint">Record observations, mistakes, or lessons learned from this trade.</p>
            @if (selectedTrade.notes?.length) {
              @for (note of selectedTrade.notes; track note.id) {
                <div class="note">
                  <div class="note-content">{{ note.content }}</div>
                  <div class="note-time">{{ note.createdAt | date:'short' }}</div>
                </div>
              }
            } @else {
              <p class="empty-state small">No notes</p>
            }
            <div class="note-input">
              <textarea [(ngModel)]="newNote" placeholder="Write a note..." rows="3"></textarea>
              <button class="btn btn-sm" (click)="addNote()" [disabled]="!newNote.trim()">Add Note</button>
            </div>
          </div>
        </div>
      }
    </div>
  `,
  styles: [`
    .page-header {
      margin-bottom: 24px;
    }

    .stats-grid {
      display: grid;
      grid-template-columns: repeat(4, 1fr);
      gap: 16px;
      margin-bottom: 24px;
    }

    .stat-card {
      text-align: center;
    }

    .stat-label {
      color: var(--text-muted);
      font-size: 14px;
      margin-bottom: 8px;
    }

    .stat-value {
      font-size: 24px;
      font-weight: 600;
    }

    .card h3 {
      margin-bottom: 16px;
    }

    .empty-state {
      text-align: center;
      color: var(--text-muted);
      padding: 40px;

      &.small {
        padding: 20px;
      }
    }

    .clickable {
      cursor: pointer;
      &:hover {
        background: var(--surface-light);
      }
    }

    .selected-row {
      background: var(--surface-light);
    }

    .buy { color: var(--success); }
    .sell { color: var(--danger); }

    .tag {
      background: var(--surface-light);
      padding: 2px 8px;
      border-radius: 12px;
      font-size: 12px;
      margin-right: 4px;
    }

    .trade-detail {
      margin-top: 20px;
    }

    .detail-header h3 {
      display: flex;
      align-items: center;
      gap: 8px;
    }

    .volume-badge {
      font-size: 13px;
      font-weight: 400;
      color: var(--text-muted);
    }

    .trade-id {
      font-size: 13px;
      font-weight: 400;
      color: var(--text-muted);
      margin-left: auto;
    }

    .section-hint {
      font-size: 12px;
      color: var(--text-muted);
      margin: 0 0 12px;
    }

    .detail-grid {
      display: grid;
      grid-template-columns: repeat(3, 1fr);
      gap: 16px;
      margin-bottom: 20px;
    }

    .detail-item {
      .label {
        display: block;
        color: var(--text-muted);
        font-size: 12px;
        margin-bottom: 4px;
      }
      .value {
        font-weight: 500;
      }
    }

    h4 {
      margin: 16px 0 8px;
      font-size: 14px;
      border-bottom: 1px solid var(--border);
      padding-bottom: 8px;
    }

    /* Review section */
    .review-section {
      margin-bottom: 20px;
    }

    .review-fields {
      display: grid;
      grid-template-columns: repeat(2, 1fr);
      gap: 12px;
      margin-bottom: 12px;
    }

    .review-field label {
      display: block;
      font-size: 12px;
      color: var(--text-muted);
      margin-bottom: 4px;
    }

    .review-field input,
    .review-field select {
      width: 100%;
      padding: 6px 10px;
      border: 1px solid var(--border);
      border-radius: 4px;
      background: var(--surface);
      color: var(--text);
      font-size: 13px;
      box-sizing: border-box;
    }

    .star-rating {
      display: flex;
      gap: 4px;
    }

    .star {
      font-size: 22px;
      cursor: pointer;
      color: var(--border);
      transition: color 0.15s;
    }

    .star.filled {
      color: #f5a623;
    }

    .star:hover {
      color: #f5a623;
    }

    .btn {
      padding: 6px 16px;
      border: 1px solid var(--border);
      border-radius: 4px;
      background: var(--surface);
      color: var(--text);
      cursor: pointer;
      font-size: 13px;
    }

    .btn:disabled {
      opacity: 0.5;
      cursor: not-allowed;
    }

    .btn-primary {
      background: var(--primary, #3b82f6);
      color: #fff;
      border-color: var(--primary, #3b82f6);
    }

    .btn-sm {
      padding: 4px 12px;
      font-size: 12px;
    }

    /* Tags section */
    .tags-section {
      margin-bottom: 20px;
    }

    .tags-list {
      display: flex;
      flex-wrap: wrap;
      gap: 6px;
      margin-bottom: 8px;
    }

    .tag-pill {
      display: inline-flex;
      align-items: center;
      gap: 4px;
      background: var(--surface-light);
      padding: 4px 10px;
      border-radius: 12px;
      font-size: 12px;
    }

    .tag-delete {
      cursor: pointer;
      font-size: 14px;
      line-height: 1;
      opacity: 0.6;
    }

    .tag-delete:hover {
      opacity: 1;
    }

    .no-tags {
      color: var(--text-muted);
      font-size: 13px;
    }

    .tag-input-row {
      display: flex;
      gap: 8px;
    }

    .tag-input-row input {
      flex: 1;
      padding: 4px 10px;
      border: 1px solid var(--border);
      border-radius: 4px;
      background: var(--surface);
      color: var(--text);
      font-size: 13px;
    }

    /* Notes section */
    .notes-section {
      margin-bottom: 20px;
    }

    .note {
      background: var(--surface-light);
      padding: 12px;
      border-radius: 6px;
      margin-bottom: 8px;
    }

    .note-content {
      white-space: pre-wrap;
    }

    .note-time {
      font-size: 11px;
      color: var(--text-muted);
      margin-top: 6px;
    }

    .note-input {
      margin-top: 8px;
    }

    .note-input textarea {
      width: 100%;
      padding: 8px 10px;
      border: 1px solid var(--border);
      border-radius: 4px;
      background: var(--surface);
      color: var(--text);
      font-size: 13px;
      resize: vertical;
      box-sizing: border-box;
      margin-bottom: 8px;
    }

    @media (max-width: 768px) {
      .stats-grid {
        grid-template-columns: repeat(2, 1fr);
      }

      .detail-grid {
        grid-template-columns: repeat(2, 1fr);
      }

      .review-fields {
        grid-template-columns: 1fr;
      }

      .stat-value {
        font-size: 20px;
      }

      .selected-row {
        border-color: var(--primary) !important;
        box-shadow: 0 0 12px var(--primary-glow);
      }
    }

    @media (max-width: 480px) {
      .stat-value {
        font-size: 18px;
      }

      .detail-header h3 {
        flex-wrap: wrap;
      }
    }
  `]
})
export class JournalComponent implements OnInit {
  trades: any[] = [];
  selectedTrade: any = null;
  stats = {
    totalTrades: 0,
    winRate: 0,
    profitFactor: 0,
    totalPnL: 0
  };

  reviewForm = { strategy: '', setup: '', emotion: '', rating: 0 };
  savingReview = false;
  newTag = '';
  newNote = '';

  constructor(private http: HttpClient) {}

  ngOnInit() {
    this.loadTrades();
    this.loadStats();
  }

  loadTrades() {
    this.http.get<any[]>(`${environment.apiUrl}/api/journal`).subscribe({
      next: (data) => this.trades = data
    });
  }

  loadStats() {
    this.http.get<any>(`${environment.apiUrl}/api/analytics/overview`).subscribe({
      next: (data) => {
        this.stats = {
          totalTrades: data.totalTrades,
          winRate: data.winRate,
          profitFactor: data.profitFactor,
          totalPnL: data.totalPnL
        };
      }
    });
  }

  selectTrade(trade: any) {
    this.http.get<any>(`${environment.apiUrl}/api/journal/${trade.id}`).subscribe({
      next: (data) => {
        this.selectedTrade = data;
        this.reviewForm = {
          strategy: data.strategy || '',
          setup: data.setup || '',
          emotion: data.emotion || '',
          rating: data.rating || 0
        };
        this.newTag = '';
        this.newNote = '';
      }
    });
  }

  saveReview() {
    if (!this.selectedTrade) return;
    this.savingReview = true;
    const body = {
      strategy: this.reviewForm.strategy || null,
      setup: this.reviewForm.setup || null,
      emotion: this.reviewForm.emotion || null,
      rating: this.reviewForm.rating || null
    };
    this.http.put(`${environment.apiUrl}/api/journal/${this.selectedTrade.id}`, body).subscribe({
      next: () => {
        this.savingReview = false;
        this.reloadSelectedTrade();
      },
      error: () => this.savingReview = false
    });
  }

  addTag() {
    const tag = this.newTag.trim();
    if (!tag || !this.selectedTrade) return;
    this.http.post(`${environment.apiUrl}/api/journal/${this.selectedTrade.id}/tags`, { tag }).subscribe({
      next: () => {
        this.newTag = '';
        this.reloadSelectedTrade();
      }
    });
  }

  deleteTag(tagId: number) {
    if (!this.selectedTrade) return;
    this.http.delete(`${environment.apiUrl}/api/journal/${this.selectedTrade.id}/tags/${tagId}`).subscribe({
      next: () => this.reloadSelectedTrade()
    });
  }

  addNote() {
    const content = this.newNote.trim();
    if (!content || !this.selectedTrade) return;
    this.http.post(`${environment.apiUrl}/api/journal/${this.selectedTrade.id}/notes`, { content }).subscribe({
      next: () => {
        this.newNote = '';
        this.reloadSelectedTrade();
      }
    });
  }

  formatDuration(duration: string): string {
    if (!duration) return '-';
    // Parse HH:MM:SS or HH:MM:SS.fffffff format from .NET TimeSpan
    const match = duration.match(/^(\d+)\.?(\d{2}):(\d{2}):(\d{2})/);
    if (match) {
      const days = parseInt(match[1], 10);
      const hours = parseInt(match[2], 10);
      const minutes = parseInt(match[3], 10);
      const seconds = parseInt(match[4], 10);
      const parts: string[] = [];
      if (days > 0) parts.push(`${days}d`);
      if (hours > 0) parts.push(`${hours}h`);
      if (minutes > 0) parts.push(`${minutes}m`);
      if (seconds > 0 || parts.length === 0) parts.push(`${seconds}s`);
      return parts.join(' ');
    }
    // Try simpler HH:MM:SS without days
    const simpleMatch = duration.match(/^(\d{2}):(\d{2}):(\d{2})/);
    if (simpleMatch) {
      const hours = parseInt(simpleMatch[1], 10);
      const minutes = parseInt(simpleMatch[2], 10);
      const seconds = parseInt(simpleMatch[3], 10);
      const parts: string[] = [];
      if (hours > 0) parts.push(`${hours}h`);
      if (minutes > 0) parts.push(`${minutes}m`);
      if (seconds > 0 || parts.length === 0) parts.push(`${seconds}s`);
      return parts.join(' ');
    }
    return duration;
  }

  private reloadSelectedTrade() {
    if (!this.selectedTrade) return;
    this.http.get<any>(`${environment.apiUrl}/api/journal/${this.selectedTrade.id}`).subscribe({
      next: (data) => {
        this.selectedTrade = data;
        // Also update in the trades list
        const idx = this.trades.findIndex(t => t.id === data.id);
        if (idx !== -1) this.trades[idx] = data;
      }
    });
  }
}
