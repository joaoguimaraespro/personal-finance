import { Pipe, PipeTransform, inject } from '@angular/core';
import { Prefs } from './prefs';

/** Formats server-calculated values only; the UI never computes financial figures itself. */
@Pipe({ name: 'money', pure: false })
export class MoneyPipe implements PipeTransform {
  private readonly prefs = inject(Prefs);

  transform(value: number | null | undefined, currency = 'EUR', signed = false): string {
    if (value === null || value === undefined) return '—';
    const text = new Intl.NumberFormat(this.prefs.locale(), {
      style: 'currency',
      currency,
      minimumFractionDigits: 2,
      maximumFractionDigits: 2,
    }).format(value);
    return signed && value > 0 ? `+${text}` : text;
  }
}

@Pipe({ name: 'pct', pure: false })
export class PercentPipe implements PipeTransform {
  private readonly prefs = inject(Prefs);

  transform(value: number | null | undefined, digits = 1): string {
    if (value === null || value === undefined) return '—';
    return new Intl.NumberFormat(this.prefs.locale(), {
      style: 'percent',
      minimumFractionDigits: digits,
      maximumFractionDigits: digits,
    }).format(value);
  }
}

@Pipe({ name: 'monthName', pure: false })
export class MonthNamePipe implements PipeTransform {
  private readonly prefs = inject(Prefs);

  transform(month: number, style: 'long' | 'short' = 'long'): string {
    const name = new Intl.DateTimeFormat(this.prefs.locale(), { month: style }).format(new Date(2000, month - 1, 1));
    return name.charAt(0).toUpperCase() + name.slice(1);
  }
}

@Pipe({ name: 'day', pure: false })
export class DayPipe implements PipeTransform {
  private readonly prefs = inject(Prefs);

  transform(isoDate: string | null | undefined, style: 'short' | 'medium' = 'medium'): string {
    if (!isoDate) return '—';
    const [y, m, d] = isoDate.slice(0, 10).split('-').map(Number);
    return new Intl.DateTimeFormat(this.prefs.locale(), { dateStyle: style }).format(new Date(y, m - 1, d));
  }
}

export const today = (): string => toIsoDate(new Date());

export function toIsoDate(date: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

export const currentPeriod = (): string => today().slice(0, 7);

export function shiftPeriod(period: string, months: number): string {
  const [y, m] = period.split('-').map(Number);
  const d = new Date(y, m - 1 + months, 1);
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`;
}
