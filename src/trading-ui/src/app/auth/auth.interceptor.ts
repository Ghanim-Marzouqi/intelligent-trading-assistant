import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { tap } from 'rxjs';
import { AuthService } from './auth.service';

let logoutInProgress = false;

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const authService = inject(AuthService);
  const router = inject(Router);

  // Don't add token to login requests
  if (req.url.includes('/api/auth/login')) {
    return next(req);
  }

  const token = authService.getToken();
  if (token) {
    req = req.clone({
      setHeaders: {
        Authorization: `Bearer ${token}`
      }
    });
  }

  return next(req).pipe(
    tap({
      error: (error) => {
        if (error.status === 401 && !logoutInProgress) {
          logoutInProgress = true;
          authService.logout();
          router.navigate(['/login']).then(() => {
            logoutInProgress = false;
          });
        }
      }
    })
  );
};
