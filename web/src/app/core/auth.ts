import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { Me } from './models';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  readonly me = signal<Me | null>(null);
  readonly ready = computed(() => this.me()?.mfaSatisfied === true);

  async refresh(): Promise<Me> {
    // Every auth state change rotates the anti-forgery token (it is bound to the signed-in identity).
    await firstValueFrom(this.http.get('/api/auth/antiforgery'));
    const me = await firstValueFrom(this.http.get<Me>('/api/auth/me'));
    this.me.set(me);
    return me;
  }

  async setup(email: string, password: string, setupToken: string) {
    await firstValueFrom(this.http.post('/api/auth/setup', { email, password, setupToken }));
  }

  async login(email: string, password: string, rememberMe: boolean): Promise<string> {
    const res = await firstValueFrom(
      this.http.post<{ status: string }>('/api/auth/login', { email, password, rememberMe }),
    );
    await this.refresh();
    return res.status;
  }

  async verifyMfa(code: string | null, recoveryCode: string | null, rememberMe: boolean) {
    await firstValueFrom(this.http.post('/api/auth/login/mfa', { code, recoveryCode, rememberMe }));
    await this.refresh();
  }

  mfaSetup = () => firstValueFrom(this.http.get<{ sharedKey: string; otpAuthUri: string }>('/api/auth/mfa/setup'));

  async enableMfa(code: string): Promise<string[]> {
    const res = await firstValueFrom(this.http.post<{ recoveryCodes: string[] }>('/api/auth/mfa/enable', { code }));
    await this.refresh();
    return res.recoveryCodes;
  }

  changePassword = (currentPassword: string, newPassword: string) =>
    firstValueFrom(this.http.post('/api/auth/password', { currentPassword, newPassword }));

  async logout() {
    await firstValueFrom(this.http.post('/api/auth/logout', {}));
    await this.refresh();
  }
}

/** Only a session that completed MFA may see financial pages. */
export const requireMfa: CanActivateFn = async () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  const me = auth.me() ?? (await auth.refresh());
  if (me.mfaSatisfied) return true;
  if (me.setupRequired) return router.parseUrl('/setup');
  if (me.authenticated && !me.mfaEnabled) return router.parseUrl('/mfa-setup');
  return router.parseUrl('/login');
};
