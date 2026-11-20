import type {
  InstanceSettingsResponse,
  SettingView,
  UpdateInstanceSettingsRequest,
} from '../../core/api/client';

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

export const NO_CHANGES: UpdateInstanceSettingsRequest = {
  registration: null,
  smtpHost: null,
  smtpPort: null,
  smtpUser: null,
  smtpPassword: null,
  smtpFrom: null,
  smtpSecurity: null,
};

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

function field(view: SettingView, edited: string): string | null {
  if (view.locked) return null;
  const value = edited.trim();
  return value === (view.value ?? '') ? null : value;
}

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

export function hasChanges(patch: UpdateInstanceSettingsRequest): boolean {
  return Object.values(patch).some((value) => value !== null);
}

export function count(value: number | string): number {
  const number = typeof value === 'number' ? value : Number.parseInt(value, 10);
  return Number.isFinite(number) ? number : 0;
}
