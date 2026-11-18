import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject, Injector } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { Session } from '../session/session';

/** Requests whose 401 is an answer the caller handles itself, not an expired session. */
const OWN_401 = ['/api/v1/auth/', '/api/v1/setup'];

/**
 * When a session ends while someone works (expired cookie, revoked elsewhere), any API call
 * answers 401. Forget the user, send them to sign-in and bring them back to where they were.
 */
export const sessionExpiredInterceptor: HttpInterceptorFn = (req, next) => {
  const injector = inject(Injector);
  return next(req).pipe(
    catchError((error: unknown) => {
      const handledByCaller = OWN_401.some((path) => req.url === path || req.url.startsWith(path));
      if (error instanceof HttpErrorResponse && error.status === 401 && !handledByCaller) {
        // Resolved here, not above: Session itself sends requests through this interceptor.
        const session = injector.get(Session);
        const router = injector.get(Router);
        const returnUrl = router.url;
        void session.refresh().then(() =>
          router.navigate(['/sign-in'], {
            queryParams: returnUrl && returnUrl !== '/' ? { returnUrl } : {},
          }),
        );
      }
      return throwError(() => error);
    }),
  );
};
