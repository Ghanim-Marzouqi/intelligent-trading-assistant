import { Component, inject } from '@angular/core';
import { RouterOutlet, RouterLink, RouterLinkActive } from '@angular/router';
import { AuthService } from './auth/auth.service';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  template: `
    <div class="app-container">
      <nav class="sidebar">
        <div class="logo">
          <svg class="logo-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <polyline points="22 12 18 12 15 21 9 3 6 12 2 12"/>
          </svg>
          <span class="logo-text">Trading Assistant</span>
        </div>
        <ul class="nav-links">
          <li>
            <a routerLink="/dashboard" routerLinkActive="active">
              <svg class="nav-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                <rect x="3" y="3" width="7" height="7" rx="1"/><rect x="14" y="3" width="7" height="7" rx="1"/><rect x="3" y="14" width="7" height="7" rx="1"/><rect x="14" y="14" width="7" height="7" rx="1"/>
              </svg>
              Dashboard
            </a>
          </li>
          <li>
            <a routerLink="/positions" routerLinkActive="active">
              <svg class="nav-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                <rect x="2" y="7" width="20" height="14" rx="2" ry="2"/><path d="M16 21V5a2 2 0 0 0-2-2h-4a2 2 0 0 0-2 2v16"/>
              </svg>
              Positions
            </a>
          </li>
          <li>
            <a routerLink="/alerts" routerLinkActive="active">
              <svg class="nav-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                <path d="M18 8A6 6 0 0 0 6 8c0 7-3 9-3 9h18s-3-2-3-9"/><path d="M13.73 21a2 2 0 0 1-3.46 0"/>
              </svg>
              Alerts
            </a>
          </li>
          <li>
            <a routerLink="/journal" routerLinkActive="active">
              <svg class="nav-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                <path d="M4 19.5A2.5 2.5 0 0 1 6.5 17H20"/><path d="M6.5 2H20v20H6.5A2.5 2.5 0 0 1 4 19.5v-15A2.5 2.5 0 0 1 6.5 2z"/>
              </svg>
              Journal
            </a>
          </li>
          <li>
            <a routerLink="/analytics" routerLinkActive="active">
              <svg class="nav-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                <line x1="18" y1="20" x2="18" y2="10"/><line x1="12" y1="20" x2="12" y2="4"/><line x1="6" y1="20" x2="6" y2="14"/>
              </svg>
              Analytics
            </a>
          </li>
          <li>
            <a routerLink="/ai" routerLinkActive="active">
              <svg class="nav-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                <path d="M12 2a4 4 0 0 1 4 4v1a1 1 0 0 1-1 1H9a1 1 0 0 1-1-1V6a4 4 0 0 1 4-4z"/><path d="M9 8v1a3 3 0 0 0 6 0V8"/><path d="M12 14v3"/><circle cx="12" cy="20" r="2"/><path d="M5 11h2"/><path d="M17 11h2"/>
              </svg>
              AI Analysis
            </a>
          </li>
        </ul>
        <div class="sidebar-footer">
          <button class="logout-btn" (click)="logout()">
            <svg class="nav-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
              <path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4"/><polyline points="16 17 21 12 16 7"/><line x1="21" y1="12" x2="9" y2="12"/>
            </svg>
            Logout
          </button>
        </div>
      </nav>
      <main class="main-content">
        <router-outlet></router-outlet>
      </main>
    </div>
  `,
  styles: [`
    .app-container {
      display: flex;
      min-height: 100vh;
    }

    .sidebar {
      width: 240px;
      background: var(--surface);
      border-right: 1px solid var(--border);
      padding: 20px 0;
      display: flex;
      flex-direction: column;
    }

    .logo {
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 0 20px 24px;
      border-bottom: 1px solid var(--border);
      margin-bottom: 20px;
    }

    .logo-icon {
      width: 28px;
      height: 28px;
      color: var(--primary);
    }

    .logo-text {
      font-weight: 700;
      font-size: 16px;
      color: var(--text-bright);
      letter-spacing: -0.01em;
    }

    .nav-links {
      list-style: none;
      padding: 0;
      flex: 1;
    }

    .nav-icon {
      width: 18px;
      height: 18px;
      flex-shrink: 0;
    }

    .nav-links a {
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 11px 20px;
      color: var(--text-muted);
      font-size: 14px;
      font-weight: 500;
      transition: all 0.2s;

      &:hover {
        background: var(--surface-light);
        color: var(--text);
      }

      &.active {
        background: var(--primary);
        color: white;
        box-shadow: inset 3px 0 0 rgba(255,255,255,0.3);
      }
    }

    .main-content {
      flex: 1;
      padding: 24px 32px;
      overflow-y: auto;
    }

    .sidebar-footer {
      border-top: 1px solid var(--border);
      padding-top: 12px;
    }

    .logout-btn {
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 11px 20px;
      width: 100%;
      background: none;
      border: none;
      color: var(--text-muted);
      font-size: 14px;
      cursor: pointer;
      transition: all 0.2s;

      &:hover {
        background: var(--surface-light);
        color: var(--danger);
      }
    }
  `]
})
export class AppComponent {
  private authService = inject(AuthService);

  logout() {
    this.authService.logout();
  }
}
