import { equalShares, ownerPart, shareError, shareablePeople } from './share-lines';

describe('share lines', () => {
  it('splits equally between the owner and the people, the owner keeping the rounding', () => {
    const shares = equalShares(17.99, ['ana', 'rui']);
    expect(shares).toEqual([
      { personId: 'ana', amount: 5.99 },
      { personId: 'rui', amount: 5.99 },
    ]);
    expect(ownerPart(17.99, shares)).toBe(6.01);
  });

  it('has nothing to split without people or an amount', () => {
    expect(equalShares(10, [])).toEqual([]);
    expect(equalShares(0, ['ana'])).toEqual([]);
  });

  it('rejects no shares, empty parts and parts above the amount', () => {
    expect(shareError(10, [])).toBe('none');
    expect(shareError(10, [{ personId: 'ana', amount: 0 }])).toBe('amount');
    expect(shareError(10, [{ personId: 'ana', amount: 10.01 }])).toBe('amount');
    expect(shareError(10, [{ personId: 'ana', amount: 10 }])).toBeNull();
  });

  it('keeps the people already sharing an edited expense, even if archived or not loaded yet', () => {
    const active = [{ id: 'ana', name: 'Ana' }];
    const onExpense = [
      { personId: 'ana', personName: 'Ana' },
      { personId: 'rui', personName: 'Rui' },
    ];
    expect(shareablePeople(active, onExpense)).toEqual([
      { id: 'ana', name: 'Ana' },
      { id: 'rui', name: 'Rui' },
    ]);
    expect(shareablePeople([], onExpense)).toEqual([
      { id: 'ana', name: 'Ana' },
      { id: 'rui', name: 'Rui' },
    ]);
    expect(shareablePeople(active, [])).toEqual(active);
  });
});
