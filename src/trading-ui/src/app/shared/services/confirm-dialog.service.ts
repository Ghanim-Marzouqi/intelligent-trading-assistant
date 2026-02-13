import { Injectable } from '@angular/core';
import { BehaviorSubject } from 'rxjs';

export interface ConfirmOptions {
  title?: string;
  message: string;
  confirmText?: string;
  cancelText?: string;
  variant?: 'danger' | 'warning';
}

export interface ConfirmState {
  visible: boolean;
  options: ConfirmOptions;
  resolve: ((value: boolean) => void) | null;
}

@Injectable({ providedIn: 'root' })
export class ConfirmDialogService {
  private state$ = new BehaviorSubject<ConfirmState>({
    visible: false,
    options: { message: '' },
    resolve: null,
  });

  readonly dialogState$ = this.state$.asObservable();

  confirm(options: ConfirmOptions): Promise<boolean> {
    return new Promise<boolean>((resolve) => {
      this.state$.next({
        visible: true,
        options,
        resolve,
      });
    });
  }

  close(result: boolean) {
    const current = this.state$.value;
    if (current.resolve) {
      current.resolve(result);
    }
    this.state$.next({
      visible: false,
      options: { message: '' },
      resolve: null,
    });
  }
}
