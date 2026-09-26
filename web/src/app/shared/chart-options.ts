import type { EChartsOption } from 'echarts';

export const SERIES_COLORS = {
  income: '#10b981',
  expenses: '#e11d48',
  invested: '#7c3aed',
  saved: '#0891b2',
  budget: '#a1a1aa',
  net: '#f59e0b',
};

const axisLabel = { color: '#a1a1aa', fontSize: 11 };

export function moneyAxis(locale: string): EChartsOption['yAxis'] {
  return {
    type: 'value',
    axisLabel: {
      ...axisLabel,
      formatter: (v: number) =>
        new Intl.NumberFormat(locale, { notation: 'compact', maximumFractionDigits: 1 }).format(v),
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
    valueFormatter: (v: unknown) =>
      typeof v === 'number'
        ? new Intl.NumberFormat(locale, { style: 'currency', currency: 'EUR' }).format(v)
        : '—',
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
