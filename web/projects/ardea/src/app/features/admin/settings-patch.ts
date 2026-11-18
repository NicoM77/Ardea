import type {
  InstanceSettingsResponse,
  SettingView,
  UpdateInstanceSettingsRequest,
} from '../../core/api/client';

/** What the SMTP form edits. The password field is empty unless a new one is typed. */
export interface SmtpForm {
  host: string;
  port: string;
  user: string;
  newPassword: string;
  from: string;
  security: string;
}

export const REGISTRATION_MODES = ['invite', 'open', 'closed'] as const;
export const SMTP_SECURITY = ['starttls', 'tls', 'none'] as const;

/** Nothing changes: every field absent (null) in `PATCH /api/v1/instance/settings`. */
export const NO_CHANGES: UpdateInstanceSettingsRequest = {
  registration: null,
  smtpHost: null,
  smtpPort: null,
  smtpUser: null,
  smtpPassword: null,
  smtpFrom: null,
  smtpSecurity: null,
};

/** The form as loaded: the effective values, and never a password. */
export function smtpFormOf(settings: InstanceSettingsResponse): SmtpForm {
  const smtp = settings.smtp;
  return {
    host: smtp.host.value ?? '',
    port: smtp.port.value ?? '',
    user: smtp.user.value ?? '',
    newPassword: '',
    from: smtp.from.value ?? '',
    security: smtp.security.value ?? '',
  };
}

/**
 * One field of the patch: absent when it is locked by the environment or did not change, the
 * trimmed new value when it changed, an empty string when it was cleared (which removes it).
 */
function field(view: SettingView, edited: string): string | null {
  if (view.locked) return null;
  const value = edited.trim();
  return value === (view.value ?? '') ? null : value;
}

/** The SMTP part of the patch. A new password is sent only when one was typed. */
export function smtpPatch(
  settings: InstanceSettingsResponse,
  form: SmtpForm,
): UpdateInstanceSettingsRequest {
  const smtp = settings.smtp;
  return {
    ...NO_CHANGES,
    smtpHost: field(smtp.host, form.host),
    smtpPort: field(smtp.port, form.port),
    smtpUser: field(smtp.user, form.user),
    smtpPassword: smtp.password.locked || form.newPassword === '' ? null : form.newPassword,
    smtpFrom: field(smtp.from, form.from),
    smtpSecurity: field(smtp.security, form.security),
  };
}

/** Whether a patch would change anything at all. */
export function hasChanges(patch: UpdateInstanceSettingsRequest): boolean {
  return Object.values(patch).some((value) => value !== null);
}

/** Job counts arrive as int64, which the contract allows as number or string. */
export function count(value: number | string): number {
  const number = typeof value === 'number' ? value : Number.parseInt(value, 10);
  return Number.isFinite(number) ? number : 0;
}
