import { Injectable } from '@angular/core';
import { BehaviorSubject } from 'rxjs';

export interface Toast {
  id: number;
  message: string;
  type: 'error' | 'warning' | 'info';
}

@Injectable({ providedIn: 'root' })
export class NotificationService {
  private nextId = 0;
  private toasts: Toast[] = [];
  readonly toasts$ = new BehaviorSubject<Toast[]>([]);

  show(message: string, type: Toast['type'] = 'error', durationMs = 5000): void {
    const toast: Toast = { id: this.nextId++, message, type };
    this.toasts = [...this.toasts, toast];
    this.toasts$.next(this.toasts);

    setTimeout(() => this.dismiss(toast.id), durationMs);
  }

  dismiss(id: number): void {
    this.toasts = this.toasts.filter(t => t.id !== id);
    this.toasts$.next(this.toasts);
  }
}
