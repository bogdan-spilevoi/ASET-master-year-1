import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { SessionService } from './session.service';

export const authGuard: CanActivateFn = (route, state) => {
  const session = inject(SessionService);
  // The profile also offers server-side logout when the access token has expired.
  if (session.token() || (route.routeConfig?.path === 'profile' && session.user())) return true;
  return inject(Router).createUrlTree(['/login'], { queryParams: { returnTo: state.url.split('?')[0] } });
};

export function signedInDestination(returnTo: string | null): string {
  if (returnTo === '/profile' || returnTo === '/listings/mine') return '/profile';
  if (returnTo && /^\/listings\/drafts\/[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(returnTo)) return returnTo;
  return returnTo === '/listings/new' ? returnTo : '/';
}
