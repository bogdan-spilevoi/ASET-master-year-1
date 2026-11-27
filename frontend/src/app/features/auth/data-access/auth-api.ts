import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Session } from '../../../core/auth/session.service';

@Injectable({ providedIn: 'root' })
export class AuthApi {
  private readonly http = inject(HttpClient);

  profile() {
    return this.http.get<{ userName: string; email: string }>('/api/auth/me');
  }

  logout(refreshToken: string) {
    return this.http.post<void>('/api/auth/logout', { refreshToken });
  }

  login(email: string, password: string) {
    return this.http.post<Session>('/api/auth/login', { email, password });
  }

  register(userName: string, email: string, password: string) {
    return this.http.post<Session>('/api/auth/register', { userName, email, password });
  }
}
