import { PeriodReturn } from '../../core/models';
import {
  RETURN_PERIODS,
  dayChangeHint,
  dayChangeLabel,
  monthYear,
  returnHint,
  returnLabel,
} from './periods';

const ret = (r: Partial<PeriodReturn>): PeriodReturn => ({
  period: 'ALL',
  gain: 100,
  percent: 0.059,
  from: '2024-03-14',
  partial: false,
  timeWeighted: false,
  ...r,
});

describe('return periods', () => {
  it('offers 1M, YTD, 1Y and All, shortest first', () => {
    expect(RETURN_PERIODS).toEqual(['1M', 'YTD', '1Y', 'ALL']);
  });

  it('formats the inception as month and year', () => {
    expect(monthYear('2024-03-14', 'pt-PT')).toBe('03/2024');
    expect(monthYear('2024-03-14', 'en-IE')).toBe('03/2024');
  });

  it('labels the all-time return with the date it starts from', () => {
    expect(returnLabel(ret({}), 'pt-PT')).toEqual({
      key: 'portfolio.returnSince',
      params: { date: '03/2024' },
    });
    // Nothing known yet: the plain total return.
    expect(returnLabel(ret({ from: null }), 'pt-PT')).toEqual({ key: 'portfolio.totalReturn' });
    expect(returnHint(ret({}))).toBe('portfolio.returnAllHint');
  });

  it('labels a period as time-weighted, with its translated name', () => {
    const r = ret({ period: '1Y', from: '2025-10-03', timeWeighted: true });
    expect(returnLabel(r, 'pt-PT', (p) => (p === '1Y' ? '1A' : p))).toEqual({
      key: 'portfolio.returnPeriodTwr',
      params: { period: '1A' },
    });
    expect(returnHint(r)).toBe('portfolio.returnTwrHint');
  });

  it('says since when history starts inside the period', () => {
    const r = ret({ period: '1Y', from: '2026-08-01', partial: true, timeWeighted: true });
    expect(returnLabel(r, 'pt-PT')).toEqual({
      key: 'portfolio.returnSinceTwr',
      params: { date: '01/08/2026' },
    });
  });

  it('calls a coin change 24h and explains mixed totals', () => {
    expect(dayChangeLabel('Rolling24Hours')).toBe('portfolio.change24h');
    expect(dayChangeLabel('PreviousClose')).toBe('portfolio.today');
    expect(dayChangeLabel('Mixed')).toBe('portfolio.today');
    expect(dayChangeLabel(undefined)).toBe('portfolio.today');
    expect(dayChangeHint('Mixed')).toBe('portfolio.todayMixedHint');
    expect(dayChangeHint('Rolling24Hours')).toBe('portfolio.change24hHint');
    expect(dayChangeHint('PreviousClose')).toBeNull();
  });
});
