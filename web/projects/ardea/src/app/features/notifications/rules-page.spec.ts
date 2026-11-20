import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { computed } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { Session } from '../../core/session/session';
import { RulesPage } from './rules-page';

const ORG_ID = '0190a000-0000-7000-8000-0000000000aa';
const URL = '/o/heron/notification-rules';

const rule = (id: string, name: string) => ({
  id,
  name,
  enabled: true,
  minLevel: 4,
  events: ['notice.created'],
  projectIds: [],
  targets: [],
  createdAt: '2026-11-19T08:00:00Z',
});

async function settle(): Promise<void> {
  await new Promise((resolve) => setTimeout(resolve));
  TestBed.tick();
}

describe('RulesPage', () => {
  it('shows a rule again that was restored by Undo while the list is open', async () => {
    const user = {
      id: 'me',
      email: 'me@example.org',
      displayName: 'Me',
      isInstanceAdmin: false,
      twoFactorEnabled: true,
      organizations: [
        { id: ORG_ID, slug: 'heron', name: 'Heron', role: 'admin', mfaRequired: false },
      ],
    };
    TestBed.configureTestingModule({
      imports: [TranslocoTestingModule.forRoot({ langs: { en: {} } })],
      providers: [
        provideRouter([{ path: 'o/:slug/notification-rules', component: RulesPage }]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: Session, useValue: { user: computed(() => user), refresh: vi.fn() } },
      ],
    });
    const harness = await RouterTestingHarness.create(URL);
    const http = TestBed.inject(HttpTestingController);
    const answer = (items: unknown[]) => {
      for (const req of http.match(() => true))
        req.flush(req.request.url === `/api/v1/orgs/${ORG_ID}/rules` ? items : []);
    };

    await settle();
    answer([rule('r1', 'First rule')]);
    await settle();
    harness.detectChanges();
    expect(harness.routeNativeElement?.textContent).toContain('First rule');
    expect(harness.routeNativeElement?.textContent).not.toContain('Restored rule');

    await TestBed.inject(Router).navigateByUrl(URL, { onSameUrlNavigation: 'reload' });
    await settle();
    answer([rule('r1', 'First rule'), rule('r2', 'Restored rule')]);
    await settle();
    harness.detectChanges();
    expect(harness.routeNativeElement?.textContent).toContain('Restored rule');
  });
});
