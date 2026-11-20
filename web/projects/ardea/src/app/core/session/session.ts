import { computed, inject, Injectable, Injector, Resource } from '@angular/core';
import { toObservable } from '@angular/core/rxjs-interop';
import { filter, firstValueFrom } from 'rxjs';
import type { SessionOrganization, SessionUser } from '../api/client';
import { getSessionResource } from '../api/generated/endpoints/authentication/authentication.service';
import { getSetupStatusResource } from '../api/generated/endpoints/setup/setup.service';
import { signInStateOf, SignInState } from './access';

export type Me = SessionUser & { organizations: SessionOrganization[] };

@Injectable({ providedIn: 'root' })
export class Session {
  private readonly injector = inject(Injector);

  readonly setupStatus = getSetupStatusResource();
  readonly session = getSessionResource();

  readonly user = computed<Me | null>(() => {
    const value = this.session.hasValue() ? this.session.value() : undefined;
    return value ? { ...value.user, organizations: value.organizations } : null;
  });
  readonly state = computed<SignInState>(() => signInStateOf(!!this.user(), this.session.error()));
  readonly setupRequired = computed<boolean | null>(() =>
    this.setupStatus.hasValue() ? (this.setupStatus.value()?.setupRequired ?? null) : null,
  );

  async ready(): Promise<void> {
    await Promise.all([
      settled(this.setupStatus, this.injector),
      settled(this.session, this.injector),
    ]);
  }

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
