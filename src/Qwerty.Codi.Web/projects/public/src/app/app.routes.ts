import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    loadComponent: () => import('./welcome').then((module) => module.Welcome),
    title: 'Codì',
  },
  {
    path: 'join',
    loadComponent: () => import('./join/join').then((module) => module.Join),
    title: 'Entra nella lezione',
  },
  {
    path: 'lezione',
    loadComponent: () => import('./session/session').then((module) => module.ParticipantSession),
    title: 'La mia lezione',
  },
  { path: '**', redirectTo: '' },
];
