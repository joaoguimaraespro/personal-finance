import { HttpClient, HttpContext, HttpErrorResponse } from '@angular/common/http';
import { Injectable, NgZone, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { BACKGROUND } from './activity';
import { AuthService } from './auth';
import { DataEvents } from './data-events';

interface SessionInfo {
  expiresAtUtc: string;
  idleTimeoutMinutes: number;
}

/** Warn this long before an idle session ends. */
export const WARN_BEFORE_MS = 5 * 60_000;
/** Someone who clicked or typed this recently is using the app; keep their session alive. */
export const ACTIVE_WITHIN_MS = 5 * 60_000;
const CHECK_EVERY_MS = 5 * 60_000;

export type SessionAction = 'none' | 'extend' | 'recheck' | 'warn' | 'expired';

/**
 * What to do for a session with `leftMs` remaining. Pure, so the policy is testable:
 * active users are renewed silently once half the idle timeout has passed; idle users are warned
 * shortly before expiry, after re-checking the server (other requests may have slid the session).
 */
export function sessionAction(
  leftMs: number,
  idleMs: number,
  sinceActivityMs: number,
  sinceCheckMs: number,
): SessionAction {
  if (leftMs <= 0) return 'expired';
  if (sinceActivityMs < ACTIVE_WITHIN_MS && leftMs < idleMs / 2) return 'extend';
  if (leftMs <= WARN_BEFORE_MS) return sinceCheckMs > 30_000 ? 'recheck' : 'warn';
  return sinceCheckMs > CHECK_EVERY_MS ? 'recheck' : 'none';
}

/**
 * Keeps the signed-in session honest: renews it while the owner is using the app, warns before an idle
 * session ends, re-validates when the tab comes back or the network returns, and sends an expired session
 * to sign-in with a clear message instead of leaving a page that silently stopped working.
 */
@Injectable({ providedIn: 'root' })
export class SessionMonitor {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly auth = inject(AuthService);
  private readonly events = inject(DataEvents);
  private readonly zone = inject(NgZone);

  /** Seconds until an idle session ends, while the warning is showing. */
  readonly secondsLeft = signal<number | null>(null);

  private expiresAt = 0;
  private idleMs = 0;
  private lastActivity = Date.now();
  private lastCheck = 0;
  private lastExtend = 0;
  private busy = false;
  private timer: ReturnType<typeof setInterval> | undefined;
  private readonly background = { context: new HttpContext().set(BACKGROUND, true) };

  private readonly onActivity = () => (this.lastActivity = Date.now());
  private readonly onReturn = () => {
    if (document.visibilityState === 'visible') void this.check(true);
  };

  start() {
    if (this.timer) return;
    this.lastActivity = Date.now();
    this.zone.runOutsideAngular(() => {
      window.addEventListener('pointerdown', this.onActivity, { passive: true });
      window.addEventListener('keydown', this.onActivity, { passive: true });
      document.addEventListener('visibilitychange', this.onReturn);
      window.addEventListener('online', this.onReturn);
      this.timer = setInterval(() => this.zone.run(() => this.tick()), 1000);
    });
    void this.check();
  }

  stop() {
    clearInterval(this.timer);
    this.timer = undefined;
    window.removeEventListener('pointerdown', this.onActivity);
    window.removeEventListener('keydown', this.onActivity);
    document.removeEventListener('visibilitychange', this.onReturn);
    window.removeEventListener('online', this.onReturn);
    this.expiresAt = 0;
    this.secondsLeft.set(null);
  }

  /** Renew the idle timeout (automatically while active, or "Stay signed in" from the warning). */
  async extend() {
    this.lastExtend = Date.now();
    await this.run(async () => {
      this.apply(
        await firstValueFrom(
          this.http.post<SessionInfo>('/api/auth/session/extend', {}, this.background),
        ),
      );
    });
  }

  /** The server said 401: leave cleanly, remembering where the owner was. */
  expired() {
    if (!this.timer && !this.auth.me()) return;
    const returnUrl = this.router.url.startsWith('/login') ? undefined : this.router.url;
    this.stop();
    this.auth.me.set(null);
    void this.router.navigate(['/login'], { queryParams: { expired: 1, returnUrl } });
  }

  private tick() {
    if (!this.expiresAt || this.busy) return;
    const now = Date.now();
    const left = this.expiresAt - now;
    const action = sessionAction(left, this.idleMs, now - this.lastActivity, now - this.lastCheck);
    this.secondsLeft.set(action === 'warn' ? Math.ceil(left / 1000) : null);
    // At most one automatic renewal a minute, even if the server didn't move the expiry.
    if (action === 'extend' && now - this.lastExtend > 60_000) void this.extend();
    else if (action === 'recheck' || action === 'expired') void this.check();
  }

  private async check(refreshData = false) {
    await this.run(async () => {
      this.apply(
        await firstValueFrom(this.http.get<SessionInfo>('/api/auth/session', this.background)),
      );
      if (refreshData) this.events.bump();
    });
  }

  private apply(info: SessionInfo) {
    this.expiresAt = Date.parse(info.expiresAtUtc);
    this.idleMs = info.idleTimeoutMinutes * 60_000;
    this.lastCheck = Date.now();
    this.secondsLeft.set(null);
  }

  private async run(action: () => Promise<void>) {
    if (this.busy) return;
    this.busy = true;
    try {
      await action();
    } catch (err) {
      // Offline or the server is unreachable: keep the current state and try again on the next check.
      if (err instanceof HttpErrorResponse && err.status === 401) this.expired();
      else this.lastCheck = Date.now();
    } finally {
      this.busy = false;
    }
  }
}
