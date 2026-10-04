import { parseAmount } from './crypto-dialog';

describe('parseAmount', () => {
  it('reads Portuguese and English decimals and thousands', () => {
    expect(parseAmount('0,25')).toBe(0.25);
    expect(parseAmount('0.25')).toBe(0.25);
    expect(parseAmount('40 000')).toBe(40000);
    expect(parseAmount('1.234,5')).toBe(1234.5);
    expect(parseAmount('1,234.5')).toBe(1234.5);
    expect(parseAmount('42 000 €')).toBe(42000);
  });

  it('rejects text that is not a number', () => {
    expect(parseAmount('')).toBeNull();
    expect(parseAmount('abc')).toBeNull();
    expect(parseAmount('1,2,3')).toBeNull();
  });
});
