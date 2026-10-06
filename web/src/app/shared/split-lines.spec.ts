import { describe, expect, it } from 'vitest';
import {
  SplitDraftLine,
  fillRemaining,
  fromSplits,
  splitError,
  splitRemaining,
  splitSum,
  startSplit,
  toSplitRequests,
} from './split-lines';

const line = (categoryId: string, amount: string, note = ''): SplitDraftLine => ({
  categoryId,
  amount,
  note,
});

describe('split lines', () => {
  it('adds amounts exactly, with decimal commas or dots', () => {
    expect(splitSum([line('a', '0,1'), line('b', '0.2')])).toBe(0.3);
    expect(splitSum([line('a', '60,25'), line('b', '')])).toBe(60.25);
  });

  it('shows what is left and what is over', () => {
    expect(splitRemaining(100, [line('a', '60'), line('b', '')])).toBe(40);
    expect(splitRemaining(100, [line('a', '60'), line('b', '40')])).toBe(0);
    expect(splitRemaining(100, [line('a', '60'), line('b', '45,5')])).toBe(-5.5);
    expect(splitRemaining(null, [line('a', '10'), line('b', '')])).toBe(-10);
  });

  it('accepts a balanced split', () => {
    expect(splitError(100, [line('a', '60'), line('b', '40', 'gas')])).toBeNull();
  });

  it.each<[string, number | null, SplitDraftLine[]]>([
    ['count', 100, [line('a', '100')]],
    ['category', 100, [line('a', '60'), line('', '40')]],
    ['duplicate', 100, [line('a', '60'), line('a', '40')]],
    ['amount', 100, [line('a', '100'), line('b', '0')]],
    ['note', 100, [line('a', '60', 'x'.repeat(121)), line('b', '40')]],
    ['sum', 100, [line('a', '60'), line('b', '39,99')]],
  ])('refuses %s problems', (expected, total, lines) => {
    expect(splitError(total, lines)).toBe(expected);
  });

  it('starts from the current category and an empty line', () => {
    expect(startSplit('energy')).toEqual([line('energy', ''), line('', '')]);
    expect(startSplit(null)).toEqual([line('', ''), line('', '')]);
  });

  it('fills the remaining amount into the empty line', () => {
    expect(fillRemaining(100, [line('a', '60,5'), line('b', '')])[1].amount).toBe('39,5');
    // No empty line: the last line takes what is left besides the others.
    expect(fillRemaining(100, [line('a', '60'), line('b', '10')])[1].amount).toBe('40');
    // Nothing left: unchanged.
    const full = [line('a', '100'), line('b', '')];
    expect(fillRemaining(100, full)).toBe(full);
  });

  it('round-trips with the API shape', () => {
    const lines = fromSplits([
      { categoryId: 'a', amount: 60.5, note: 'electricity' },
      { categoryId: 'b', amount: 39.5, note: null },
    ]);
    expect(lines).toEqual([line('a', '60,5', 'electricity'), line('b', '39,5')]);
    expect(toSplitRequests(lines)).toEqual([
      { categoryId: 'a', amount: 60.5, note: 'electricity' },
      { categoryId: 'b', amount: 39.5, note: null },
    ]);
  });
});
