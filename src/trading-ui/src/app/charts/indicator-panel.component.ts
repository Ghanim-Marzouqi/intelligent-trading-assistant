import { Component, Input, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';

export type IndicatorType = 'SMA' | 'EMA' | 'RSI' | 'VOLUME';

export interface IndicatorConfig {
  type: IndicatorType;
  period: number;
  enabled: boolean;
  color: string;
}

@Component({
  selector: 'app-indicator-panel',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="indicator-panel">
      <h3 class="panel-title">Indicators</h3>
      @for (ind of indicators; track ind.type; let i = $index) {
        <div class="indicator-row">
          <label class="toggle-label">
            <input
              type="checkbox"
              [checked]="ind.enabled"
              (change)="toggleIndicator(i)" />
            <span class="color-dot" [style.background]="ind.color"></span>
            <span class="ind-name">{{ ind.type }}</span>
          </label>
          @if (ind.type !== 'VOLUME') {
            <input
              type="number"
              class="period-input"
              [ngModel]="ind.period"
              (ngModelChange)="updatePeriod(i, $event)"
              [disabled]="!ind.enabled"
              min="2"
              max="200" />
          }
        </div>
      }
    </div>
  `,
  styles: [`
    .indicator-panel {
      display: flex;
      flex-direction: column;
      gap: 8px;
    }

    .panel-title {
      font-size: 14px;
      font-weight: 700;
      color: var(--text-bright);
      margin: 0;
    }

    .indicator-row {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 8px;
      padding: 6px 0;
    }

    .toggle-label {
      display: flex;
      align-items: center;
      gap: 8px;
      cursor: pointer;
      font-size: 13px;
      color: var(--text);
    }

    .toggle-label input[type="checkbox"] {
      width: 14px;
      height: 14px;
      accent-color: var(--primary);
      cursor: pointer;
    }

    .color-dot {
      width: 10px;
      height: 10px;
      border-radius: 50%;
      flex-shrink: 0;
    }

    .ind-name {
      font-weight: 600;
      font-size: 12px;
      font-family: var(--font-mono);
    }

    .period-input {
      width: 52px;
      padding: 4px 6px;
      font-size: 12px;
      text-align: center;
      background: var(--surface-light);
      border: 1px solid var(--border);
      border-radius: 4px;
      color: var(--text);
      font-family: var(--font-mono);
    }
    .period-input:disabled {
      opacity: 0.4;
    }
    .period-input:focus {
      outline: none;
      border-color: var(--primary);
    }
  `]
})
export class IndicatorPanelComponent {
  @Input() indicators: IndicatorConfig[] = [];
  @Output() indicatorsChanged = new EventEmitter<IndicatorConfig[]>();

  toggleIndicator(index: number) {
    const copy = this.indicators.map((ind, i) =>
      i === index ? { ...ind, enabled: !ind.enabled } : { ...ind }
    );
    this.indicatorsChanged.emit(copy);
  }

  updatePeriod(index: number, period: number) {
    if (period < 2 || period > 200) return;
    const copy = this.indicators.map((ind, i) =>
      i === index ? { ...ind, period } : { ...ind }
    );
    this.indicatorsChanged.emit(copy);
  }
}
