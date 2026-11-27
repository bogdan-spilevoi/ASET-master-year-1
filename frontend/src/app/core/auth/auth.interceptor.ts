import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { SessionService } from './session.service';

export const authInterceptor: HttpInterceptorFn = (request, next) => {
  if (!request.url.startsWith('/api/listings') && request.url !== '/api/auth/me') return next(request);
  const token = inject(SessionService).token();
  return next(token ? request.clone({ setHeaders: { Authorization: 'Bearer ' + token } }) : request);
};
