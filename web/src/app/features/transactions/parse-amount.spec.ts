import { describe, expect, it } from 'vitest';
import { parseAmount, parseDecimal } from './quick-add';

describe('parseAmount', () => {
  it.each([
    ['45,90', 45.9],
    ['45.90', 45.9],
    ['1.234,56', 1234.56],
    ['1,234.56', 1234.56],
    ['€ 32,50', 32.5],
    ['1000', 1000],
    ['0,1', 0.1],
  ])('parses %s', (input, expected) => {
    expect(parseAmount(input)).toBe(expected);
  });

  it.each(['', 'abc', '   '])('rejects %j', (input) => {
    expect(parseAmount(input)).toBeNull();
  });
});

describe('parseDecimal', () => {
  it.each([
    ['0,00012345', 0.00012345],
    ['0.00012345', 0.00012345],
    ['$ 187.2', 187.2],
    ['1.234,5678', 1234.5678],
  ])('keeps the precision of %s (crypto quantities)', (input, expected) => {
    expect(parseDecimal(input)).toBe(expected);
  });

  it('rounds money to 4 decimals only in parseAmount', () => {
    expect(parseAmount('0,00012345')).toBe(0.0001);
  });
});
