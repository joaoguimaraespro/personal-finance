import { HttpErrorResponse, HttpInterceptorFn, provideHttpClient, withFetch, withInterceptors, withXsrfConfiguration } from '@angular/common/http';
import { ApplicationConfig, inject, provideBrowserGlobalErrorListeners } from '@angular/core';
import { Router, provideRouter, withComponentInputBinding } from '@angular/router';
import { TranslateService, provideTranslateService } from '@ngx-translate/core';
import { provideTranslateHttpLoader } from '@ngx-translate/http-loader';
import { TimeoutError, catchError, throwError, timeout } from 'rxjs';
import { BACKGROUND, trackActivity } from './core/activity';
import { SessionMonitor } from './core/session';
import { Toasts } from './core/toast';
import { routes } from './app.routes';

/** A 401 anywhere means the session ended: sign in again, then come back to the same page. */
const sessionExpired: HttpInterceptorFn = (req, next) => {
  const session = inject(SessionMonitor);
  return next(req).pipe(
    catchError((err: unknown) => {
      if (err instanceof HttpErrorResponse && err.status === 401 && !req.url.startsWith('/api/auth/')) {
        session.expired();
      }
      return throwError(() => err);
    }),
  );
};

/**
 * Requests that hang (server down, VPN reconnecting on a phone) fail after a while with a clear message,
 * instead of leaving the page waiting until a manual reload. Uploads get longer.
 */
const requestTimeout: HttpInterceptorFn = (req, next) => {
  if (!req.url.startsWith('/api/')) return next(req);
  const toasts = inject(Toasts);
  const i18n = inject(TranslateService);
  const ms = req.body instanceof FormData ? 180_000 : 30_000;
  return next(req).pipe(
    timeout({ first: ms }),
    catchError((err: unknown) => {
      if (!(err instanceof TimeoutError)) return throwError(() => err);
      if (!req.context.get(BACKGROUND)) toasts.show(i18n.instant('session.timeout'), 'error', undefined, 8000);
      return throwError(
        () => new HttpErrorResponse({ status: 0, statusText: 'Timeout', url: req.url, error: err }),
      );
    }),
  );
};

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes, withComponentInputBinding()),
    provideHttpClient(
      withFetch(),
      // The API issues XSRF-TOKEN; Angular echoes it on every mutating same-origin request.
      withXsrfConfiguration({ cookieName: 'XSRF-TOKEN', headerName: 'X-XSRF-TOKEN' }),
      withInterceptors([trackActivity, requestTimeout, sessionExpired]),
    ),
    provideTranslateService({
      loader: provideTranslateHttpLoader({ prefix: '/i18n/', suffix: '.json' }),
      fallbackLang: 'en',
    }),
  ],
};
