import { email, maxLength, minLength, required, SchemaPath } from '@angular/forms/signals';
import type { FieldTree, TreeValidationResult } from '@angular/forms/signals';
import { problemOf } from '../../core/api/problem';

const PASSWORD_MIN_LENGTH = 12;
const PASSWORD_MAX_LENGTH = 256;

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
  weakPassword: 'auth.fields.newPassword.weak',
};

export function weakPasswordError(
  error: unknown,
  password: FieldTree<string>,
): TreeValidationResult | null {
  return problemOf(error)?.code === 'weak_password'
    ? [{ kind: 'weakPassword', fieldTree: password }]
    : null;
}

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

export function fieldErrorKey(field: FieldTree<string>, keys: ErrorKeys): string | null {
  const state = field();
  if (!state.touched()) return null;
  for (const error of state.errors()) {
    const key = keys[error.kind];
    if (key) return key;
  }
  return null;
}

export function normalizeTotp(value: string): string {
  return value.replace(/[^0-9]/g, '');
}

export function normalizeRecoveryCode(value: string): string {
  return value.replace(/\s+/g, '').toUpperCase();
}

export function formatSharedKey(key: string): string {
  return (
    key
      .replace(/\s+/g, '')
      .toUpperCase()
      .match(/.{1,4}/g)
      ?.join(' ') ?? ''
  );
}
