import { describe, expect, it } from 'vitest';
import {
  allSelected,
  daysLeft,
  groupScopes,
  isWriteScope,
  newlyGrantedWrites,
  restorableItemId,
  Scope,
  toggleGroup,
  toggleScope,
} from './scope-selection';

const scopes: Scope[] = [
  { name: 'overview.read', description: '', sensitive: false },
  { name: 'goals.read', description: '', sensitive: false },
  { name: 'personal.notes.read', description: '', sensitive: true },
  { name: 'transactions.write', description: '', sensitive: true, write: true },
  { name: 'planning.write', description: '', sensitive: true, write: true },
];

describe('groupScopes', () => {
  it('splits read, sensitive read and write scopes', () => {
    const groups = groupScopes(scopes);
    expect(groups.read.map((s) => s.name)).toEqual(['overview.read', 'goals.read']);
    expect(groups.sensitiveRead.map((s) => s.name)).toEqual(['personal.notes.read']);
    expect(groups.write.map((s) => s.name)).toEqual(['transactions.write', 'planning.write']);
  });

  it('treats a catalogue without write flags as read-only', () => {
    const old = scopes.map(({ write: _write, ...s }) => s);
    expect(groupScopes(old).write).toEqual([]);
  });

  it('knows which scopes write', () => {
    expect(isWriteScope(scopes, 'planning.write')).toBe(true);
    expect(isWriteScope(scopes, 'overview.read')).toBe(false);
    expect(isWriteScope(scopes, 'unknown')).toBe(false);
  });
});

describe('select all per group', () => {
  const { read, write } = groupScopes(scopes);

  it('selects the whole group and leaves the others alone', () => {
    const next = toggleGroup(new Set(['personal.notes.read']), read);
    expect([...next].sort()).toEqual(['goals.read', 'overview.read', 'personal.notes.read']);
    expect(allSelected(next, read)).toBe(true);
    expect(allSelected(next, write)).toBe(false);
  });

  it('clears the group when it is already fully selected', () => {
    const next = toggleGroup(new Set(['overview.read', 'goals.read', 'transactions.write']), read);
    expect([...next]).toEqual(['transactions.write']);
  });

  it('completes a partly selected group instead of clearing it', () => {
    const next = toggleGroup(new Set(['transactions.write']), write);
    expect([...next].sort()).toEqual(['planning.write', 'transactions.write']);
  });

  it('never reports an empty group as all selected', () => {
    expect(allSelected(new Set(['x']), [])).toBe(false);
  });

  it('toggles one scope without mutating the original set', () => {
    const original = new Set(['overview.read']);
    expect([...toggleScope(original, 'goals.read')].sort()).toEqual([
      'goals.read',
      'overview.read',
    ]);
    expect([...toggleScope(original, 'overview.read')]).toEqual([]);
    expect([...original]).toEqual(['overview.read']);
  });
});

describe('newlyGrantedWrites', () => {
  it('lists only write scopes that were not granted before', () => {
    const before = new Set(['overview.read', 'planning.write']);
    const after = new Set(['overview.read', 'goals.read', 'planning.write', 'transactions.write']);
    expect(newlyGrantedWrites(before, after, scopes)).toEqual(['transactions.write']);
  });

  it('is empty when writes are removed or only reads are added', () => {
    expect(newlyGrantedWrites(new Set(['transactions.write']), new Set(), scopes)).toEqual([]);
    expect(newlyGrantedWrites(new Set(), new Set(['goals.read']), scopes)).toEqual([]);
  });
});

describe('recycle bin helpers', () => {
  const bin = [{ id: 'bin-1', recordId: 'tx-1' }];

  it('links an allowed delete still in the bin to its restore', () => {
    expect(
      restorableItemId({ tool: 'delete_transaction', decision: 'Allowed', recordId: 'tx-1' }, bin),
    ).toBe('bin-1');
  });

  it('does not offer restore for other writes, denied deletes or items no longer in the bin', () => {
    expect(
      restorableItemId({ tool: 'update_transaction', decision: 'Allowed', recordId: 'tx-1' }, bin),
    ).toBeNull();
    expect(
      restorableItemId({ tool: 'delete_transaction', decision: 'Denied', recordId: 'tx-1' }, bin),
    ).toBeNull();
    expect(
      restorableItemId({ tool: 'delete_transaction', decision: 'Allowed', recordId: 'tx-2' }, bin),
    ).toBeNull();
    expect(
      restorableItemId({ tool: 'delete_transaction', decision: 'Allowed', recordId: null }, bin),
    ).toBeNull();
  });

  it('counts whole days left before the purge', () => {
    const now = new Date('2026-10-05T12:00:00Z');
    expect(daysLeft('2026-11-04T12:00:00Z', now)).toBe(30);
    expect(daysLeft('2026-10-05T13:00:00Z', now)).toBe(1);
    expect(daysLeft('2026-10-01T00:00:00Z', now)).toBe(0);
  });
});
