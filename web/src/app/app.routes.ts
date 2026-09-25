import { Routes } from '@angular/router';
import { requireMfa } from './core/auth';
import { ShellComponent } from './shell/shell';

export const routes: Routes = [
  { path: 'login', loadComponent: () => import('./features/auth/login').then((m) => m.LoginComponent) },
  { path: 'setup', loadComponent: () => import('./features/auth/setup').then((m) => m.SetupComponent) },
  { path: 'mfa-setup', loadComponent: () => import('./features/auth/mfa-setup').then((m) => m.MfaSetupComponent) },
  {
    path: '',
    component: ShellComponent,
    canActivate: [requireMfa],
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
      { path: 'dashboard', loadComponent: () => import('./features/dashboard/dashboard').then((m) => m.DashboardComponent) },
      { path: 'monthly', loadComponent: () => import('./features/monthly/monthly').then((m) => m.MonthlyComponent) },
      { path: 'annual', loadComponent: () => import('./features/annual/annual').then((m) => m.AnnualComponent) },
      { path: 'transactions', loadComponent: () => import('./features/transactions/transactions').then((m) => m.TransactionsComponent) },
      { path: 'recurring', loadComponent: () => import('./features/recurring/recurring').then((m) => m.RecurringComponent) },
      { path: 'budgets', loadComponent: () => import('./features/budgets/budgets').then((m) => m.BudgetsComponent) },
      { path: 'goals', loadComponent: () => import('./features/goals/goals').then((m) => m.GoalsComponent) },
      { path: 'accounts', loadComponent: () => import('./features/accounts/accounts').then((m) => m.AccountsComponent) },
      { path: 'categories', loadComponent: () => import('./features/categories/categories').then((m) => m.CategoriesComponent) },
      { path: 'import', loadComponent: () => import('./features/import/import').then((m) => m.ImportComponent) },
      { path: 'settings', loadComponent: () => import('./features/settings/settings').then((m) => m.SettingsComponent) },
    ],
  },
  { path: '**', redirectTo: '' },
];
