import { AsyncPipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, map, of } from 'rxjs';
import { SessionService } from '../../../core/auth/session.service';
import { apiError } from '../../../shared/api-error';
import { MyListings } from '../../listings/my-listings/my-listings';
import { AuthApi } from '../data-access/auth-api';

@Component({
  imports: [AsyncPipe, MyListings],
  templateUrl: './profile.html',
  styleUrl: './profile.scss'
})
export class Profile {
  private readonly api = inject(AuthApi);
  private readonly session = inject(SessionService);
  private readonly router = inject(Router);
  readonly pending = signal(false);
  readonly logoutError = signal('');
  readonly profile = this.api.profile().pipe(
    map(user => ({ user, error: '' })),
    catchError(error => of({ user: null, error: apiError(error) }))
  );

  logout() {
    const active = this.session.user();
    if (!active || this.pending()) return;
    this.logoutError.set('');
    this.pending.set(true);
    this.api.logout(active.refreshToken).subscribe({
      next: () => {
        if (this.session.user() === active) {
          this.session.signOut();
          void this.router.navigateByUrl('/', { replaceUrl: true });
        }
        this.pending.set(false);
      },
      error: error => {
        this.logoutError.set(apiError(error));
        this.pending.set(false);
      }
    });
  }
}
