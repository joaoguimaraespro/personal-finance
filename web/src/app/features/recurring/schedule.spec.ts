import { describe, expect, it } from 'vitest';
import { Frequency } from '../../core/models';
import {
  maxInterval,
  scheduleBody,
  scheduleLabel,
  usesDayOfMonth,
  withFrequency,
} from './schedule';

describe('scheduleLabel', () => {
  it('reads "every N days" for daily items', () => {
    expect(scheduleLabel({ frequency: 'Daily', interval: 15 })).toEqual({
      key: 'recurring.everyNDays',
      params: { n: 15 },
    });
    expect(scheduleLabel({ frequency: 'Daily', interval: 1 })).toEqual({
      key: 'recurring.everyDay',
    });
  });

  it.each<Frequency>(['Weekly', 'Monthly', 'Yearly'])('uses the frequency name for %s', (f) => {
    expect(scheduleLabel({ frequency: f, interval: 1 })).toEqual({ key: `frequency.${f}` });
  });
});

describe('scheduleBody', () => {
  it('sends the interval and never a day of month for daily items', () => {
    expect(scheduleBody({ frequency: 'Daily', interval: '15', dayOfMonth: '20' })).toEqual({
      frequency: 'Daily',
      interval: 15,
      dayOfMonth: null,
    });
  });

  it('keeps the day of month for monthly and yearly items', () => {
    expect(scheduleBody({ frequency: 'Monthly', interval: '1', dayOfMonth: '25' })).toEqual({
      frequency: 'Monthly',
      interval: 1,
      dayOfMonth: 25,
    });
    expect(
      scheduleBody({ frequency: 'Yearly', interval: '1', dayOfMonth: '' }).dayOfMonth,
    ).toBeNull();
    expect(scheduleBody({ frequency: 'Weekly', interval: '2', dayOfMonth: '3' })).toEqual({
      frequency: 'Weekly',
      interval: 2,
      dayOfMonth: null,
    });
  });

  it.each([
    ['', 1],
    ['abc', 1],
    ['0', 1],
    ['7.9', 7],
  ])('reads interval %j as %d', (text, expected) => {
    expect(scheduleBody({ frequency: 'Daily', interval: text, dayOfMonth: '' }).interval).toBe(
      expected,
    );
  });
});

describe('form rules', () => {
  it('allows up to 365 days and 24 of anything else', () => {
    expect(maxInterval('Daily')).toBe(365);
    expect(maxInterval('Weekly')).toBe(24);
    expect(maxInterval('Monthly')).toBe(24);
  });

  it('shows day of month only for monthly and yearly items', () => {
    expect(usesDayOfMonth('Daily')).toBe(false);
    expect(usesDayOfMonth('Weekly')).toBe(false);
    expect(usesDayOfMonth('Monthly')).toBe(true);
    expect(usesDayOfMonth('Yearly')).toBe(true);
  });

  it('starts the interval again from 1 when the frequency changes', () => {
    expect(withFrequency({ frequency: 'Daily', interval: '15' }, 'Monthly')).toEqual({
      frequency: 'Monthly',
      interval: '1',
    });
    expect(withFrequency({ frequency: 'Daily', interval: '15' }, 'Daily')).toEqual({
      frequency: 'Daily',
    });
  });
});
