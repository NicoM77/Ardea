import { Component, computed, input } from '@angular/core';

export type UiAvatarTint = 'a' | 'b' | 'c';
const TINTS: readonly UiAvatarTint[] = ['a', 'b', 'c'];

export function initialsOf(name: string): string {
  const words = name.trim().split(/\s+/).filter(Boolean);
  if (words.length === 0) return '';
  const first = Array.from(words[0])[0] ?? '';
  const last = words.length > 1 ? (Array.from(words[words.length - 1])[0] ?? '') : '';
  return (first + last).toLocaleUpperCase();
}

export function tintFor(id: string): UiAvatarTint {
  let hash = 0;
  for (const char of id) hash = (hash * 31 + (char.codePointAt(0) ?? 0)) | 0;
  return TINTS[Math.abs(hash) % TINTS.length];
}

@Component({
  selector: 'ui-avatar',
  host: { '[class]': "'is-' + tint()", '[attr.data-initials]': 'initials()', 'aria-hidden': 'true' },
  template: '',
  styles: `
    :host::after {
      content: attr(data-initials);
    }
    :host {
      display: inline-grid;
      place-items: center;
      width: 24px;
      height: 24px;
      flex: none;
      border-radius: 50%;
      font-size: var(--fs-small);
      font-weight: 600;
      line-height: 1;
      letter-spacing: -0.02em;
    }
    :host(.is-a) {
      background: var(--who-a-bg);
      color: var(--who-a-ink);
    }
    :host(.is-b) {
      background: var(--who-b-bg);
      color: var(--who-b-ink);
    }
    :host(.is-c) {
      background: var(--who-c-bg);
      color: var(--who-c-ink);
    }
  `,
})
export class UiAvatar {
  readonly name = input.required<string>();
  readonly personId = input.required<string>();
  protected readonly initials = computed(() => initialsOf(this.name()));
  protected readonly tint = computed(() => tintFor(this.personId()));
}
