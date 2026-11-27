import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { signedInDestination } from '../../../core/auth/auth.guard';
import { AuthApi } from '../data-access/auth-api';
import { SessionService } from '../../../core/auth/session.service';
import { apiError } from '../../../shared/api-error';

@Component({
  imports: [FormsModule, RouterLink],
  template: `
    <section class="form-page"><a routerLink="/">← Item board</a><h1>Sign in</h1>
      <form #form="ngForm" (ngSubmit)="submit()">
        
        <label>Email<input type="email" name="email" autocomplete="email" [(ngModel)]="email" required email></label>
        <label>Password<input type="password" name="password" autocomplete="current-password" [(ngModel)]="password" required></label>
        @if (error()) { <p role="alert">{{ error() }}</p> }
        <button class="primary" [disabled]="form.invalid || busy()">Sign in</button>
      </form>
      <p>New to SmartLost? <a routerLink="/register" queryParamsHandling="preserve">Create an account</a></p>
    </section>
  `
})
export class Login {
  private readonly api = inject(AuthApi);
  private readonly session = inject(SessionService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  readonly error = signal('');
  readonly busy = signal(false);
  email = '';
  password = '';

  submit() {
    this.busy.set(true);
    this.error.set('');
    this.api.login(this.email, this.password).subscribe({
      next: user => {
        this.session.signIn(user);
        void this.router.navigateByUrl(signedInDestination(this.route.snapshot.queryParamMap.get('returnTo')));
      },
      error: error => { this.error.set(apiError(error)); this.busy.set(false); }
    });
  }
}
