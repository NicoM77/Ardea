import { inject } from '@angular/core';
import {
  ActivatedRouteSnapshot,
  CanActivateFn,
  Router,
  RouterStateSnapshot,
  UrlTree,
} from '@angular/router';
import {
  AccessDecision,
  decideGuestAccess,
  decideSetupAccess,
  decideSignedInAccess,
} from './access';
import { Session } from './session';

function redirect(router: Router, decision: AccessDecision, returnUrl?: string): true | UrlTree {
  switch (decision) {
    case 'allow':
      return true;
    case 'setup':
      return router.createUrlTree(['/setup']);
    case 'signIn':
      return router.createUrlTree(['/sign-in'], {
        queryParams: returnUrl && returnUrl !== '/' ? { returnUrl } : {},
      });
    case 'home':
      return router.createUrlTree(['/']);
    case 'unavailable':
      return router.createUrlTree(['/unavailable'], {
        queryParams: returnUrl && returnUrl !== '/' ? { returnUrl } : {},
      });
  }
}

export const signedInGuard: CanActivateFn = async (_route, state: RouterStateSnapshot) => {
  const session = inject(Session);
  const router = inject(Router);
  await session.ready();
  return redirect(
    router,
    decideSignedInAccess(session.setupRequired(), session.state()),
    state.url,
  );
};

export const guestGuard: CanActivateFn = async () => {
  const session = inject(Session);
  const router = inject(Router);
  await session.ready();
  return redirect(router, decideGuestAccess(session.setupRequired(), session.state()));
};

export const setupGuard: CanActivateFn = async () => {
  const session = inject(Session);
  const router = inject(Router);
  await session.ready();
  return redirect(router, decideSetupAccess(session.setupRequired()));
};

export const organizationGuard: CanActivateFn = (route: ActivatedRouteSnapshot) => {
  const slug = route.paramMap.get('slug');
  const user = inject(Session).user();
  const member = user?.organizations.some((org) => org.slug === slug) ?? false;
  return member || inject(Router).createUrlTree(['/']);
};

export const instanceAdminGuard: CanActivateFn = () =>
  inject(Session).user()?.isInstanceAdmin === true || inject(Router).createUrlTree(['/']);

export const homeRedirectGuard: CanActivateFn = () => {
  const first = inject(Session).user()?.organizations[0];
  const router = inject(Router);
  return first
    ? router.createUrlTree(['/o', first.slug, 'overview'])
    : router.createUrlTree(['/no-organization']);
};
