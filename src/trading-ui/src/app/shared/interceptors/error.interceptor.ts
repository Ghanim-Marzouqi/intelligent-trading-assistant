import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { NotificationService } from '../services/notification.service';

export const errorInterceptor: HttpInterceptorFn = (req, next) => {
  const notifications = inject(NotificationService);

  return next(req).pipe(
    catchError(error => {
      // 401 is handled by the auth interceptor
      if (error.status === 401) {
        return throwError(() => error);
      }

      if (error.status === 0) {
        notifications.show('Connection lost. Please check your network.', 'error');
      } else if (error.status === 400) {
        const message = error.error?.error
          ?? error.error?.title
          ?? 'Invalid request';
        notifications.show(message, 'warning');
      } else if (error.status === 403) {
        notifications.show('Access denied', 'error');
      } else if (error.status === 404) {
        notifications.show('Resource not found', 'warning');
      } else if (error.status === 429) {
        notifications.show('Too many requests. Please slow down.', 'warning');
      } else if (error.status >= 500) {
        notifications.show('Server error. Please try again later.', 'error');
      }

      return throwError(() => error);
    })
  );
};
