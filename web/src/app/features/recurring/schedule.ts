import { Frequency } from '../../core/models';

/** Largest "every N" for each frequency (mirrors the API validator). */
export const MAX_DAILY_INTERVAL = 365;
export const MAX_INTERVAL = 24;

export const maxInterval = (frequency: Frequency) =>
  frequency === 'Daily' ? MAX_DAILY_INTERVAL : MAX_INTERVAL;

/** Day of month only anchors monthly and yearly items; weekly and daily ones count days from the start. */
export const usesDayOfMonth = (frequency: Frequency) =>
  frequency === 'Monthly' || frequency === 'Yearly';

/** Translation key and params describing how often an item repeats ("Every 15 days", "Monthly"). */
export function scheduleLabel(r: { frequency: Frequency; interval: number }): {
  key: string;
  params?: Record<string, unknown>;
} {
  if (r.frequency !== 'Daily') return { key: `frequency.${r.frequency}` };
  return r.interval > 1
    ? { key: 'recurring.everyNDays', params: { n: r.interval } }
    : { key: 'recurring.everyDay' };
}

/** The schedule part of a create/update request built from the form's text fields. */
export function scheduleBody(f: { frequency: Frequency; interval: string; dayOfMonth: string }): {
  frequency: Frequency;
  interval: number;
  dayOfMonth: number | null;
} {
  const interval = Math.trunc(Number(f.interval));
  return {
    frequency: f.frequency,
    // Kept for every frequency so editing a "every 2 weeks" item does not reset it.
    interval: Number.isFinite(interval) && interval >= 1 ? interval : 1,
    dayOfMonth: usesDayOfMonth(f.frequency) && f.dayOfMonth ? Number(f.dayOfMonth) : null,
  };
}

/**
 * Form changes when the frequency changes. "Every N" counts days, weeks, months or years depending on the frequency
 * (and only daily items show the field), so a new frequency starts again from 1.
 */
export function withFrequency(
  f: { frequency: Frequency; interval: string },
  frequency: Frequency,
): { frequency: Frequency; interval?: string } {
  return frequency === f.frequency ? { frequency } : { frequency, interval: '1' };
}
