export function focusFirstInvalid(host: HTMLElement): void {
  setTimeout(() => host.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus());
}
