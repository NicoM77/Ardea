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
  async function render(
    isInstanceAdmin: boolean,
    query: { operatorKind?: string } = {},
    sources: SourceStatus[] = [
      source('CVE Program'),
      source('BACS warnings', {
        operator: 'BACS',
        operatorKind: 'authority',
        health: 'failing',
        healthReason: 'HTTP 503',
      }),
    ],
  ) {
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
    http.expectOne('/api/v1/sources').flush(sources);
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

  it('puts Run now and Pause of a running source into its menu, without the disclosure', async () => {
    const { fixture, http, element } = await render(true, {}, [source('CVE Program')]);
    expect(element.querySelector('.paused')).toBeNull();
    element
      .querySelector<HTMLButtonElement>('ardea-source-controls ui-overflow-menu button')!
      .click();
    await settle(fixture);
    const items = [...document.querySelectorAll<HTMLButtonElement>('[role="menuitem"]')];
    expect(items.map((item) => item.textContent?.trim())).toEqual([
      'en.sources.admin.syncNow',
      'en.sources.admin.pause',
    ]);
    items[1].click();
    await settle(fixture);
    const pause = http.expectOne('/api/v1/instance/sources/cve-program');
    expect(pause.request.body).toEqual({ enabled: false, intervalMinutes: null });
  });

  it('shows Resume on a paused source, and one way to resume all when every source is paused', async () => {
    const paused = [source('CVE Program', { enabled: false }), source('OSV', { enabled: false })];
    const { fixture, http, element } = await render(true, {}, paused);
    expect(element.querySelectorAll('ardea-source-controls ui-overflow-menu')).toHaveLength(0);
    expect(
      element.querySelectorAll('button[aria-label="en.sources.admin.resumeNamed"]'),
    ).toHaveLength(2);
    expect(element.querySelector('.paused')?.textContent).toContain('en.sources.admin.allPaused');
    element.querySelector<HTMLButtonElement>('.resume-all')!.click();
    await settle(fixture);
    for (const key of ['cve-program', 'osv'])
      expect(http.expectOne(`/api/v1/instance/sources/${key}`).request.body).toEqual({
        enabled: true,
        intervalMinutes: null,
      });
  });
});
