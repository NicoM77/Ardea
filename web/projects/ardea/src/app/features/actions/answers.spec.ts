import { TestBed } from '@angular/core/testing';
import { TRANSLOCO_TRANSPILER, TranslocoTestingModule } from '@jsverse/transloco';
import en from '../../../i18n/en.json';
import type { AnswersView } from '../../core/api/client';
import { IcuTranspiler } from '../../core/i18n/icu-transpiler';
import { Answers } from './answers';
import { quoteLang, rangePhrase } from './answers-format';

const ANSWERS: AnswersView = {
  origin: 'template',
  whatHappened: {
    entryKey: 'CVE-2026-116325',
    title: 'Function owner’s rights through a crafted search path',
    excerpt:
      'A database user who may create objects can run SQL with the rights of a function’s owner.',
    source: 'CVE Program',
    moreEntries: 3,
  },
  affected: {
    verdict: 'affected',
    items: [
      {
        project: { id: 'p1', name: 'Platform Ops' },
        component: { id: 'c1', name: 'PostgreSQL', version: '17.9', removed: false },
        level: 4,
        versionVerdict: 'in_range',
        versionReason: 'in_affected_range',
        ranges: [{ introduced: '17', fixed: '17.12', lastAffected: null, exact: null }],
      },
    ],
    moreItems: 0,
  },
  toDo: {
    kind: 'update',
    targetVersion: '17.12',
    installedVersion: '17.9',
    steps: [],
    advisoryUrl: 'https://www.postgresql.org/support/security/',
  },
};

describe('the three answers', () => {
  function render(answers: AnswersView) {
    TestBed.configureTestingModule({
      imports: [
        Answers,
        TranslocoTestingModule.forRoot({
          langs: { en },
          translocoConfig: { availableLangs: ['en'], defaultLang: 'en' },
          preloadLangs: true,
        }),
      ],
      providers: [{ provide: TRANSLOCO_TRANSPILER, useClass: IcuTranspiler }],
    });
    const fixture = TestBed.createComponent(Answers);
    fixture.componentRef.setInput('answers', answers);
    fixture.componentRef.setInput('component', 'PostgreSQL');
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }
  const text = (el: Element | null) => el?.textContent?.replace(/\s+/g, ' ').trim();

  it('words the parts in the reader’s language: verdict, version, range and target', () => {
    const element = render(ANSWERS);
    const affected = element.querySelector('[data-answer="affected"]');
    expect(text(affected?.querySelector('.answer') ?? null)).toBe('Yes');
    expect(text(affected)).toContain('PostgreSQL 17.9 · In range: 17 to before 17.12');
    const todo = element.querySelector('[data-answer="todo"]');
    expect(text(todo?.querySelector('.answer') ?? null)).toBe('Update to 17.12');
    expect(text(todo?.querySelector('code') ?? null)).toBe('PostgreSQL 17.12');
    expect(text(todo)).toContain('From 17.9, the version installed now');
  });

  it('quotes the source’s own words as a quotation and names the source', () => {
    const element = render(ANSWERS);
    const happened = element.querySelector('[data-answer="happened"]');
    expect(text(happened?.querySelector('blockquote') ?? null)).toBe(ANSWERS.whatHappened.excerpt);
    expect(text(happened?.querySelector('figcaption') ?? null)).toBe(
      'From CVE Program, shortened at a sentence end',
    );
    expect(text(happened)).toContain('And 3 more entries');
  });

  it('says when no fix is named, and quotes the source’s advice as steps', () => {
    const noFix = render({
      ...ANSWERS,
      toDo: { ...ANSWERS.toDo, kind: 'no_fix', targetVersion: null },
    });
    expect(text(noFix.querySelector('[data-answer="todo"] .answer'))).toBe('No fix named yet');
    TestBed.resetTestingModule();
    const steps = render({
      ...ANSWERS,
      toDo: {
        ...ANSWERS.toDo,
        kind: 'source_guidance',
        targetVersion: null,
        steps: ['Revoke CREATE.'],
      },
    });
    expect(text(steps.querySelector('[data-answer="todo"] ol q'))).toBe('Revoke CREATE.');
  });

  it('writes every shape of range', () => {
    const r = (
      introduced: string | null,
      fixed: string | null,
      lastAffected: string | null = null,
    ) => rangePhrase({ introduced, fixed, lastAffected, exact: null });
    expect(r('17', '17.12')).toEqual({
      key: 'answers.range.fromBefore',
      params: { from: '17', to: '17.12' },
    });
    expect(r('0', '2.4.2').key).toBe('answers.range.before');
    expect(r('1.0', null, '1.9').key).toBe('answers.range.fromTo');
    expect(r(null, null, '1.9').key).toBe('answers.range.upTo');
    expect(r('3.1', null).key).toBe('answers.range.from');
    expect(r(null, null).key).toBe('answers.range.any');
    expect(
      rangePhrase({ introduced: null, fixed: null, lastAffected: null, exact: '1.0.3' }).key,
    ).toBe('answers.range.exact');
  });

  it('marks the English source text with its language when the interface speaks another', () => {
    expect(quoteLang('en')).toBeNull();
    expect(quoteLang('de-CH')).toBe('en');
  });
});
