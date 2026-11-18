import { email, maxLength, minLength, required, SchemaPath } from '@angular/forms/signals';
import type { FieldTree } from '@angular/forms/signals';

/** Master plan 9: at least 12 characters; length is the rule, not character classes. */
export const PASSWORD_MIN_LENGTH = 12;
/** The longest password the API accepts; long passphrases are welcome. */
export const PASSWORD_MAX_LENGTH = 256;

/** Translation keys for the validation errors a field can show, by error kind. */
export type ErrorKeys = Partial<Record<string, string>>;

export const EMAIL_ERRORS: ErrorKeys = {
  required: 'auth.fields.email.required',
  email: 'auth.fields.email.invalid',
};

export const NAME_ERRORS: ErrorKeys = {
  required: 'auth.fields.displayName.required',
  maxLength: 'auth.fields.displayName.tooLong',
};

export const PASSWORD_ERRORS: ErrorKeys = {
  required: 'auth.fields.password.required',
};

export const NEW_PASSWORD_ERRORS: ErrorKeys = {
  required: 'auth.fields.newPassword.required',
  minLength: 'auth.fields.newPassword.tooShort',
  maxLength: 'auth.fields.newPassword.tooLong',
};

export function emailRules(path: SchemaPath<string>): void {
  required(path);
  email(path);
}

export function newPasswordRules(path: SchemaPath<string>): void {
  required(path);
  minLength(path, PASSWORD_MIN_LENGTH);
  maxLength(path, PASSWORD_MAX_LENGTH);
}

export function displayNameRules(path: SchemaPath<string>): void {
  required(path);
  maxLength(path, 100);
}

/**
 * The error a field shows: only once it was left or the form was submitted, and only the first
 * one, so a person reads one sentence at a time. Returns a translation key or null.
 */
export function fieldErrorKey(field: FieldTree<string>, keys: ErrorKeys): string | null {
  const state = field();
  if (!state.touched()) return null;
  for (const error of state.errors()) {
    const key = keys[error.kind];
    if (key) return key;
  }
  return null;
}

/** Digits only: people paste "123 456" or "123-456" from their app. */
export function normalizeTotp(value: string): string {
  return value.replace(/[^0-9]/g, '');
}

/** Recovery codes are compared without spaces and case. */
export function normalizeRecoveryCode(value: string): string {
  return value.replace(/\s+/g, '').toUpperCase();
}

/** The shared key in groups of four, the way authenticator apps show it and people type it. */
export function formatSharedKey(key: string): string {
  return (
    key
      .replace(/\s+/g, '')
      .toUpperCase()
      .match(/.{1,4}/g)
      ?.join(' ') ?? ''
  );
}
