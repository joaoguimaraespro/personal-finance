import { HttpContextToken, HttpInterceptorFn } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { finalize } from 'rxjs';

/** Set on background polling requests so they don't flash the global loading bar. */
export const BACKGROUND = new HttpContextToken<boolean>(() => false);

/** Counts in-flight API requests for the global loading bar. */
@Injectable({ providedIn: 'root' })
export class Activity {
  private readonly inFlight = signal(0);
  readonly busy = computed(() => this.inFlight() > 0);

  start() {
    this.inFlight.update((n) => n + 1);
  }

  end() {
    this.inFlight.update((n) => Math.max(0, n - 1));
  }
}

export const trackActivity: HttpInterceptorFn = (req, next) => {
  if (req.context.get(BACKGROUND) || !req.url.startsWith('/api/')) return next(req);
  const activity = inject(Activity);
  activity.start();
  return next(req).pipe(finalize(() => activity.end()));
};
