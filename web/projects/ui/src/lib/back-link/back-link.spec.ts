import { Location } from '@angular/common';
import { Component, input } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { UiBackLink } from './back-link';

@Component({ template: '' })
class Blank {}

@Component({
  imports: [UiBackLink],
  template: `<ui-back-link label="Vulnerabilities" link="/entries" [history]="history()" />`,
})
class Host {
  readonly history = input(false);
}

describe('UiBackLink', () => {
  async function render(history: boolean, visited: string[]) {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'entries', component: Blank },
          { path: 'search', component: Blank },
          { path: 'entries/:key', component: Blank },
        ]),
      ],
    });
    const router = TestBed.inject(Router);
    for (const url of visited) await router.navigateByUrl(url);
    const location = TestBed.inject(Location);
    const back = vi.spyOn(location, 'back').mockImplementation(() => undefined);
    const fixture = TestBed.createComponent(Host);
    fixture.componentRef.setInput('history', history);
    await fixture.whenStable();
    const link = (fixture.nativeElement as HTMLElement).querySelector('a')!;
    return { fixture, router, back, link };
  }

  it('is a link with a chevron and the label of the page it leads to', async () => {
    const { link } = await render(false, ['/entries/CVE-2026-0001']);
    expect(link.getAttribute('href')).toBe('/entries');
    expect(link.textContent?.trim()).toBe('Vulnerabilities');
    expect(link.querySelector('svg')?.getAttribute('aria-hidden')).toBe('true');
  });

  it('goes back in the history when the previous page was in the app', async () => {
    const { fixture, router, back, link } = await render(true, [
      '/search',
      '/entries/CVE-2026-0001',
    ]);
    link.click();
    await fixture.whenStable();
    expect(back).toHaveBeenCalledOnce();
    expect(router.url).toBe('/entries/CVE-2026-0001');
  });

  it('follows its link when the page was opened directly', async () => {
    const { fixture, router, back, link } = await render(true, ['/entries/CVE-2026-0001']);
    link.click();
    await fixture.whenStable();
    expect(back).not.toHaveBeenCalled();
    expect(router.url).toBe('/entries');
  });

  it('follows its link without history mode', async () => {
    const { fixture, router, back, link } = await render(false, [
      '/search',
      '/entries/CVE-2026-0001',
    ]);
    link.click();
    await fixture.whenStable();
    expect(back).not.toHaveBeenCalled();
    expect(router.url).toBe('/entries');
  });
});
