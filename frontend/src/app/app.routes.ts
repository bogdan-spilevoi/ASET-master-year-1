import { Routes } from '@angular/router';
import { authGuard } from './core/auth/auth.guard';

export const routes: Routes = [
  { path: '', pathMatch: 'full', loadComponent: () => import('./features/listings/listing-feed/listing-feed').then(m => m.ListingFeed) },
  { path: 'login', loadComponent: () => import('./features/auth/login/login').then(m => m.Login) },
  { path: 'register', loadComponent: () => import('./features/auth/register/register').then(m => m.Register) },
  { path: 'listings/new', canActivate: [authGuard], loadComponent: () => import('./features/listings/listing-editor/listing-editor').then(m => m.ListingEditor) },
  { path: 'listings/drafts/:id', canActivate: [authGuard], loadComponent: () => import('./features/listings/listing-editor/listing-editor').then(m => m.ListingEditor) },
  { path: 'profile', canActivate: [authGuard], loadComponent: () => import('./features/auth/profile/profile').then(m => m.Profile) },
  { path: 'listings/mine', pathMatch: 'full', redirectTo: 'profile' },
  { path: 'listings/:id', loadComponent: () => import('./features/listings/listing-details/listing-details').then(m => m.ListingDetails) },
  { path: '**', redirectTo: '' }
];
