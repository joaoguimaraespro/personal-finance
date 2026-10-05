/** A permission an AI client can be granted, as listed by /api/ai-admin/catalog. */
export interface Scope {
  name: string;
  description: string;
  sensitive: boolean;
  write?: boolean;
}

export interface ScopeGroups {
  /** Everyday read scopes ("Leitura"). */
  read: Scope[];
  /** Read scopes that add identifiers or notes: off unless really needed. */
  sensitiveRead: Scope[];
  /** Opt-in write scopes ("Escrita"): all sensitive. */
  write: Scope[];
}

export function groupScopes(scopes: readonly Scope[]): ScopeGroups {
  return {
    read: scopes.filter((s) => !s.write && !s.sensitive),
    sensitiveRead: scopes.filter((s) => !s.write && s.sensitive),
    write: scopes.filter((s) => !!s.write),
  };
}

export function isWriteScope(scopes: readonly Scope[], name: string): boolean {
  return scopes.some((s) => s.name === name && !!s.write);
}

/** True when every scope of the group is selected (and the group is not empty). */
export function allSelected(selected: ReadonlySet<string>, group: readonly Scope[]): boolean {
  return group.length > 0 && group.every((s) => selected.has(s.name));
}

/**
 * "Select all" for a group: selects every scope in it, or — when they are all selected already — clears them.
 * Other groups are left as they are.
 */
export function toggleGroup(selected: ReadonlySet<string>, group: readonly Scope[]): Set<string> {
  const next = new Set(selected);
  if (allSelected(selected, group)) group.forEach((s) => next.delete(s.name));
  else group.forEach((s) => next.add(s.name));
  return next;
}

export function toggleScope(selected: ReadonlySet<string>, name: string): Set<string> {
  const next = new Set(selected);
  if (next.has(name)) next.delete(name);
  else next.add(name);
  return next;
}

/** Write scopes that `next` grants and `previous` did not: enabling them needs the owner's confirmation. */
export function newlyGrantedWrites(
  previous: ReadonlySet<string>,
  next: ReadonlySet<string>,
  scopes: readonly Scope[],
): string[] {
  return scopes
    .filter((s) => s.write && next.has(s.name) && !previous.has(s.name))
    .map((s) => s.name);
}

/** An audit entry of a delete whose record is still in the recycle bin can be restored from the log. */
export function restorableItemId(
  event: { tool: string; decision: string; recordId?: string | null },
  bin: readonly { id: string; recordId: string }[],
): string | null {
  if (event.decision !== 'Allowed' || !event.recordId || !event.tool.startsWith('delete_'))
    return null;
  return bin.find((b) => b.recordId === event.recordId)?.id ?? null;
}

/** Whole days left before a recycle-bin item is purged (never negative). */
export function daysLeft(purgeAfterUtc: string, now: Date = new Date()): number {
  const ms = new Date(purgeAfterUtc).getTime() - now.getTime();
  return Math.max(0, Math.ceil(ms / 86_400_000));
}
