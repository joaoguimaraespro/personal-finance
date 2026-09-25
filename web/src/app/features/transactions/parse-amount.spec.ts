import { describe, expect, it } from 'vitest';
import { parseAmount } from './quick-add';

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
