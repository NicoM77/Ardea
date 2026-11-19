import { computed, inject, Injectable, signal } from '@angular/core';
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

/** A product to add: its catalogue key and its name, for the confirmation. */
export interface StackProduct {
  key: string;
  name: string;
}

/**
 * The organization's stack (master plan 17.2, ADR 046) for one page: its entries and what to add,
 * read from `GET stack` and `GET stack/suggestions`. Every change is saved at once through the
 * stack project's component endpoints and confirmed with Undo (DESIGN.md, Feedback). Provided by
 * the page that shows the stack, so the wizard and the stack page each have their own.
 */
@Injectable()
export class StackStore {
  private readonly projects = inject(ProjectsService);
  private readonly toaster = inject(UiToaster);
  private readonly transloco = inject(TranslocoService);
  private readonly current = injectOrganization();

  readonly orgId = computed(() => this.current.organization()?.id);
  /** Viewers read the stack; members and above change it, as any project. */
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
  /** Suggestions without what the stack names meanwhile, so nothing is offered twice while they reload. */
  readonly suggested = computed(() => {
    const keys = this.keys();
    const list = this.suggestions.hasValue() ? (this.suggestions.value() ?? []) : [];
    return list.filter((s) => !keys.has(s.productKey));
  });

  /** Products and entries being saved; their buttons wait. */
  readonly pending = signal<ReadonlySet<string>>(new Set());
  readonly problem = signal<string | null>(null);

  isPending(id: string): boolean {
    return this.pending().has(id);
  }

  async add(product: StackProduct): Promise<boolean> {
    if (this.keys().has(product.key)) return true;
    const added = await this.run(product.key, (org, stackId) =>
      firstValueFrom(
        this.projects.addComponent(org, stackId, entryInput({ productKey: product.key })),
      ),
    );
    if (!added) return false;
    this.patch((list) => [...list, added]);
    this.confirm('stack.toast.added', { name: added.name }, () => void this.removeQuietly(added));
    return true;
  }

  async remove(entry: ComponentView): Promise<boolean> {
    const done = await this.run(entry.id, async (org, stackId) => {
      await firstValueFrom(this.projects.removeComponent(org, stackId, entry.id));
      return true;
    });
    if (!done) return false;
    this.patch((list) => list.filter((e) => e.id !== entry.id));
    this.confirm('stack.toast.removed', { name: entry.name }, () => void this.restore(entry));
    return true;
  }

  async setThreshold(entry: ComponentView, threshold: Threshold): Promise<void> {
    if (entry.noticeThreshold === threshold) return;
    const changed = await this.update(entry, { noticeThreshold: threshold });
    if (!changed) return;
    this.confirm(
      threshold === 'everything' ? 'stack.toast.everything' : 'stack.toast.onlyImportant',
      { name: entry.name },
      () => void this.update(entry, { noticeThreshold: entry.noticeThreshold }),
    );
  }

  /** A new version, or none («""»), with the precision it is meant with (ADR 046). */
  async setVersion(entry: ComponentView, version: string, precision: Precision): Promise<void> {
    const before = entry.version ?? '';
    if (before === version && (version === '' || entry.versionPrecision === precision)) return;
    const changed = await this.update(entry, {
      version,
      versionPrecision: version ? precision : null,
    });
    if (!changed) return;
    const key = !version
      ? 'stack.toast.anyVersion'
      : precision === 'line'
        ? 'stack.toast.line'
        : 'stack.toast.exact';
    this.confirm(
      key,
      { name: entry.name, version },
      () =>
        void this.update(changed, {
          version: before,
          versionPrecision: before ? entry.versionPrecision : null,
        }),
    );
  }

  private async update(
    entry: ComponentView,
    change: Partial<ComponentInput>,
  ): Promise<ComponentView | null> {
    const changed = await this.run(entry.id, (org, stackId) =>
      firstValueFrom(this.projects.updateComponent(org, stackId, entry.id, entryInput(change))),
    );
    if (changed) this.patch((list) => list.map((e) => (e.id === changed.id ? changed : e)));
    return changed;
  }

  private async removeQuietly(entry: ComponentView): Promise<void> {
    const done = await this.run(entry.id, async (org, stackId) => {
      await firstValueFrom(this.projects.removeComponent(org, stackId, entry.id));
      return true;
    });
    if (done) this.patch((list) => list.filter((e) => e.id !== entry.id));
  }

  /** Undo of a removal: the entry comes back with its version, precision and threshold. */
  private async restore(entry: ComponentView): Promise<void> {
    if (!entry.productKey) return;
    const back = await this.run(entry.productKey, (org, stackId) =>
      firstValueFrom(
        this.projects.addComponent(
          org,
          stackId,
          entryInput({
            productKey: entry.productKey,
            version: entry.version,
            versionPrecision: entry.version ? entry.versionPrecision : null,
            noticeThreshold: entry.noticeThreshold,
          }),
        ),
      ),
    );
    if (back) this.patch((list) => [...list, back]);
  }

  private confirm(key: string, params: Record<string, string>, undo: () => void): void {
    this.toaster.show({ text: this.transloco.translate(key, params), undo });
  }

  /** The stack as the last answer left it; suggestions follow in the background. */
  private patch(change: (list: ComponentView[]) => ComponentView[]): void {
    const stack = this.stack.hasValue() ? this.stack.value() : undefined;
    if (stack) {
      const next: StackView = { ...stack, components: change([...stack.components]) };
      this.stack.value.set(next);
    }
    this.suggestions.reload();
  }

  private async run<T>(
    id: string,
    work: (org: string, stackId: string) => Promise<T>,
  ): Promise<T | null> {
    const org = this.orgId();
    const stackId = this.stack.hasValue() ? this.stack.value()?.projectId : undefined;
    if (!org || !stackId || this.pending().has(id)) return null;
    this.pending.update((set) => new Set([...set, id]));
    this.problem.set(null);
    try {
      return await work(org, stackId);
    } catch (error) {
      this.problem.set(problemMessageKey(error, STACK_PROBLEMS));
      this.stack.reload();
      return null;
    } finally {
      this.pending.update((set) => new Set([...set].filter((x) => x !== id)));
    }
  }
}
