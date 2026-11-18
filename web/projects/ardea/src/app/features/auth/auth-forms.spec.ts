import { formatSharedKey, normalizeRecoveryCode, normalizeTotp } from './auth-forms';

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
