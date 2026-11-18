import { HttpInterceptorFn } from '@angular/common/http';
import { inject, InjectionToken } from '@angular/core';

/**
 * Where the API lives. Empty in the web app: the API serves it from the same origin. The later
 * mobile app connects to an address the person enters (master plan 11.9) and provides it here.
 */
export const API_BASE_URL = new InjectionToken<string>('API_BASE_URL', {
  providedIn: 'root',
  factory: () => '',
});

/** Puts the base URL in front of the generated client's `/api/...` paths (master plan 2.4). */
export const apiBaseUrlInterceptor: HttpInterceptorFn = (req, next) => {
  const base = inject(API_BASE_URL).replace(/\/+$/, '');
  return base && req.url.startsWith('/api/') ? next(req.clone({ url: base + req.url })) : next(req);
};
