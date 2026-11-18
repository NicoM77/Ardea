import { httpResource } from '@angular/common/http';
import { computed, inject, Injectable, Injector, Resource } from '@angular/core';
import { toObservable } from '@angular/core/rxjs-interop';
import { filter, firstValueFrom } from 'rxjs';
import { AuthApi } from '../api/auth-api';
import { Me, SetupStatus } from '../api/models';
import { signInStateOf, SignInState } from './access';

/**
 * Who is signed in and whether the instance still needs its first user. Both are reads
 * (`httpResource`); sign-in, sign-out and setup call `refresh()` afterwards. No global store.
 */
@Injectable({ providedIn: 'root' })
export class Session {
  private readonly injector = inject(Injector);
  private readonly auth = inject(AuthApi);

  readonly setupStatus = httpResource<SetupStatus>(() => '/api/v1/setup/status');
  readonly me = httpResource<Me>(() => '/api/v1/me');

  readonly user = computed<Me | null>(() => (this.me.hasValue() ? this.me.value() : null));
  readonly state = computed<SignInState>(() => signInStateOf(this.me.hasValue(), this.me.error()));
  readonly setupRequired = computed<boolean | null>(() =>
    this.setupStatus.hasValue() ? this.setupStatus.value().setupRequired : null,
  );

  /** Resolves once both reads have an answer, so a guard never decides on a loading state. */
  async ready(): Promise<void> {
    await Promise.all([settled(this.setupStatus, this.injector), settled(this.me, this.injector)]);
  }

  /** After a change of identity: a fresh antiforgery token for the new user, then both reads again. */
  async refresh(): Promise<void> {
    await this.auth.antiforgery().catch(() => undefined);
    this.setupStatus.reload();
    this.me.reload();
    await this.ready();
  }
}

function settled(resource: Resource<unknown>, injector: Injector): Promise<unknown> {
  return firstValueFrom(
    toObservable(resource.isLoading, { injector }).pipe(filter((loading) => !loading)),
  );
}
