import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { AiCard } from './ai-card';

async function settle(fixture: ComponentFixture<unknown>): Promise<void> {
  await new Promise((resolve) => setTimeout(resolve));
  TestBed.tick();
  fixture.detectChanges();
}

describe('AiCard', () => {
  it('lists the providers, tests one and shows the answer, and shows the correction rate', async () => {
    TestBed.configureTestingModule({
      imports: [AiCard, TranslocoTestingModule.forRoot({ langs: { en: {} } })],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    const fixture = TestBed.createComponent(AiCard);
    const http = TestBed.inject(HttpTestingController);
    await settle(fixture);
    http.expectOne('/api/v1/instance/ai/providers').flush([
      { name: 'local', kind: 'ollama', model: 'qwen3:14b', local: true, host: '10.0.0.5' },
      { name: 'cloud', kind: 'anthropic', model: 'claude', local: false, host: null },
    ]);
    http.expectOne('/api/v1/instance/ai/quality').flush([
      {
        provider: 'local',
        model: 'qwen3:14b',
        promptVersion: '1',
        assessments: 12,
        verdicts: 20,
        confirmed: 3,
        corrected: 1,
        correctionRate: 0.25,
      },
    ]);
    await settle(fixture);
    const element = fixture.nativeElement as HTMLElement;
    expect(element.textContent).toContain('qwen3:14b');
    expect(element.textContent).toContain('10.0.0.5');
    expect(element.textContent).toContain('en.admin.ai.local');
    expect(element.textContent).toContain('en.admin.ai.cloud');
    expect(element.querySelector('tbody')?.textContent).toContain('25%');

    element.querySelector<HTMLButtonElement>('.provider button')!.click();
    await settle(fixture);
    http
      .expectOne((r) => r.method === 'POST' && r.url === '/api/v1/instance/ai/providers/local/test')
      .flush({ ok: false, durationMs: 1200, error: 'The provider could not be reached.' });
    await settle(fixture);
    expect(element.querySelector('.provider [role=status]')?.textContent).toContain(
      'The provider could not be reached.',
    );
  });
});
