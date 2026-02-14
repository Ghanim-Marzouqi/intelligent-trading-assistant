import { Component, Input, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';

export interface PriceLevel {
  id: string;
  price: number;
  color: string;
  label: string;
}

@Component({
  selector: 'app-drawing-levels',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="levels-panel">
      <h3 class="panel-title">Price Levels</h3>

      @for (level of levels; track level.id; let i = $index) {
        <div class="level-row">
          <span class="level-dot" [style.background]="level.color"></span>
          <span class="level-price">{{ level.price.toFixed(digits) }}</span>
          <input
            type="text"
            class="level-label"
            [ngModel]="level.label"
            (ngModelChange)="updateLabel(i, $event)"
            placeholder="Label" />
          <button class="remove-btn" (click)="removeLevel(i)">&times;</button>
        </div>
      }

      <button class="add-btn" (click)="addLevel()">
        + Add Level
      </button>
    </div>
  `,
  styles: [`
    .levels-panel {
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

    .level-row {
      display: flex;
      align-items: center;
      gap: 8px;
    }

    .level-dot {
      width: 10px;
      height: 10px;
      border-radius: 50%;
      flex-shrink: 0;
    }

    .level-price {
      font-family: var(--font-mono);
      font-size: 12px;
      font-weight: 600;
      color: var(--text);
      min-width: 70px;
    }

    .level-label {
      flex: 1;
      padding: 4px 8px;
      font-size: 12px;
      background: var(--surface-light);
      border: 1px solid var(--border);
      border-radius: 4px;
      color: var(--text);
      min-width: 0;
    }
    .level-label:focus {
      outline: none;
      border-color: var(--primary);
    }

    .remove-btn {
      padding: 2px 6px;
      font-size: 16px;
      background: none;
      color: var(--text-muted);
      border: none;
      cursor: pointer;
      border-radius: 4px;
      line-height: 1;
    }
    .remove-btn:hover {
      color: var(--danger);
      background: var(--surface-light);
    }

    .add-btn {
      padding: 6px 12px;
      font-size: 12px;
      font-weight: 600;
      background: var(--surface-light);
      border: 1px solid var(--border);
      border-radius: var(--radius);
      color: var(--text-muted);
      cursor: pointer;
      transition: all 0.15s;
    }
    .add-btn:hover {
      color: var(--text);
      border-color: var(--primary);
    }
  `]
})
export class DrawingLevelsComponent {
  @Input() levels: PriceLevel[] = [];
  @Input() currentPrice = 0;
  @Input() digits = 5;
  @Output() levelsChanged = new EventEmitter<PriceLevel[]>();

  addLevel() {
    const copy = [
      ...this.levels,
      {
        id: crypto.randomUUID(),
        price: this.currentPrice || 1.0,
        color: '#3b82f6',
        label: '',
      }
    ];
    this.levelsChanged.emit(copy);
  }

  removeLevel(index: number) {
    const copy = this.levels.filter((_, i) => i !== index);
    this.levelsChanged.emit(copy);
  }

  updateLabel(index: number, label: string) {
    const copy = this.levels.map((lvl, i) =>
      i === index ? { ...lvl, label } : { ...lvl }
    );
    this.levelsChanged.emit(copy);
  }
}
