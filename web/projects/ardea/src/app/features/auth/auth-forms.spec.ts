import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { form } from '@angular/forms/signals';
import {
  formatSharedKey,
  normalizeRecoveryCode,
  normalizeTotp,
  weakPasswordError,
} from './auth-forms';

describe('two-factor input', () => {
  it('keeps only the digits of a pasted code', () => {
    expect(normalizeTotp(' 123 456 ')).toBe('123456');
    expect(normalizeTotp('123-456')).toBe('123456');
  });

  it('compares recovery codes without spaces and case', () => {
    expect(normalizeRecoveryCode(' ab12c-d34ef ')).toBe('AB12C-D34EF');
  });

  it('shows the shared key in groups of four, whatever the server sends', () => {
    expect(formatSharedKey('gezdgnbvgy3tqojq')).toBe('GEZD GNBV GY3T QOJQ');
    expect(formatSharedKey('GEZD GNBV GY3T QOJQ AB')).toBe('GEZD GNBV GY3T QOJQ AB');
    expect(formatSharedKey('')).toBe('');
  });
});

describe('weakPasswordError', () => {
  const rejected = (code: string) =>
    new HttpErrorResponse({ status: 400, error: { status: 400, code } });

  it('puts the server’s weak_password answer on the password field', () => {
    TestBed.runInInjectionContext(() => {
      const f = form(signal({ password: 'correct horse' }));
      expect(weakPasswordError(rejected('weak_password'), f.password)).toEqual([
        { kind: 'weakPassword', fieldTree: f.password },
      ]);
    });
  });

  it('leaves every other failure to the form', () => {
    TestBed.runInInjectionContext(() => {
      const f = form(signal({ password: 'correct horse' }));
      expect(weakPasswordError(rejected('invalid_token'), f.password)).toBeNull();
      expect(weakPasswordError(new Error('offline'), f.password)).toBeNull();
    });
  });
});
