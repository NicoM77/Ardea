import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { computed, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TranslocoTestingModule } from '@jsverse/transloco';
import type { SourceStatus } from '../../core/api/client';
import { Session, type Me } from '../../core/session/session';
import { SourcesPage } from './sources-page';

const source = (name: string, extra: Partial<SourceStatus> = {}): SourceStatus => ({
  key: name.toLowerCase().replace(/\s+/g, '-'),
  name,
  kind: 'feed',
  operator: 'CVE Program',
  operatorKind: 'mandated',
  enabled: true,
  health: 'healthy',
  healthReason: null,
  intervalMinutes: 60,
  lastSuccessAt: '2026-11-18T08:00:00Z',
  lastAttemptAt: '2026-11-18T08:00:00Z',
  nextSyncAt: '2026-11-18T09:00:00Z',
  consecutiveFailures: 0,
  records: 6432,
  quarantined: 0,
  attribution: 'Sample attribution',
  termsMode: 'full',
  ...extra,
});

async function settle(fixture: ComponentFixture<unknown>): Promise<void> {
  await new Promise((resolve) => setTimeout(resolve));
  TestBed.tick();
  fixture.detectChanges();
}

describe('SourcesPage', () => {
  async function render(isInstanceAdmin: boolean, query: { operatorKind?: string } = {}) {
    const user = signal<Me>({
      id: 'u1',
      email: 'sample@example.org',
      displayName: 'Sample Person',
      isInstanceAdmin,
      twoFactorEnabled: true,
      organizations: [],
    });
    TestBed.configureTestingModule({
      imports: [SourcesPage, TranslocoTestingModule.forRoot({ langs: { en: {} } })],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: Session, useValue: { user: computed(() => user()) } },
      ],
    });
    const fixture = TestBed.createComponent(SourcesPage);
    if (query.operatorKind) fixture.componentRef.setInput('operatorKind', query.operatorKind);
    await settle(fixture);
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/v1/sources').flush([
      source('CVE Program'),
      source('BACS warnings', {
        operator: 'BACS',
        operatorKind: 'authority',
        health: 'failing',
        healthReason: 'HTTP 503',
      }),
    ]);
    await settle(fixture);
    return { fixture, http, element: fixture.nativeElement as HTMLElement };
  }
  const names = (element: HTMLElement) =>
    [...element.querySelectorAll('.source h2')].map((node) => node.textContent?.trim());

  it('shows the failing source first, with its reason', async () => {
    const { element } = await render(false);
    expect(names(element)).toEqual(['BACS warnings', 'CVE Program']);
    expect(element.textContent).toContain('HTTP 503');
  });

  it('filters by the kind of operator in the address', async () => {
    const { element } = await render(false, { operatorKind: 'authority' });
    expect(names(element)).toEqual(['BACS warnings']);
  });

  it('offers runs and quarantine to the instance administrator only', async () => {
    expect((await render(false)).element.querySelector('.toggle')).toBeNull();
    TestBed.resetTestingModule();

    const { fixture, http, element } = await render(true);
    element.querySelector<HTMLButtonElement>('.toggle')!.click();
    await settle(fixture);
    http.expectOne((r) => r.url === '/api/v1/instance/sources/bacs-warnings/runs').flush([]);
    http
      .expectOne((r) => r.url === '/api/v1/instance/sources/bacs-warnings/quarantine')
      .flush([
        {
          externalId: 'bacs-42',
          url: null,
          reason: 'Missing date',
          lastSeenAt: '2026-11-18T08:00:00Z',
          revision: 2,
        },
      ]);
    await settle(fixture);
    expect(element.textContent).toContain('Missing date');
  });
});
