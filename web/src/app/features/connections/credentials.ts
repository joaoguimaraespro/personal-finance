import { Broker } from '../../core/models';

/** A friendly, translatable validation problem for one credential field. */
export interface FieldProblem {
  key: string;
  /** i18n key under connections.validation */
  message: string;
}

/**
 * Client-side checks that catch the usual copy/paste mistakes before anything is saved.
 * They never judge whether a credential is valid — only the broker can — so they stay permissive.
 */
export function validateCredentials(kind: Broker, fields: Record<string, string>): FieldProblem[] {
  const problems: FieldProblem[] = [];
  const value = (key: string) => (fields[key] ?? '').trim();
  const check = (key: string, ok: boolean, message: string) => {
    if (!ok) problems.push({ key, message: `connections.validation.${message}` });
  };

  if (kind === 'Trading212') {
    const key = value('apiKey');
    const secret = value('apiSecret');
    // Checks run in form order so the first problem is the first field to fix.
    check('apiKey', key.length > 0, 'required');
    check('apiKey', !/\s/.test(key), 'noSpaces');
    check('apiSecret', secret.length > 0, 'required');
    check('apiSecret', !/\s/.test(secret), 'noSpaces');
    if (key && secret) check('apiSecret', key !== secret, 't212SameValue');
  } else if (kind === 'InteractiveBrokers') {
    const token = value('token');
    const queryId = value('queryId');
    check('token', token.length > 0, 'required');
    check('token', !/\s/.test(token), 'noSpaces');
    check('queryId', queryId.length > 0, 'required');
    // The query ID is the number shown next to the query, not its name.
    if (queryId) check('queryId', /^\d+$/.test(queryId), 'ibkrQueryIdDigits');
    if (token && queryId) check('queryId', token !== queryId, 'ibkrSameValue');
  }

  // Keep only the first problem per field: one clear message beats three.
  return problems.filter((p, i) => problems.findIndex((q) => q.key === p.key) === i);
}

/**
 * Maps the sync errors the backend records (fixed English strings from the broker clients) to a
 * translated explanation and the guide step that fixes it. Unknown messages are shown as-is.
 */
export function friendlyError(message: string | null | undefined): string | null {
  if (!message) return null;
  const rules: [RegExp, string][] = [
    [/Trading 212 rejected the API key \(401\)/i, 't212Unauthorized'],
    [/Trading 212 key lacks a required read permission \(403\)/i, 't212Forbidden'],
    [/Trading 212 rate limit/i, 't212RateLimited'],
    [/Flex token has expired/i, 'ibkrExpired'],
    [/Flex token is restricted to another IP/i, 'ibkrIp'],
    [/Flex query id is invalid/i, 'ibkrQueryId'],
    [/Flex token is invalid/i, 'ibkrToken'],
    [/no statement for the requested period/i, 'ibkrNoStatement'],
  ];
  const hit = rules.find(([pattern]) => pattern.test(message));
  return hit ? `connections.errors.${hit[1]}` : null;
}
