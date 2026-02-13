import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { environment } from '../../environments/environment';

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  private readonly TOKEN_KEY = 'trading_jwt';

  constructor(private http: HttpClient, private router: Router) {}

  login(password: string): Promise<boolean> {
    return new Promise((resolve) => {
      this.http.post<{ token: string }>(`${environment.apiUrl}/api/auth/login`, { password }).subscribe({
        next: (response) => {
          localStorage.setItem(this.TOKEN_KEY, response.token);
          resolve(true);
        },
        error: () => resolve(false)
      });
    });
  }

  getToken(): string | null {
    return localStorage.getItem(this.TOKEN_KEY);
  }

  isAuthenticated(): boolean {
    const token = this.getToken();
    if (!token) return false;

    try {
      let base64 = token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/');
      const pad = base64.length % 4;
      if (pad) {
        base64 += '='.repeat(4 - pad);
      }
      const payload = JSON.parse(atob(base64));
      return payload.exp * 1000 > Date.now();
    } catch {
      return false;
    }
  }

  logout(): void {
    localStorage.removeItem(this.TOKEN_KEY);
    this.router.navigate(['/login']);
  }
}
