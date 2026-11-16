import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: 'dashboard',
    title: 'Overview · Snack Counter',
    loadComponent: () => import('./dashboard').then((m) => m.Dashboard),
  },
  {
    path: 'volunteer',
    title: 'Volunteer · Snack Counter',
    loadComponent: () => import('./volunteer').then((m) => m.Volunteer),
  },
  {
    path: 'expenses',
    title: 'Expenses · Snack Counter',
    loadComponent: () => import('./expenses').then((m) => m.Expenses),
  },
  {
    path: 'hr',
    title: 'HR demo · Snack Counter',
    loadComponent: () => import('./hr').then((m) => m.Hr),
  },
  { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
  { path: '**', redirectTo: 'dashboard' },
];
