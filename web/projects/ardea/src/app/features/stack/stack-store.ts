import { computed, DestroyRef, effect, inject, Injectable, signal, untracked } from '@angular/core';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import { UiToaster } from 'ui';
import {
  getStackResource,
  listStackSuggestionsResource,
  ProjectsService,
  type ComponentInput,
  type ComponentView,
  type StackView,
} from '../../core/api/client';
import { problemMessageKey } from '../../core/api/problem';
import { injectOrganization } from '../organizations/current-organization';
import { roleRank } from '../organizations/org-format';
import { entryInput, sortEntries, type Precision, type Threshold } from './stack-format';

const STACK_PROBLEMS = {
  codes: {
    validation_failed: 'stack.errors.invalid',
    component_not_found: 'stack.errors.gone',
    project_not_found: 'stack.errors.gone',
  },
  fallback: 'stack.errors.save',
};

export interface StackProduct {
  key: string;
  name: string;
}

interface Target {
  org: string;
  stackId: string;
}

@Injectable({ providedIn: 'root' })
export class StackRevision {
  readonly count = signal(0);
}

@Injectable()
export class StackStore {
  private readonly projects = inject(ProjectsService);
  private readonly toaster = inject(UiToaster);
  private readonly transloco = inject(TranslocoService);
  private readonly revision = inject(StackRevision);
  private readonly current = injectOrganization();
  private alive = true;

  readonly orgId = computed(() => this.current.organization()?.id);
  readonly canEdit = computed(
    () => roleRank(this.current.organization()?.role) >= roleRank('member'),
  );
  readonly stack = getStackResource(this.orgId);
  readonly suggestions = listStackSuggestionsResource(this.orgId);

  readonly loaded = computed(() => this.stack.hasValue());
  readonly loadProblem = computed(() =>
    this.stack.error()
      ? problemMessageKey(this.stack.error(), { fallback: 'stack.errors.load' })
      : null,
  );
  readonly entries = computed(() =>
    this.stack.hasValue() ? sortEntries(this.stack.value()?.components ?? []) : [],
  );
  readonly keys = computed(
    () => new Set(this.entries().flatMap((e) => (e.productKey ? [e.productKey] : []))),
  );
  readonly suggested = computed(() => {
    const keys = this.keys();
    const list = this.suggestions.hasValue() ? (this.suggestions.value() ?? []) : [];
    return list.filter((s) => !keys.has(s.productKey));
  });

  readonly pending = signal<ReadonlySet<string>>(new Set());
  readonly problem = signal<string | null>(null);

  constructor() {
    inject(DestroyRef).onDestroy(() => (this.alive = false));
    let seen = this.revision.count();
    effect(() => {
      const now = this.revision.count();
      if (now === seen) return;
      seen = now;
      untracked(() => {
        this.stack.reload();
        this.suggestions.reload();
      });
    });
  }

  isPending(id: string): boolean {
    return this.pending().has(id);
  }

  async add(product: StackProduct): Promise<boolean> {
    if (this.keys().has(product.key)) return true;
    const target = this.target();
    if (!target) return false;
    const added = await this.run(product.key, () =>
      firstValueFrom(
        this.projects.addComponent(
          target.org,
          target.stackId,
          entryInput({ productKey: product.key }),
        ),
      ),
    );
    if (!added) return false;
    this.patch((list) => [...list, added]);
    this.confirm('stack.toast.added', { name: added.name }, () =>
      this.undo(
        () => firstValueFrom(this.projects.removeComponent(target.org, target.stackId, added.id)),
        (list) => list.filter((e) => e.id !== added.id),
      ),
    );
    return true;
  }

  async remove(entry: ComponentView): Promise<boolean> {
    const target = this.target();
    if (!target) return false;
    const done = await this.run(entry.id, async () => {
      await firstValueFrom(this.projects.removeComponent(target.org, target.stackId, entry.id));
      return true;
    });
    if (!done) return false;
    this.patch((list) => list.filter((e) => e.id !== entry.id));
    this.confirm('stack.toast.removed', { name: entry.name }, () => this.restore(target, entry));
    return true;
  }

  async setThreshold(entry: ComponentView, threshold: Threshold): Promise<boolean> {
    if (entry.noticeThreshold === threshold) return true;
    const target = this.target();
    const changed = target && (await this.update(target, entry, { noticeThreshold: threshold }));
    if (!target || !changed) return false;
    this.confirm(
      threshold === 'everything' ? 'stack.toast.everything' : 'stack.toast.onlyImportant',
      { name: entry.name },
      () => this.undoUpdate(target, entry, { noticeThreshold: entry.noticeThreshold }),
    );
    return true;
  }

  async setVersion(entry: ComponentView, version: string, precision: Precision): Promise<boolean> {
    const before = entry.version ?? '';
    if (before === version && (version === '' || entry.versionPrecision === precision)) return true;
    const target = this.target();
    const changed =
      target &&
      (await this.update(target, entry, { version, versionPrecision: version ? precision : null }));
    if (!target || !changed) return false;
    const key = !version
      ? 'stack.toast.anyVersion'
      : precision === 'line'
        ? 'stack.toast.line'
        : 'stack.toast.exact';
    this.confirm(key, { name: entry.name, version }, () =>
      this.undoUpdate(target, entry, {
        version: before,
        versionPrecision: before ? entry.versionPrecision : null,
      }),
    );
    return true;
  }

  private target(): Target | null {
    const org = this.orgId();
    const stackId = this.stack.hasValue() ? this.stack.value()?.projectId : undefined;
    return org && stackId ? { org, stackId } : null;
  }

  private async update(
    target: Target,
    entry: ComponentView,
    change: Partial<ComponentInput>,
  ): Promise<ComponentView | null> {
    const changed = await this.run(entry.id, () =>
      firstValueFrom(
        this.projects.updateComponent(target.org, target.stackId, entry.id, entryInput(change)),
      ),
    );
    if (changed) this.patch((list) => list.map((e) => (e.id === changed.id ? changed : e)));
    return changed;
  }

  private undoUpdate(target: Target, entry: ComponentView, change: Partial<ComponentInput>): void {
    this.undo(
      () =>
        firstValueFrom(
          this.projects.updateComponent(target.org, target.stackId, entry.id, entryInput(change)),
        ),
      (list, back) => list.map((e) => (back && e.id === back.id ? back : e)),
    );
  }

  private restore(target: Target, entry: ComponentView): void {
    if (!entry.productKey) {
      this.toaster.show({
        text: this.transloco.translate('stack.errors.undoFree', { name: entry.name }),
      });
      return;
    }
    this.undo(
      () =>
        firstValueFrom(
          this.projects.addComponent(
            target.org,
            target.stackId,
            entryInput({
              productKey: entry.productKey,
              version: entry.version,
              versionPrecision: entry.version ? entry.versionPrecision : null,
              noticeThreshold: entry.noticeThreshold,
            }),
          ),
        ),
      (list, back) => (back ? [...list, back] : list),
    );
  }

  private undo<T>(
    work: () => Promise<T>,
    change: (list: ComponentView[], answer: T | null) => ComponentView[],
  ): void {
    void (async () => {
      try {
        const answer = await work();
        if (this.alive) this.patch((list) => change(list, (answer as T) ?? null));
        else this.revision.count.update((n) => n + 1);
      } catch (error) {
        const key = problemMessageKey(error, { ...STACK_PROBLEMS, fallback: 'stack.errors.undo' });
        if (this.alive) {
          this.problem.set(key);
          this.stack.reload();
        } else {
          this.toaster.show({ text: this.transloco.translate(key) });
          this.revision.count.update((n) => n + 1);
        }
      }
    })();
  }

  private confirm(key: string, params: Record<string, string>, undo: () => void): void {
    this.toaster.show({ text: this.transloco.translate(key, params), undo });
  }

  private patch(change: (list: ComponentView[]) => ComponentView[]): void {
    const stack = this.stack.hasValue() ? this.stack.value() : undefined;
    if (stack) {
      const next: StackView = { ...stack, components: change([...stack.components]) };
      this.stack.value.set(next);
    }
    this.suggestions.reload();
  }

  private async run<T>(id: string, work: () => Promise<T>): Promise<T | null> {
    if (this.pending().has(id)) return null;
    this.pending.update((set) => new Set([...set, id]));
    this.problem.set(null);
    try {
      return await work();
    } catch (error) {
      this.problem.set(problemMessageKey(error, STACK_PROBLEMS));
      this.stack.reload();
      return null;
    } finally {
      this.pending.update((set) => new Set([...set].filter((x) => x !== id)));
    }
  }
}
