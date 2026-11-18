import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TranslocoTestingModule } from '@jsverse/transloco';
import type { ReviewItemView } from '../../core/api/client';
import { ReviewList } from './review-list';

async function settle(fixture: ComponentFixture<unknown>): Promise<void> {
  await new Promise((resolve) => setTimeout(resolve));
  TestBed.tick();
  fixture.detectChanges();
}

const item: ReviewItemView = {
  id: 'case-1',
  kind: 'cve_conflict',
  state: 'open',
  entries: [
    { primaryKey: 'CVE-2026-1001', title: 'Sample A', state: 'published' },
    { primaryKey: 'CVE-2026-1002', title: 'Sample B', state: 'published' },
  ],
  sharedAliases: ['GHSA-aaaa-bbbb-cccc'],
  firstSeenAt: '2026-11-17T08:00:00Z',
  lastSeenAt: '2026-11-18T08:00:00Z',
  decidedAt: null,
};

describe('ReviewList', () => {
  async function render() {
    TestBed.configureTestingModule({
      imports: [ReviewList, TranslocoTestingModule.forRoot({ langs: { en: {} } })],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    const fixture = TestBed.createComponent(ReviewList);
    await settle(fixture);
    const http = TestBed.inject(HttpTestingController);
    const open = http.expectOne((r) => r.url === '/api/v1/instance/review');
    expect(open.request.params.get('state')).toBe('open');
    open.flush([item]);
    await settle(fixture);
    return { fixture, http, element: fixture.nativeElement as HTMLElement };
  }

  it('shows the entries of a case as links and the identifiers that join them', async () => {
    const { element } = await render();
    const links = [...element.querySelectorAll('a')].map((a) => a.getAttribute('href'));
    expect(links).toEqual(['/entries/CVE-2026-1001', '/entries/CVE-2026-1002']);
    expect(element.textContent).toContain('GHSA-aaaa-bbbb-cccc');
  });

  it('offers only «Keep separate», never a merge, and reloads after deciding', async () => {
    const { fixture, http, element } = await render();
    const buttons = [...element.querySelectorAll('button')].map((b) => b.textContent?.trim());
    expect(buttons).toEqual(['en.review.keepSeparate']);

    element.querySelector('button')!.click();
    await settle(fixture);
    const decide = http.expectOne('/api/v1/instance/review/case-1/keep-separate');
    expect(decide.request.method).toBe('POST');
    decide.flush(null, { status: 204, statusText: 'No Content' });
    await settle(fixture);
    http.expectOne((r) => r.url === '/api/v1/instance/review').flush([]);
    await settle(fixture);
    expect(element.textContent).toContain('review.emptyHeading');
  });

  it('loads decided cases only when they are asked for', async () => {
    const { fixture, http, element } = await render();
    http.expectNone((r) => r.params.get('state') === 'kept_separate');
    element.querySelector<HTMLInputElement>('input[type=checkbox]')!.click();
    await settle(fixture);
    http.expectOne((r) => r.params.get('state') === 'kept_separate').flush([]);
    await settle(fixture);
    expect(element.textContent).toContain('review.noDecided');
  });
});
