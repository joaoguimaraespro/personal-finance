import { DayChangeBasis, PeriodReturn, ReturnPeriod } from '../../core/models';

/** The periods offered at the top of the portfolio page, shortest first. */
export const RETURN_PERIODS: readonly ReturnPeriod[] = ['1M', 'YTD', '1Y', 'ALL'];

/** "03/2024": month and year of an ISO date (yyyy-mm-dd), in the locale's numeric order. */
export function monthYear(isoDate: string, locale: string): string {
  const [y, m] = isoDate.slice(0, 10).split('-').map(Number);
  return new Intl.DateTimeFormat(locale, { month: '2-digit', year: 'numeric' }).format(
    new Date(y, m - 1, 1),
  );
}

/** "01/08/2026": a full numeric date, for history that starts inside the chosen period. */
export function numericDate(isoDate: string, locale: string): string {
  const [y, m, d] = isoDate.slice(0, 10).split('-').map(Number);
  return new Intl.DateTimeFormat(locale, {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
  }).format(new Date(y, m - 1, d));
}

/** A translation key with its parameters. */
export interface Label {
  key: string;
  params?: Record<string, string>;
}

/**
 * The label of a return, so the period it covers is always stated:
 * - all time: "Return since 03/2024" (total return since the first deposit or trade);
 * - a period: "Return 1Y (TWR)";
 * - a period that history doesn't fully cover: "Return since 01/08/2026 (TWR)".
 */
export function returnLabel(
  r: PeriodReturn | null | undefined,
  locale: string,
  periodName: (period: ReturnPeriod) => string = (p) => p,
): Label {
  if (r?.costBased && r.period === 'ALL') {
    return { key: 'portfolio.gainOnCost' };
  }
  if (!r || r.period === 'ALL') {
    return r?.from
      ? { key: 'portfolio.returnSince', params: { date: monthYear(r.from, locale) } }
      : { key: 'portfolio.totalReturn' };
  }
  if (r.partial && r.from) {
    return { key: 'portfolio.returnSinceTwr', params: { date: numericDate(r.from, locale) } };
  }
  return { key: 'portfolio.returnPeriodTwr', params: { period: periodName(r.period) } };
}

/** Explains how the figure is measured (shown as a tooltip). */
export function returnHint(r: PeriodReturn | null | undefined): string {
  if (r?.costBased && r.period === 'ALL') return 'portfolio.gainOnCostHint';
  return !r || !r.timeWeighted ? 'portfolio.returnAllHint' : 'portfolio.returnTwrHint';
}

/** Coins move around the clock: their change is over 24 hours; shares and ETFs since the previous close. */
export function dayChangeLabel(basis: DayChangeBasis | null | undefined): string {
  return basis === 'Rolling24Hours' ? 'portfolio.change24h' : 'portfolio.today';
}

/** A tooltip when the figure is not (only) "since the previous close". */
export function dayChangeHint(basis: DayChangeBasis | null | undefined): string | null {
  return basis === 'Mixed'
    ? 'portfolio.todayMixedHint'
    : basis === 'Rolling24Hours'
      ? 'portfolio.change24hHint'
      : null;
}

/**
 * Income by month over the period chosen at the top, every month included (those that paid nothing as zero), up to
 * the current month: 1M covers this month and the last, YTD since January, 1Y the last 12 months and ALL since the
 * first month that paid (the last 12 months when none has).
 */
export function incomeMonths(
  byMonth: readonly { period: string; amount: number }[],
  period: ReturnPeriod,
  today = new Date(),
): { period: string; amount: number }[] {
  const key = (y: number, m: number) => `${y}-${String(m).padStart(2, '0')}`;
  const index = (p: string) => {
    const [y, m] = p.split('-').map(Number);
    return y * 12 + (m - 1);
  };
  const end = today.getFullYear() * 12 + today.getMonth();
  const paid = byMonth.filter((m) => m.amount).map((m) => index(m.period));
  const start =
    period === '1M'
      ? end - 1
      : period === 'YTD'
        ? today.getFullYear() * 12
        : period === '1Y' || !paid.length
          ? end - 11
          : Math.min(...paid);
  const amounts = new Map(byMonth.map((m) => [index(m.period), m.amount]));
  const months: { period: string; amount: number }[] = [];
  for (let i = start; i <= end; i++) {
    months.push({ period: key(Math.floor(i / 12), (i % 12) + 1), amount: amounts.get(i) ?? 0 });
  }
  return months;
}
