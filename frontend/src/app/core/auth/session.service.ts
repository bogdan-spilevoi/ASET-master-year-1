import { Injectable, signal } from '@angular/core';

export interface Session {
  userName: string;
  accessToken: string;
  refreshToken: string;
  expiresAtUtc: string;
}

@Injectable({ providedIn: 'root' })
export class SessionService {
  private readonly current = signal<Session | null>(null);
  readonly user = this.current.asReadonly();

  signIn({ userName, accessToken, expiresAtUtc, refreshToken }: Session) {
    this.current.set({ userName, accessToken, expiresAtUtc, refreshToken });
  }

  token(): string | null {
    const session = this.current();
    if (session && Date.parse(session.expiresAtUtc) > Date.now()) return session.accessToken;
    return null;
  }

  signOut() {
    this.current.set(null);
  }
}
