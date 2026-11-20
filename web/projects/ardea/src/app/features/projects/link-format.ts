import { DestroyRef, effect, inject, Signal } from '@angular/core';
import type { LinkView } from '../../core/api/client';

export const LINK_STATE_LABELS: Record<string, string> = {
  pending: 'projects.links.states.pending',
  ok: 'projects.links.states.ok',
  partial: 'projects.links.states.partial',
  failing: 'projects.links.states.failing',
};

export function isReading(link: LinkView): boolean {
  return link.state === 'pending';
}

export function repositoryName(link: LinkView): string {
  return `${link.owner}/${link.name}`;
}

export function pollWhile(active: Signal<boolean>, tick: () => void, every = 4000): void {
  let timer: ReturnType<typeof setInterval> | undefined;
  const stop = () => {
    clearInterval(timer);
    timer = undefined;
  };
  effect(() => {
    if (active() && !timer) timer = setInterval(tick, every);
    else if (!active()) stop();
  });
  inject(DestroyRef).onDestroy(stop);
}
