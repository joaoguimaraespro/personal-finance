import { formatMoney, inDisplay } from '../core/display-currency';
import type { EChartsOption } from 'echarts';

/**
 * One meaning, one colour, on every page: money in and wealth are the brand green, spending is rose, investing is
 * violet, saving is teal. Balances and references are neutral so they never compete with the series they summarise.
 */
export const SERIES_COLORS = {
  income: '#059669',
  expenses: '#e11d48',
  /** Second expense shade (e.g. variable vs fixed). */
  expensesLight: '#fb7185',
  invested: '#7c3aed',
  saved: '#0891b2',
  budget: '#a1a1aa',
  /** Net balance line drawn over income/expense bars: neutral, it summarises them. */
  net: '#71717a',
  /** Portfolio value: the investing colour. */
  value: '#7c3aed',
  /** Net worth: the brand green. */
  wealth: '#059669',
  muted: '#a1a1aa',
};

/** Asset classes, from the same family: shares violet, funds indigo, bonds teal, crypto amber, cash grey. */
export const ASSET_CLASS_COLORS = {
  Stock: '#7c3aed',
  Etf: '#059669',
  Bond: '#0891b2',
  Fund: '#4f46e5',
  Crypto: '#d97706',
  Cash: '#a1a1aa',
  Other: '#db2777',
} as const;

export const CHART_FONT = "'Inter Variable', Inter, ui-sans-serif, system-ui, sans-serif";

const axisLabel = { color: '#a1a1aa', fontSize: 11 };

export function moneyAxis(locale: string): EChartsOption['yAxis'] {
  return {
    type: 'value',
    axisLabel: {
      ...axisLabel,
      formatter: (v: number) =>
        new Intl.NumberFormat(locale, { notation: 'compact', maximumFractionDigits: 1 }).format(
          inDisplay(v).value,
        ),
    },
    splitLine: { lineStyle: { color: 'rgba(161,161,170,0.18)' } },
  };
}

export function percentAxis(): EChartsOption['yAxis'] {
  return {
    type: 'value',
    axisLabel: { ...axisLabel, formatter: (v: number) => `${Math.round(v * 100)}%` },
    splitLine: { lineStyle: { color: 'rgba(161,161,170,0.18)' } },
  };
}

export function categoryAxis(labels: string[]): EChartsOption['xAxis'] {
  return {
    type: 'category',
    data: labels,
    axisLabel,
    axisTick: { show: false },
    axisLine: { show: false },
  };
}

export const baseChart: EChartsOption = {
  grid: { left: 8, right: 8, top: 36, bottom: 8, containLabel: true },
  legend: {
    top: 0,
    left: 0,
    icon: 'roundRect',
    itemWidth: 10,
    itemHeight: 10,
    textStyle: { color: '#a1a1aa', fontSize: 12 },
  },
  tooltip: { trigger: 'axis', valueFormatter: undefined },
};

export function moneyTooltip(locale: string) {
  return {
    trigger: 'axis' as const,
    valueFormatter: (v: unknown) => (typeof v === 'number' ? formatMoney(locale, v) : '—'),
  };
}

export function percentTooltip(locale: string) {
  return {
    trigger: 'axis' as const,
    valueFormatter: (v: unknown) =>
      typeof v === 'number'
        ? new Intl.NumberFormat(locale, { style: 'percent', maximumFractionDigits: 1 }).format(v)
        : '—',
  };
}
