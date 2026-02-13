import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from './auth.service';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="login-container">
      <div class="login-card">
        <div class="login-header">
          <span class="logo-icon">📈</span>
          <h1>Trading Assistant</h1>
        </div>
        <form (ngSubmit)="onSubmit()">
          <div class="form-group">
            <label for="password">Password</label>
            <input
              id="password"
              type="password"
              [(ngModel)]="password"
              name="password"
              placeholder="Enter password"
              [class.error]="errorMessage"
              autocomplete="current-password"
            />
          </div>
          @if (errorMessage) {
            <div class="error-message">{{ errorMessage }}</div>
          }
          <button type="submit" [disabled]="loading">
            {{ loading ? 'Signing in...' : 'Sign In' }}
          </button>
        </form>
      </div>
    </div>
  `,
  styles: [`
    .login-container {
      display: flex;
      align-items: center;
      justify-content: center;
      min-height: 100vh;
      background: var(--background);
    }

    .login-card {
      background: var(--surface);
      border: 1px solid var(--border);
      border-radius: 12px;
      padding: 40px;
      width: 100%;
      max-width: 400px;
    }

    .login-header {
      text-align: center;
      margin-bottom: 32px;
    }

    .logo-icon {
      font-size: 48px;
      display: block;
      margin-bottom: 12px;
    }

    h1 {
      font-size: 24px;
      font-weight: 600;
    }

    .form-group {
      margin-bottom: 20px;
    }

    label {
      display: block;
      margin-bottom: 8px;
      color: var(--text-muted);
      font-size: 14px;
    }

    input {
      width: 100%;
      padding: 12px;
      border: 1px solid var(--border);
      border-radius: 6px;
      background: var(--background);
      color: var(--text);
      font-size: 16px;
      box-sizing: border-box;

      &:focus {
        outline: none;
        border-color: var(--primary);
      }

      &.error {
        border-color: var(--danger);
      }
    }

    .error-message {
      color: var(--danger);
      font-size: 14px;
      margin-bottom: 16px;
    }

    button {
      width: 100%;
      padding: 12px;
      background: var(--primary);
      color: white;
      border: none;
      border-radius: 6px;
      font-size: 16px;
      font-weight: 500;
      cursor: pointer;
      transition: opacity 0.2s;

      &:hover:not(:disabled) {
        opacity: 0.9;
      }

      &:disabled {
        opacity: 0.6;
        cursor: not-allowed;
      }
    }
  `]
})
export class LoginComponent {
  password = '';
  loading = false;
  errorMessage = '';

  constructor(private authService: AuthService, private router: Router) {}

  async onSubmit() {
    this.loading = true;
    this.errorMessage = '';

    try {
      const success = await this.authService.login(this.password);

      if (success) {
        this.router.navigate(['/dashboard']);
      } else {
        this.errorMessage = 'Invalid password';
      }
    } catch (error: any) {
        this.errorMessage = error?.message || 'Login failed';
    } finally {
        this.loading = false;
    }
  }
}
