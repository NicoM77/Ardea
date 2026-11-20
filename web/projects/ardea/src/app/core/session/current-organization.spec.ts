import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import type { SessionOrganization } from '../api/client';
import { CurrentOrganization, pickOrganization, slugOfUrl } from './current-organization';
import { Session } from './session';

const org = (slug: string): SessionOrganization => ({
  id: slug,
  slug,
  name: slug,
  role: 'member',
  mfaRequired: false,
});
const first = org('first');
const second = org('second');

describe('slugOfUrl', () => {
  it('reads the organization from an organization page only', () => {
    expect(slugOfUrl('/o/second/reminders/4?view=done')).toBe('second');
    expect(slugOfUrl('/entries?q=xz')).toBeNull();
  });
});

describe('pickOrganization', () => {
  it('takes the first slug that names one of the organizations, else the first organization', () => {
    expect(pickOrganization([first, second], 'second', 'first')).toBe(second);
    expect(pickOrganization([first, second], null, 'second')).toBe(second);
    expect(pickOrganization([first, second], 'gone', 'left')).toBe(first);
    expect(pickOrganization([], 'second')).toBeNull();
  });
});

@Component({ template: '' })
class Blank {}

describe('CurrentOrganization', () => {
  beforeEach(() => sessionStorage.clear());
  afterEach(() => sessionStorage.clear());

  function setup(organizations: SessionOrganization[]) {
    const user = signal({ organizations });
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'o/:slug/overview', component: Blank },
          { path: 'entries', component: Blank },
        ]),
        { provide: Session, useValue: { user } },
      ],
    });
    const router = TestBed.inject(Router);
    const go = async (url: string) => {
      await router.navigateByUrl(url);
      TestBed.tick();
    };
    return { current: TestBed.inject(CurrentOrganization), go, user };
  }

  it('stays in the organization last opened on pages that belong to no organization', async () => {
    const { current, go } = setup([first, second]);
    await go('/o/second/overview');
    expect(current.organization()).toBe(second);
    await go('/entries');
    expect(current.organization()).toBe(second);
  });

  it('remembers the organization per tab, across a reload', async () => {
    sessionStorage.setItem('ardea.organization', 'second');
    const { current, go } = setup([first, second]);
    await go('/entries');
    expect(current.organization()).toBe(second);
  });

  it('falls back to the first organization once the last one left the session', async () => {
    sessionStorage.setItem('ardea.organization', 'second');
    const { current, go } = setup([first]);
    await go('/entries');
    expect(current.organization()).toBe(first);
    expect(sessionStorage.getItem('ardea.organization')).toBe('first');
  });
});
