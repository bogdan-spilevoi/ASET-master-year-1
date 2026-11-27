import { HttpErrorResponse } from '@angular/common/http';

export function apiError(error: unknown): string {
  if (!(error instanceof HttpErrorResponse)) return 'The request could not be completed.';
  if (error.status === 0) return 'Could not connect. Check your connection and try again.';
  if (typeof error.error === 'string' && error.error.length < 250) return error.error;
  return error.error?.detail || error.error?.title || 'The request failed (HTTP ' + error.status + ').';
}
