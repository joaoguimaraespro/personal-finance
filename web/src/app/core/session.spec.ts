import { describe, expect, it } from 'vitest';
import { WARN_BEFORE_MS, sessionAction } from './session';

const IDLE = 12 * 60 * 60_000;
const MIN = 60_000;

describe('sessionAction', () => {
  it('silently renews an active user once half the idle timeout has passed', () => {
    expect(sessionAction(IDLE / 2 - MIN, IDLE, MIN, MIN)).toBe('extend');
    expect(sessionAction(IDLE - MIN, IDLE, MIN, MIN)).toBe('none');
  });

  it('warns an idle user shortly before expiry, after re-checking the server', () => {
    expect(sessionAction(WARN_BEFORE_MS - MIN, IDLE, 60 * MIN, 5 * MIN)).toBe('recheck');
    expect(sessionAction(WARN_BEFORE_MS - MIN, IDLE, 60 * MIN, 10_000)).toBe('warn');
  });

  it('re-checks periodically and when the time is up', () => {
    expect(sessionAction(IDLE / 2, IDLE, 60 * MIN, 6 * MIN)).toBe('recheck');
    expect(sessionAction(0, IDLE, MIN, 0)).toBe('expired');
  });
});
