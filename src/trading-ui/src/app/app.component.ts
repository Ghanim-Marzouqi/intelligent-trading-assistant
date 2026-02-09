import { Component } from '@angular/core';
import { RouterOutlet, RouterLink, RouterLinkActive } from '@angular/router';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  template: `
    <div class="app-container">
      <nav class="sidebar">
        <div class="logo">
          <span class="logo-icon">📈</span>
          <span class="logo-text">Trading Assistant</span>
        </div>
        <ul class="nav-links">
          <li>
            <a routerLink="/dashboard" routerLinkActive="active">
              <span class="icon">📊</span> Dashboard
            </a>
          </li>
          <li>
            <a routerLink="/positions" routerLinkActive="active">
              <span class="icon">💼</span> Positions
            </a>
          </li>
          <li>
            <a routerLink="/alerts" routerLinkActive="active">
              <span class="icon">🔔</span> Alerts
            </a>
          </li>
          <li>
            <a routerLink="/journal" routerLinkActive="active">
              <span class="icon">📝</span> Journal
            </a>
          </li>
          <li>
            <a routerLink="/analytics" routerLinkActive="active">
              <span class="icon">📈</span> Analytics
            </a>
          </li>
        </ul>
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
      font-size: 24px;
    }

    .logo-text {
      font-weight: 600;
      font-size: 16px;
    }

    .nav-links {
      list-style: none;
      padding: 0;
    }

    .nav-links a {
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 12px 20px;
      color: var(--text-muted);
      transition: all 0.2s;

      &:hover {
        background: var(--surface-light);
        color: var(--text);
      }

      &.active {
        background: var(--primary);
        color: white;
      }
    }

    .icon {
      font-size: 18px;
    }

    .main-content {
      flex: 1;
      padding: 24px;
      overflow-y: auto;
    }
  `]
})
export class AppComponent {}
