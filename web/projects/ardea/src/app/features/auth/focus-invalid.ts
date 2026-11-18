/**
 * After a submit with errors, move focus to the first field that has one, so keyboard and
 * screen-reader users land on the sentence that explains it. Runs after the error is rendered.
 */
export function focusFirstInvalid(host: HTMLElement): void {
  setTimeout(() => host.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus());
}
