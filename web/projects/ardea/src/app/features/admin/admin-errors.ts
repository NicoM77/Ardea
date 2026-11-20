import type { FieldTree, TreeValidationResult } from '@angular/forms/signals';
import { problemOf } from '../../core/api/problem';

export function fieldProblems(
  error: unknown,
  fields: Record<string, FieldTree<string>>,
): TreeValidationResult | null {
  const errors = problemOf(error)?.errors ?? {};
  const result = Object.keys(errors)
    .filter((name) => name in fields)
    .map((name) => ({ kind: 'server', fieldTree: fields[name] }));
  return result.length ? result : null;
}
