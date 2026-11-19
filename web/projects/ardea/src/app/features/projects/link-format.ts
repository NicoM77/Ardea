import { DestroyRef, effect, inject, Signal } from '@angular/core';
import type { LinkView } from '../../core/api/client';

/** A link's state as a word (master plan 16.6): only OK takes a colour, as with the sources. */
export const LINK_STATE_LABELS: Record<string, string> = {
  pending: 'projects.links.states.pending',
  ok: 'projects.links.states.ok',
  partial: 'projects.links.states.partial',
  failing: 'projects.links.states.failing',
};

/** A link that has not been read yet: its reading is queued or running. */
export function isReading(link: LinkView): boolean {
  return link.state === 'pending';
}

export function repositoryName(link: LinkView): string {
  return `${link.owner}/${link.name}`;
}

/**
 * Calls `tick` every few seconds while `active` is true, for readings that run in the background.
 * Stops on its own when nothing is running and when the page goes. Call in an injection context.
 */
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
