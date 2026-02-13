import { Component, HostListener, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Subscription } from 'rxjs';
import { ConfirmDialogService, ConfirmState } from '../services/confirm-dialog.service';

@Component({
  selector: 'app-confirm-dialog',
  standalone: true,
  imports: [CommonModule],
  template: `
    @if (state.visible) {
      <div class="confirm-backdrop" (click)="onBackdropClick()">
        <div class="confirm-card" (click)="$event.stopPropagation()">
          @if (state.options.title) {
            <h3 class="confirm-title">{{ state.options.title }}</h3>
          }
          <p class="confirm-message">{{ state.options.message }}</p>
          <div class="confirm-actions">
            <button class="confirm-cancel" (click)="cancel()">
              {{ state.options.cancelText || 'Cancel' }}
            </button>
            <button
              class="confirm-btn"
              [class.danger]="(state.options.variant || 'danger') === 'danger'"
              [class.warning]="state.options.variant === 'warning'"
              (click)="ok()">
              {{ state.options.confirmText || 'Confirm' }}
            </button>
          </div>
        </div>
      </div>
    }
  `,
  styles: [`
    .confirm-backdrop {
      position: fixed;
      inset: 0;
      z-index: 1000;
      background: rgba(0, 0, 0, 0.6);
      display: flex;
      align-items: center;
      justify-content: center;
      animation: fadeIn 0.15s ease;
    }

    .confirm-card {
      background: var(--surface);
      border: 1px solid var(--border);
      border-radius: var(--radius-lg);
      padding: 24px;
      max-width: 420px;
      width: calc(100% - 32px);
      box-shadow: 0 16px 48px rgba(0, 0, 0, 0.5);
      animation: slideUp 0.15s ease;
    }

    .confirm-title {
      font-size: 16px;
      font-weight: 600;
      color: var(--text-bright);
      margin-bottom: 8px;
    }

    .confirm-message {
      font-size: 14px;
      color: var(--text-muted);
      line-height: 1.5;
      margin-bottom: 20px;
    }

    .confirm-actions {
      display: flex;
      gap: 8px;
      justify-content: flex-end;
    }

    .confirm-cancel {
      padding: 8px 16px;
      font-size: 14px;
      font-weight: 500;
      border-radius: var(--radius);
      background: transparent;
      border: 1px solid var(--border);
      color: var(--text);
      cursor: pointer;
      transition: all 0.2s;
    }

    .confirm-cancel:hover {
      background: var(--surface-light);
      border-color: var(--border-light);
    }

    .confirm-btn {
      padding: 8px 16px;
      font-size: 14px;
      font-weight: 500;
      border-radius: var(--radius);
      border: none;
      color: white;
      cursor: pointer;
      transition: all 0.2s;
    }

    .confirm-btn.danger {
      background: var(--danger);
    }

    .confirm-btn.danger:hover {
      background: #dc2626;
    }

    .confirm-btn.warning {
      background: var(--warning);
      color: #000;
    }

    .confirm-btn.warning:hover {
      background: #d97706;
    }

    @keyframes fadeIn {
      from { opacity: 0; }
      to { opacity: 1; }
    }

    @keyframes slideUp {
      from { opacity: 0; transform: translateY(8px) scale(0.98); }
      to { opacity: 1; transform: translateY(0) scale(1); }
    }
  `]
})
export class ConfirmDialogComponent implements OnInit, OnDestroy {
  state: ConfirmState = { visible: false, options: { message: '' }, resolve: null };
  private sub: Subscription | null = null;

  constructor(private dialog: ConfirmDialogService) {}

  ngOnInit() {
    this.sub = this.dialog.dialogState$.subscribe(s => this.state = s);
  }

  ngOnDestroy() {
    this.sub?.unsubscribe();
  }

  @HostListener('document:keydown.escape')
  onEscape() {
    if (this.state.visible) {
      this.cancel();
    }
  }

  onBackdropClick() {
    this.cancel();
  }

  cancel() {
    this.dialog.close(false);
  }

  ok() {
    this.dialog.close(true);
  }
}
