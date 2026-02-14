import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { NotificationService, Toast } from '../services/notification.service';

@Component({
  selector: 'app-notification-toast',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="toast-container">
      @for (toast of toasts; track toast.id) {
        <div class="toast" [class]="'toast-' + toast.type">
          <span class="toast-message">{{ toast.message }}</span>
          <button class="toast-close" (click)="dismiss(toast.id)">&times;</button>
        </div>
      }
    </div>
  `,
  styles: [`
    .toast-container {
      position: fixed;
      top: 16px;
      right: 16px;
      z-index: 9999;
      display: flex;
      flex-direction: column;
      gap: 8px;
      max-width: 400px;
    }

    .toast {
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 12px 16px;
      border-radius: 8px;
      font-size: 14px;
      box-shadow: 0 4px 12px rgba(0, 0, 0, 0.3);
      animation: slideIn 0.2s ease-out;
    }

    .toast-error {
      background: #991b1b;
      color: #fecaca;
      border: 1px solid #dc2626;
    }

    .toast-warning {
      background: #92400e;
      color: #fef3c7;
      border: 1px solid #f59e0b;
    }

    .toast-info {
      background: #1e3a5f;
      color: #bfdbfe;
      border: 1px solid #3b82f6;
    }

    .toast-message {
      flex: 1;
    }

    .toast-close {
      background: none;
      border: none;
      color: inherit;
      font-size: 18px;
      cursor: pointer;
      padding: 0 4px;
      opacity: 0.7;
      line-height: 1;
    }

    .toast-close:hover {
      opacity: 1;
    }

    @keyframes slideIn {
      from {
        transform: translateX(100%);
        opacity: 0;
      }
      to {
        transform: translateX(0);
        opacity: 1;
      }
    }

    @media (max-width: 480px) {
      .toast-container {
        left: 16px;
        right: 16px;
        max-width: none;
      }
    }
  `]
})
export class NotificationToastComponent {
  toasts: Toast[] = [];

  constructor(private notificationService: NotificationService) {
    this.notificationService.toasts$.subscribe(toasts => {
      this.toasts = toasts;
    });
  }

  dismiss(id: number) {
    this.notificationService.dismiss(id);
  }
}
