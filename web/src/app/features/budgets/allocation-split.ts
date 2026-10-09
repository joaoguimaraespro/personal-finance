import { parseAmount } from '../../shared/parse-amount';

export interface SplitRow {
  target: 'Bucket' | 'ExpensePool' | 'Category';
  bucketId: string | null;
  mode: string;
  value: string;
}

export interface AllocationSplit {
  /** Shares of income in percent. */
  invest: number;
  save: number;
  /** null when the expense budget is not set. */
  expenses: number | null;
  /** Lines set as fixed euro amounts, which no percentage can show. */
  fixed: number;
  /** Percentages add up to more than 100. */
  over: boolean;
}

/** How the edited budget divides income, live: percentages per group, the remainder, fixed amounts apart. */
export function allocationSplit(
  rows: readonly SplitRow[],
  isInvestment: (bucketId: string) => boolean,
): AllocationSplit {
  let invest = 0;
  let save = 0;
  let fixed = 0;
  for (const r of rows.filter((x) => x.target === 'Bucket' && x.bucketId)) {
    const v = parseAmount(r.value) ?? 0;
    if (r.mode === 'PercentOfIncome') {
      if (isInvestment(r.bucketId!)) invest += v;
      else save += v;
    } else if (r.mode === 'FixedAmount') fixed += v;
  }
  const pool = rows.find((r) => r.target === 'ExpensePool');
  let expenses: number | null = null;
  if (pool?.mode === 'Remainder') expenses = Math.max(100 - invest - save, 0);
  else if (pool?.mode === 'PercentOfIncome') expenses = parseAmount(pool.value) ?? 0;
  else if (pool?.mode === 'FixedAmount') fixed += parseAmount(pool.value) ?? 0;
  const round = (n: number) => Math.round(n * 10) / 10;
  return {
    invest: round(invest),
    save: round(save),
    expenses: expenses === null ? null : round(expenses),
    fixed: Math.round(fixed * 100) / 100,
    over: invest + save + (expenses ?? 0) > 100.0001,
  };
}
