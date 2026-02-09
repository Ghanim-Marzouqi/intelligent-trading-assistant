import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', redirectTo: '/dashboard', pathMatch: 'full' },
  {
    path: 'dashboard',
    loadComponent: () => import('./dashboard/dashboard.component').then(m => m.DashboardComponent)
  },
  {
    path: 'positions',
    loadComponent: () => import('./positions/positions.component').then(m => m.PositionsComponent)
  },
  {
    path: 'alerts',
    loadComponent: () => import('./alerts/alerts.component').then(m => m.AlertsComponent)
  },
  {
    path: 'journal',
    loadComponent: () => import('./journal/journal.component').then(m => m.JournalComponent)
  },
  {
    path: 'analytics',
    loadComponent: () => import('./analytics/analytics.component').then(m => m.AnalyticsComponent)
  }
];
