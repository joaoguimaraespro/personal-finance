import { PeriodReturn } from '../../core/models';
import {
  RETURN_PERIODS,
  dayChangeHint,
  dayChangeLabel,
  incomeMonths,
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

  it('labels the gain over cost of a crypto wallet without a start date', () => {
    const r = ret({ from: null, costBased: true });
    expect(returnLabel(r, 'pt-PT')).toEqual({ key: 'portfolio.gainOnCost' });
    expect(returnHint(r)).toBe('portfolio.gainOnCostHint');
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

describe('incomeMonths', () => {
  const today = new Date(2026, 9, 21); // October 2026
  const paid = [
    { period: '2025-03', amount: 4 },
    { period: '2026-03', amount: 10 },
    { period: '2026-09', amount: 13 },
  ];

  it('shows every month of the year so far, the empty ones as zero', () => {
    const months = incomeMonths(paid, 'YTD', today);
    expect(months.map((m) => m.period)).toEqual([
      '2026-01',
      '2026-02',
      '2026-03',
      '2026-04',
      '2026-05',
      '2026-06',
      '2026-07',
      '2026-08',
      '2026-09',
      '2026-10',
    ]);
    expect(months[2].amount).toBe(10);
    expect(months[9].amount).toBe(0);
  });

  it('covers the last 12 months, this month and the last, or everything since the first payment', () => {
    expect(incomeMonths(paid, '1Y', today)[0].period).toBe('2025-11');
    expect(incomeMonths(paid, '1M', today).map((m) => m.period)).toEqual(['2026-09', '2026-10']);
    const all = incomeMonths(paid, 'ALL', today);
    expect(all[0]).toEqual({ period: '2025-03', amount: 4 });
    expect(all).toHaveLength(20);
  });

  it('shows the last 12 months when nothing has paid yet', () => {
    expect(incomeMonths([], 'ALL', today)).toHaveLength(12);
  });
});
