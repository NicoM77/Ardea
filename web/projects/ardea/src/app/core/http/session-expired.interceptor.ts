import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject, Injector } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { Session } from '../session/session';

const OWN_401 = ['/api/v1/auth/', '/api/v1/setup'];

export const sessionExpiredInterceptor: HttpInterceptorFn = (req, next) => {
  const injector = inject(Injector);
  return next(req).pipe(
    catchError((error: unknown) => {
      const handledByCaller = OWN_401.some((path) => req.url === path || req.url.startsWith(path));
      if (error instanceof HttpErrorResponse && error.status === 401 && !handledByCaller) {
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
