import { httpResource } from '@angular/common/http';
import { computed, inject, Injectable, Injector, Resource } from '@angular/core';
import { toObservable } from '@angular/core/rxjs-interop';
import { filter, firstValueFrom } from 'rxjs';
import { Me, SessionResponse, SetupStatus } from '../api/models';
import { signInStateOf, SignInState } from './access';

/**
 * Who is signed in and whether the instance still needs its first user. Both are reads
 * (`httpResource`); sign-in, sign-out and setup call `refresh()` afterwards. No global store.
 *
 * `GET /api/v1/auth/session` also sets the `XSRF-TOKEN` cookie. Its token is bound to the user,
 * which is why the session is read again after every change of identity, before the next write.
 */
@Injectable({ providedIn: 'root' })
export class Session {
  private readonly injector = inject(Injector);

  readonly setupStatus = httpResource<SetupStatus>(() => '/api/v1/setup/status');
  readonly session = httpResource<SessionResponse>(() => '/api/v1/auth/session');

  readonly user = computed<Me | null>(() => {
    if (!this.session.hasValue()) return null;
    const { user, organizations } = this.session.value();
    return { ...user, organizations };
  });
  readonly state = computed<SignInState>(() =>
    signInStateOf(this.session.hasValue(), this.session.error()),
  );
  readonly setupRequired = computed<boolean | null>(() =>
    this.setupStatus.hasValue() ? this.setupStatus.value().setupRequired : null,
  );

  /** Resolves once both reads have an answer, so a guard never decides on a loading state. */
  async ready(): Promise<void> {
    await Promise.all([
      settled(this.setupStatus, this.injector),
      settled(this.session, this.injector),
    ]);
  }

  /** After a change of identity (sign-in, setup, sign-out): both reads again. */
  async refresh(): Promise<void> {
    this.setupStatus.reload();
    this.session.reload();
    await this.ready();
  }
}

function settled(resource: Resource<unknown>, injector: Injector): Promise<unknown> {
  return firstValueFrom(
    toObservable(resource.isLoading, { injector }).pipe(filter((loading) => !loading)),
  );
}
