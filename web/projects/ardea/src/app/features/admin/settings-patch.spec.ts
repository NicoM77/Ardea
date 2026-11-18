import type { InstanceSettingsResponse } from '../../core/api/client';
import { count, hasChanges, smtpFormOf, smtpPatch } from './settings-patch';

const view = (value: string | null, locked = false) => ({ value, isSet: value !== null, locked });

const settings: InstanceSettingsResponse = {
  registration: view('invite'),
  smtp: {
    host: view('mail.example.org'),
    port: view('587'),
    user: view('ardea'),
    password: { value: null, isSet: true, locked: false },
    from: view('ardea@example.org', true),
    security: view('starttls'),
  },
};

describe('smtpPatch', () => {
  it('sends nothing when nothing changed, and never the stored password', () => {
    const patch = smtpPatch(settings, smtpFormOf(settings));
    expect(hasChanges(patch)).toBe(false);
    expect(smtpFormOf(settings).newPassword).toBe('');
  });

  it('sends a changed value trimmed, and an empty string for a cleared one', () => {
    const patch = smtpPatch(settings, {
      ...smtpFormOf(settings),
      host: '  smtp.example.org ',
      user: '',
    });
    expect(patch.smtpHost).toBe('smtp.example.org');
    expect(patch.smtpUser).toBe('');
    expect(patch.smtpPort).toBeNull();
  });

  it('leaves a value set by an environment variable alone, whatever the form says', () => {
    const patch = smtpPatch(settings, { ...smtpFormOf(settings), from: 'other@example.org' });
    expect(patch.smtpFrom).toBeNull();
  });

  it('replaces the password only when a new one was typed', () => {
    expect(
      smtpPatch(settings, { ...smtpFormOf(settings), newPassword: 's3cret' }).smtpPassword,
    ).toBe('s3cret');
  });
});

describe('count', () => {
  it('reads int64 counts sent as numbers or strings', () => {
    expect(count(3)).toBe(3);
    expect(count('12')).toBe(12);
    expect(count('not a number')).toBe(0);
  });
});
