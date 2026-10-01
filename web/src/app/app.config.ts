import { HttpErrorResponse, HttpInterceptorFn, provideHttpClient, withFetch, withInterceptors, withXsrfConfiguration } from '@angular/common/http';
import { ApplicationConfig, inject, provideBrowserGlobalErrorListeners } from '@angular/core';
import { Router, provideRouter, withComponentInputBinding } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { provideTranslateHttpLoader } from '@ngx-translate/http-loader';
import { catchError, throwError } from 'rxjs';
import { trackActivity } from './core/activity';
import { routes } from './app.routes';

/** A 401 anywhere means the session ended: go back to sign-in. */
const sessionExpired: HttpInterceptorFn = (req, next) => {
  const router = inject(Router);
  return next(req).pipe(
    catchError((err: unknown) => {
      if (err instanceof HttpErrorResponse && err.status === 401 && !req.url.startsWith('/api/auth/')) {
        void router.navigateByUrl('/login');
      }
      return throwError(() => err);
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
      withInterceptors([trackActivity, sessionExpired]),
    ),
    provideTranslateService({
      loader: provideTranslateHttpLoader({ prefix: '/i18n/', suffix: '.json' }),
      fallbackLang: 'en',
    }),
  ],
};
