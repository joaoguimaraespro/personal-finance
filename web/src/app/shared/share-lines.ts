import { ShareRequest } from '../core/models';

/**
 * Splits `amount` equally between the owner and `people` (in cents): each person gets the same part, rounded down,
 * and the owner keeps whatever the rounding leaves, so the parts always add up to the amount.
 */
export function equalShares(amount: number, people: string[]): ShareRequest[] {
  if (!(amount > 0) || people.length === 0) return [];
  const cents = Math.round(amount * 100);
  const each = Math.floor(cents / (people.length + 1));
  return people.map((personId) => ({ personId, amount: each / 100 }));
}

/** The owner's own part once the shares are taken out (never below zero). */
export function ownerPart(amount: number, shares: ShareRequest[]): number {
  const cents =
    Math.round(amount * 100) - shares.reduce((s, x) => s + Math.round(x.amount * 100), 0);
  return cents / 100;
}

/** Why a set of shares cannot be saved, or null. */
export function shareError(amount: number, shares: ShareRequest[]): 'none' | 'amount' | null {
  if (shares.length === 0) return 'none';
  if (shares.some((s) => !(s.amount > 0)) || ownerPart(amount, shares) < 0) return 'amount';
  return null;
}
