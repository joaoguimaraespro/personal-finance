import { describe, expect, it } from 'vitest';
import { friendlyError, validateCredentials } from './credentials';

describe('validateCredentials', () => {
  it('accepts a Trading 212 key and secret', () => {
    expect(validateCredentials('Trading212', { apiKey: 'abc123', apiSecret: ' s3cr3t ' })).toEqual(
      [],
    );
  });

  it('asks for the Trading 212 secret as well as the key', () => {
    expect(validateCredentials('Trading212', { apiKey: 'abc123' })).toEqual([
      { key: 'apiSecret', message: 'connections.validation.required' },
    ]);
  });

  it('flags spaces pasted inside a value and the key pasted twice', () => {
    expect(validateCredentials('Trading212', { apiKey: 'ab c', apiSecret: 'same' })).toEqual([
      { key: 'apiKey', message: 'connections.validation.noSpaces' },
    ]);
    expect(validateCredentials('Trading212', { apiKey: 'same', apiSecret: 'same' })).toEqual([
      { key: 'apiSecret', message: 'connections.validation.t212SameValue' },
    ]);
  });

  it('requires a numeric IBKR query ID', () => {
    expect(
      validateCredentials('InteractiveBrokers', { token: '1234', queryId: 'My query' }),
    ).toEqual([{ key: 'queryId', message: 'connections.validation.ibkrQueryIdDigits' }]);
    expect(
      validateCredentials('InteractiveBrokers', { token: '1234', queryId: ' 987654 ' }),
    ).toEqual([]);
  });

  it('reports one problem per field', () => {
    expect(validateCredentials('InteractiveBrokers', {})).toEqual([
      { key: 'token', message: 'connections.validation.required' },
      { key: 'queryId', message: 'connections.validation.required' },
    ]);
  });

  it('checks a Binance key and secret like a Trading 212 one', () => {
    expect(validateCredentials('Binance', { apiKey: 'k', apiSecret: 's' })).toEqual([]);
    expect(validateCredentials('Binance', { apiKey: 'same', apiSecret: 'same' })).toEqual([
      { key: 'apiSecret', message: 'connections.validation.binanceSameValue' },
    ]);
  });

  it('has no rules for the demo broker', () => {
    expect(validateCredentials('Demo', {})).toEqual([]);
  });
});

describe('friendlyError', () => {
  it.each([
    [
      'Trading 212 rejected the API key (401). Create a new key and update the connection.',
      't212Unauthorized',
    ],
    ['The Trading 212 key lacks a required read permission (403). Enable …', 't212Forbidden'],
    ['The IBKR Flex token has expired. Generate a new one in Client Portal.', 'ibkrExpired'],
    ['The IBKR Flex token is restricted to another IP address.', 'ibkrIp'],
    ['The IBKR Flex query id is invalid.', 'ibkrQueryId'],
    ['The IBKR Flex token is invalid.', 'ibkrToken'],
    [
      'IBKR temporarily blocked the Flex token after too many failed attempts (Flex error 1025). Automatic syncs pause for 12 hours; try again later or generate a new Flex token.',
      'ibkrLocked',
    ],
    [
      'IBKR has no statement for the requested period (Flex error 1003). Check that the Query ID belongs to an Activity Flex Query of this account and that the account already has activity.',
      'ibkrNoStatement',
    ],
  ])('explains %s', (message, key) => {
    expect(friendlyError(message)).toBe(`connections.errors.${key}`);
  });

  it('explains a Binance key that can do more than read', () => {
    expect(
      friendlyError(
        'This Binance API key can do more than read (withdrawals). Create a key with only "Enable Reading" and update the connection.',
      ),
    ).toBe('connections.errors.binanceNotReadOnly');
  });

  it('leaves unknown errors alone', () => {
    expect(friendlyError('Trading 212 returned 502.')).toBeNull();
    expect(friendlyError(null)).toBeNull();
  });
});
