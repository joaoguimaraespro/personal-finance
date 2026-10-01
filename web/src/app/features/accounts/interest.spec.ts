import { describe, expect, it } from 'vitest';
import { AccountKind } from '../../core/models';
import { supportsInterest } from './accounts';
import { parseAmount } from '../transactions/quick-add';

describe('supportsInterest', () => {
  it.each<[AccountKind, boolean]>([
    ['Savings', true],
    ['Bank', true],
    ['Cash', false],
    ['CreditCard', false],
    ['Loan', false],
    ['Broker', false],
  ])('%s → %s', (kind, expected) => {
    expect(supportsInterest(kind)).toBe(expected);
  });
});

describe('TANB input', () => {
  it.each([
    ['2,25', 2.25],
    ['2.25', 2.25],
    ['0', 0],
  ])('reads %s as a percentage', (text, expected) => {
    expect(parseAmount(text)).toBe(expected);
  });
});
