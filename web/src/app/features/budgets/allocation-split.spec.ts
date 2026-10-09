import { allocationSplit, SplitRow } from './allocation-split';

const bucket = (id: string, mode: string, value: string): SplitRow => ({
  target: 'Bucket',
  bucketId: id,
  mode,
  value,
});
const isInvestment = (id: string) => id.startsWith('inv');

describe('allocationSplit', () => {
  it('adds percentages per group and gives expenses the remainder', () => {
    const split = allocationSplit(
      [
        bucket('inv-etf', 'PercentOfIncome', '25'),
        bucket('inv-crypto', 'PercentOfIncome', '0'),
        bucket('sav-trip', 'PercentOfIncome', '5'),
        bucket('sav-other', 'PercentOfIncome', '5,5'),
        bucket('sav-none', 'None', ''),
        { target: 'ExpensePool', bucketId: null, mode: 'Remainder', value: '' },
      ],
      isInvestment,
    );
    expect(split).toEqual({ invest: 25, save: 10.5, expenses: 64.5, fixed: 0, over: false });
  });

  it('keeps fixed amounts apart and flags more than 100%', () => {
    const split = allocationSplit(
      [
        bucket('inv-etf', 'PercentOfIncome', '60'),
        bucket('sav-trip', 'FixedAmount', '150'),
        { target: 'ExpensePool', bucketId: null, mode: 'PercentOfIncome', value: '50' },
      ],
      isInvestment,
    );
    expect(split.fixed).toBe(150);
    expect(split.over).toBe(true);
  });

  it('has no expense share without an expense budget', () => {
    expect(
      allocationSplit([bucket('inv-etf', 'PercentOfIncome', '20')], isInvestment).expenses,
    ).toBeNull();
  });
});
