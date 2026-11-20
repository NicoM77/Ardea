import { inject, Injectable } from '@angular/core';
import { TranslocoService } from '@jsverse/transloco';

export type DateInput = string | number | Date;
export type DateStyle = 'medium' | 'long' | 'full';

const CALENDAR_DAY = /^\d{4}-\d{2}-\d{2}$/;
const SECOND = 1000;
const MINUTE = 60 * SECOND;
const HOUR = 60 * MINUTE;
const DAY = 24 * HOUR;
const WEEK = 7 * DAY;
const MONTH = 30 * DAY;
const YEAR = 365 * DAY;

const formats = new Map<string, Intl.DateTimeFormat>();
const relatives = new Map<string, Intl.RelativeTimeFormat>();

function dateFormat(locale: string, options: Intl.DateTimeFormatOptions): Intl.DateTimeFormat {
  const key = `${locale}|${JSON.stringify(options)}`;
  let format = formats.get(key);
  if (!format) {
    format = new Intl.DateTimeFormat(locale, options);
    formats.set(key, format);
  }
  return format;
}

function relativeFormat(locale: string): Intl.RelativeTimeFormat {
  let format = relatives.get(locale);
  if (!format) {
    format = new Intl.RelativeTimeFormat(locale, { numeric: 'auto' });
    relatives.set(locale, format);
  }
  return format;
}

function parse(value: DateInput): { at: Date; day: boolean } {
  if (typeof value === 'string' && CALENDAR_DAY.test(value))
    return { at: new Date(`${value}T00:00:00Z`), day: true };
  return { at: value instanceof Date ? value : new Date(value), day: false };
}

function valid(at: Date): boolean {
  return !Number.isNaN(at.getTime());
}

export function formatDate(locale: string, value: DateInput, style: DateStyle = 'medium'): string {
  const { at, day } = parse(value);
  if (!valid(at)) return String(value);
  return dateFormat(locale, { dateStyle: style, ...(day ? { timeZone: 'UTC' } : {}) }).format(at);
}

export function formatDateTime(
  locale: string,
  value: DateInput,
  style: DateStyle = 'medium',
): string {
  const { at } = parse(value);
  if (!valid(at)) return String(value);
  return dateFormat(locale, { dateStyle: style, timeStyle: 'short' }).format(at);
}

export function formatTime(locale: string, value: DateInput): string {
  const { at } = parse(value);
  if (!valid(at)) return String(value);
  return dateFormat(locale, { timeStyle: 'short' }).format(at);
}

export function formatRelative(
  locale: string,
  value: DateInput,
  now: DateInput = Date.now(),
): string {
  const { at } = parse(value);
  const from = parse(now).at;
  if (!valid(at) || !valid(from)) return String(value);
  const diff = at.getTime() - from.getTime();
  const size = Math.abs(diff);
  const format = relativeFormat(locale);
  if (size < MINUTE) return format.format(0, 'second');
  if (size < HOUR) return format.format(Math.round(diff / MINUTE), 'minute');
  if (size < DAY) return format.format(Math.round(diff / HOUR), 'hour');
  if (size < WEEK) return format.format(Math.round(diff / DAY), 'day');
  if (size < MONTH) return format.format(Math.round(diff / WEEK), 'week');
  if (size < YEAR) return format.format(Math.round(diff / MONTH), 'month');
  return format.format(Math.round(diff / YEAR), 'year');
}

@Injectable({ providedIn: 'root' })
export class Dates {
  private readonly transloco = inject(TranslocoService);

  private get locale(): string {
    return this.transloco.getActiveLang();
  }

  date(value: DateInput, style: DateStyle = 'medium'): string {
    return formatDate(this.locale, value, style);
  }

  dateTime(value: DateInput, style: DateStyle = 'medium'): string {
    return formatDateTime(this.locale, value, style);
  }

  time(value: DateInput): string {
    return formatTime(this.locale, value);
  }

  relative(value: DateInput, now: DateInput = Date.now()): string {
    return formatRelative(this.locale, value, now);
  }
}
