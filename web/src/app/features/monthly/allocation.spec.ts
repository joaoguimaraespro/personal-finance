import { autoStatus, canRecord, effectiveStatus, remaining } from './allocation';

describe('allocation status', () => {
  it('follows what was set aside against the target', () => {
    expect(autoStatus({ target: null, actual: 10 })).toBeNull();
    expect(autoStatus({ target: 0, actual: 10 })).toBeNull();
    expect(autoStatus({ target: 300, actual: 0 })).toBe('Todo');
    expect(autoStatus({ target: 300, actual: 120 })).toBe('Partial');
    expect(autoStatus({ target: 300, actual: 300 })).toBe('Done');
    expect(autoStatus({ target: 300, actual: 400 })).toBe('Done');
  });

  it('lets a status picked by hand win', () => {
    expect(effectiveStatus('NotApplicable', { target: 300, actual: 0 })).toBe('NotApplicable');
    expect(effectiveStatus(undefined, { target: 300, actual: 100 })).toBe('Partial');
  });

  it('offers to record what is left until done or marked', () => {
    expect(remaining({ target: 300, actual: 120.4 })).toBe(179.6);
    expect(remaining({ target: 300, actual: 400 })).toBe(0);
    expect(canRecord(undefined, { target: 300, actual: 120 })).toBe(true);
    expect(canRecord(undefined, { target: 300, actual: 300 })).toBe(false);
    expect(canRecord('Done', { target: 300, actual: 0 })).toBe(false);
    expect(canRecord('NotApplicable', { target: 300, actual: 0 })).toBe(false);
    expect(canRecord(undefined, { target: null, actual: 0 })).toBe(false);
  });
});
