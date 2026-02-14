import { Component, Input, Output, EventEmitter, HostListener, ElementRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';

export interface SymbolInfo {
  name: string;
  minVolume: number;
  maxVolume: number;
  volumeStep: number;
  digits: number;
  category: string;
  description: string;
}

@Component({
  selector: 'app-symbol-bar',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="symbol-bar">
      <div class="symbol-chips">
        @for (sym of watchlistSymbols; track sym) {
          <button
            class="chip"
            [class.active]="sym === selectedSymbol"
            (click)="symbolChanged.emit(sym)">
            {{ sym }}
          </button>
        }

        <div class="search-wrap">
          <input
            type="text"
            class="search-input"
            [(ngModel)]="searchText"
            (focus)="showDropdown = true"
            (keydown)="onKeydown($event)"
            placeholder="+ Search"
            autocomplete="off" />
          @if (showDropdown && searchText) {
            <div class="search-dropdown">
              <div class="category-tabs">
                @for (cat of categories; track cat) {
                  <button
                    class="cat-tab"
                    [class.active]="activeCategory === cat"
                    (mousedown)="$event.preventDefault()"
                    (click)="activeCategory = cat">{{ cat }}</button>
                }
              </div>
              <div class="symbol-list">
                @for (group of groupedFiltered; track group.category) {
                  @if (activeCategory === 'All') {
                    <div class="symbol-group-header">{{ group.category }}</div>
                  }
                  @for (s of group.symbols; track s.name) {
                    <div
                      class="symbol-option"
                      (mousedown)="$event.preventDefault()"
                      (click)="selectSymbol(s.name)">
                      <span class="symbol-name">{{ s.name }}</span>
                      <span class="symbol-desc">{{ s.description }}</span>
                    </div>
                  }
                }
                @if (groupedFiltered.length === 0) {
                  <div class="no-match">No symbols found</div>
                }
              </div>
            </div>
          }
        </div>
      </div>

      <div class="timeframe-btns">
        @for (tf of timeframes; track tf) {
          <button
            class="tf-btn"
            [class.active]="timeframe === tf"
            (click)="timeframeChanged.emit(tf)">
            {{ tf }}
          </button>
        }
      </div>
    </div>
  `,
  styles: [`
    .symbol-bar {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 12px;
      padding: 10px 16px;
      background: var(--surface);
      border: 1px solid var(--border);
      border-radius: var(--radius);
    }

    .symbol-chips {
      display: flex;
      align-items: center;
      gap: 6px;
      overflow-x: auto;
      scrollbar-width: none;
      flex: 1;
      min-width: 0;
    }
    .symbol-chips::-webkit-scrollbar { display: none; }

    .chip {
      flex: 0 0 auto;
      padding: 6px 14px;
      font-size: 13px;
      font-weight: 600;
      font-family: var(--font-mono);
      background: var(--surface-light);
      color: var(--text-muted);
      border: 1px solid var(--border);
      border-radius: 20px;
      cursor: pointer;
      transition: all 0.15s;
      white-space: nowrap;
    }
    .chip:hover {
      color: var(--text);
      border-color: var(--text-muted);
    }
    .chip.active {
      background: var(--primary);
      color: white;
      border-color: var(--primary);
    }

    .search-wrap {
      position: relative;
      flex: 0 0 auto;
    }
    .search-input {
      width: 120px;
      padding: 6px 12px;
      font-size: 13px;
      background: var(--surface-light);
      border: 1px solid var(--border);
      border-radius: 20px;
      color: var(--text);
      transition: width 0.2s;
    }
    .search-input:focus {
      width: 180px;
      border-color: var(--primary);
      outline: none;
    }

    .search-dropdown {
      position: absolute;
      top: 100%;
      left: 0;
      right: 0;
      min-width: 260px;
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
      padding: 6px 10px;
      font-size: 10px;
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
    }
    .cat-tab:hover { color: var(--text); }
    .cat-tab.active {
      color: var(--primary);
      border-bottom-color: var(--primary);
    }

    .symbol-list {
      max-height: 240px;
      overflow-y: auto;
    }
    .symbol-group-header {
      padding: 4px 12px;
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
      padding: 7px 12px;
      cursor: pointer;
      transition: background 0.1s;
    }
    .symbol-option:hover { background: var(--surface-light); }
    .symbol-name {
      font-weight: 600;
      font-size: 13px;
      min-width: 80px;
      font-family: var(--font-mono);
    }
    .symbol-desc {
      font-size: 11px;
      color: var(--text-muted);
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
    .no-match {
      padding: 16px 12px;
      text-align: center;
      color: var(--text-muted);
      font-size: 12px;
    }

    .timeframe-btns {
      display: flex;
      gap: 0;
      border-radius: var(--radius);
      overflow: hidden;
      border: 1px solid var(--border);
      flex-shrink: 0;
    }
    .tf-btn {
      padding: 6px 14px;
      font-size: 12px;
      font-weight: 600;
      background: var(--surface-light);
      color: var(--text-muted);
      border: none;
      border-radius: 0;
      cursor: pointer;
      transition: all 0.2s;
    }
    .tf-btn:not(:last-child) {
      border-right: 1px solid var(--border);
    }
    .tf-btn:hover { color: var(--text); }
    .tf-btn.active {
      background: var(--primary);
      color: white;
    }

    @media (max-width: 768px) {
      .symbol-bar {
        flex-direction: column;
        align-items: stretch;
        gap: 8px;
        padding: 8px 12px;
      }
      .timeframe-btns {
        align-self: flex-end;
      }
    }
  `]
})
export class SymbolBarComponent {
  @Input() watchlistSymbols: string[] = [];
  @Input() allSymbols: SymbolInfo[] = [];
  @Input() selectedSymbol = '';
  @Input() timeframe = 'H1';
  @Output() symbolChanged = new EventEmitter<string>();
  @Output() timeframeChanged = new EventEmitter<string>();

  timeframes = ['M15', 'H1', 'H4', 'D1'];
  searchText = '';
  showDropdown = false;
  activeCategory = 'All';

  constructor(private elRef: ElementRef) {}

  get categories(): string[] {
    const cats = new Set(this.allSymbols.map(s => s.category));
    return ['All', ...Array.from(cats).sort()];
  }

  get filteredSymbols(): SymbolInfo[] {
    const q = this.searchText.toLowerCase();
    let list = this.allSymbols;
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

  get groupedFiltered(): { category: string; symbols: SymbolInfo[] }[] {
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

  selectSymbol(name: string) {
    this.searchText = '';
    this.showDropdown = false;
    this.symbolChanged.emit(name);
  }

  onKeydown(event: KeyboardEvent) {
    if (event.key === 'Escape') {
      this.showDropdown = false;
      return;
    }
    if (event.key === 'Enter') {
      const filtered = this.filteredSymbols;
      if (filtered.length === 1) {
        this.selectSymbol(filtered[0].name);
      }
    }
  }

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent) {
    if (this.showDropdown && !this.elRef.nativeElement.querySelector('.search-wrap')?.contains(event.target as Node)) {
      this.showDropdown = false;
    }
  }
}
