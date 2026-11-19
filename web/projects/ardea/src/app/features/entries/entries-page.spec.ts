import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TranslocoTestingModule } from '@jsverse/transloco';
import type { EntrySummary } from '../../core/api/client';
import { EntriesPage } from './entries-page';

const summary = (key: string): EntrySummary => ({
  key,
  kind: 'vulnerability',
  title: `Title of ${key}`,
  state: 'published',
  cvssScore: 7.5,
  severity: 'high',
  exploitation: 'none',
  epssClass: null,
  fixAvailable: false,
  authorityWarning: false,
  publishedAt: null,
  changedAt: '2026-11-18T08:00:00Z',
  aliases: [],
});

/** Lets answered requests settle, runs effects (where httpResource sends) and renders; never waits for an open request. */
async function settle(fixture: ComponentFixture<unknown>): Promise<void> {
  await new Promise((resolve) => setTimeout(resolve));
  TestBed.tick();
  fixture.detectChanges();
}

describe('EntriesPage', () => {
  async function render(query: { q?: string; kind?: string } = {}) {
    TestBed.configureTestingModule({
      imports: [EntriesPage, TranslocoTestingModule.forRoot({ langs: { en: {} } })],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    const fixture = TestBed.createComponent(EntriesPage);
    if (query.q) fixture.componentRef.setInput('q', query.q);
    if (query.kind) fixture.componentRef.setInput('kind', query.kind);
    await settle(fixture);
    return { fixture, http: TestBed.inject(HttpTestingController) };
  }
  const keys = (element: HTMLElement) =>
    [...element.querySelectorAll('.row .id')].map((node) => node.textContent?.trim());

  it('asks the API with the filters from the address', async () => {
    const { http } = await render({ q: 'CVE-2026-1', kind: 'warning' });
    const request = http.expectOne((r) => r.url === '/api/v1/entries');
    expect(request.request.params.get('q')).toBe('CVE-2026-1');
    expect(request.request.params.get('kind')).toBe('warning');
    expect(request.request.params.has('cursor')).toBe(false);
  });

  it('appends the next page when more is asked for, with the cursor of the last page', async () => {
    const { fixture, http } = await render();
    http
      .expectOne((r) => r.url === '/api/v1/entries')
      .flush({
        items: [summary('CVE-2026-1'), summary('CVE-2026-2')],
        nextCursor: 'page-2',
      });
    await settle(fixture);
    const element: HTMLElement = fixture.nativeElement;
    expect(keys(element)).toEqual(['CVE-2026-1', 'CVE-2026-2']);

    element.querySelector<HTMLButtonElement>('.more button')!.click();
    await settle(fixture);
    const next = http.expectOne((r) => r.url === '/api/v1/entries');
    expect(next.request.params.get('cursor')).toBe('page-2');
    next.flush({ items: [summary('CVE-2026-3')], nextCursor: null });
    await settle(fixture);

    expect(keys(element)).toEqual(['CVE-2026-1', 'CVE-2026-2', 'CVE-2026-3']);
    expect(element.querySelector('.more')).toBeNull();
  });

  it('says so when a search finds nothing, and offers to clear it', async () => {
    const { fixture, http } = await render({ q: 'nothing like this' });
    http.expectOne((r) => r.url === '/api/v1/entries').flush({ items: [], nextCursor: null });
    await settle(fixture);
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('entries.empty.filteredHeading');
    expect(text).toContain('entries.empty.clear');
  });
});
