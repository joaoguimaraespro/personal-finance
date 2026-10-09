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
    const name = new Intl.DateTimeFormat(this.prefs.locale(), { month: style }).format(
      new Date(2000, month - 1, 1),
    );
    return name.charAt(0).toUpperCase() + name.slice(1);
  }
}

@Pipe({ name: 'day', pure: false })
export class DayPipe implements PipeTransform {
  private readonly prefs = inject(Prefs);

  transform(
    isoDate: string | null | undefined,
    style: 'short' | 'medium' | 'full' = 'medium',
  ): string {
    if (!isoDate) return '—';
    const [y, m, d] = isoDate.slice(0, 10).split('-').map(Number);
    return new Intl.DateTimeFormat(this.prefs.locale(), { dateStyle: style }).format(
      new Date(y, m - 1, d),
    );
  }
}

/** Instant in the viewer's time zone, e.g. "30/09/2026, 14:32". For timestamps (UTC ISO strings), not dates. */
@Pipe({ name: 'dateTime', pure: false })
export class DateTimePipe implements PipeTransform {
  private readonly prefs = inject(Prefs);

  transform(isoInstant: string | null | undefined): string {
    if (!isoInstant) return '—';
    const at = new Date(isoInstant);
    if (Number.isNaN(at.getTime())) return '—';
    return new Intl.DateTimeFormat(this.prefs.locale(), {
      dateStyle: 'short',
      timeStyle: 'short',
    }).format(at);
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

/**
 * Unit price: cents for ordinary prices, four significant digits below 1 (coins can be worth fractions of a
 * cent, e.g. 0,00001234 €).
 */
@Pipe({ name: 'price', pure: false })
export class PricePipe implements PipeTransform {
  private readonly prefs = inject(Prefs);

  transform(value: number | null | undefined, currency = 'EUR'): string {
    if (value === null || value === undefined) return '—';
    const small = Math.abs(value) > 0 && Math.abs(value) < 1;
    return new Intl.NumberFormat(this.prefs.locale(), {
      style: 'currency',
      currency,
      ...(small
        ? { minimumSignificantDigits: 2, maximumSignificantDigits: 4 }
        : { minimumFractionDigits: 2, maximumFractionDigits: 2 }),
    }).format(value);
  }
}
