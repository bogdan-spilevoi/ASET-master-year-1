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
    <section class="form-page"><a routerLink="/">← Item board</a><h1>Create an account</h1>
      <form #form="ngForm" (ngSubmit)="submit()">
        <label>Username<input name="userName" autocomplete="username" [(ngModel)]="userName" minlength="3" maxlength="100" required></label>
        <label>Email<input type="email" name="email" autocomplete="email" [(ngModel)]="email" required email></label>
        <label>Password<input type="password" name="password" autocomplete="new-password" [(ngModel)]="password" minlength="8" required></label>
        <small>Use at least 8 characters for your password.</small>
        @if (error()) { <p role="alert">{{ error() }}</p> }
        <button class="primary" [disabled]="form.invalid || busy()">Create an account</button>
      </form>
      <p>Already registered? <a routerLink="/login" queryParamsHandling="preserve">Sign in</a></p>
    </section>
  `
})
export class Register {
  private readonly api = inject(AuthApi);
  private readonly session = inject(SessionService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  readonly error = signal('');
  readonly busy = signal(false);
  userName = '';
  email = '';
  password = '';

  submit() {
    this.busy.set(true);
    this.error.set('');
    this.api.register(this.userName, this.email, this.password).subscribe({
      next: user => {
        this.session.signIn(user);
        void this.router.navigateByUrl(signedInDestination(this.route.snapshot.queryParamMap.get('returnTo')));
      },
      error: error => { this.error.set(apiError(error)); this.busy.set(false); }
    });
  }
}
