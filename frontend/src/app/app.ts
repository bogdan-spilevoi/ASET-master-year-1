import { Component, inject } from '@angular/core';
import { RouterLink, RouterOutlet } from '@angular/router';
import { SessionService } from './core/auth/session.service';

@Component({
  selector: 'sl-app', imports: [RouterLink, RouterOutlet],
  template: `
    <header><nav aria-label="Main navigation"><a class="brand" routerLink="/"><img src="mark.svg" width="34" height="34" alt="">SmartLost</a>
      <div class="nav-actions">@if (session.user()) {
        <a class="text-link" routerLink="/profile">Profile</a>
      } @else { <a class="text-link" routerLink="/login">Sign in</a> }
      <a class="button primary" routerLink="/listings/new">Report an item</a></div>
    </nav></header>
    <main><router-outlet /></main>
  `
})
export class App {
  readonly session = inject(SessionService);
}
