import { coinIconName, coinInitials } from './coin-icon';
import { walletLogo } from './broker-logo';

describe('coinIconName', () => {
  it('maps plain tickers and quote pairs to the icon file', () => {
    expect(coinIconName('BTC')).toBe('btc');
    expect(coinIconName(' eth ')).toBe('eth');
    expect(coinIconName('BTC-EUR')).toBe('btc');
    expect(coinIconName('SOL-USD')).toBe('sol');
    expect(coinIconName('ETH/USDT')).toBe('eth');
    expect(coinIconName('ETH-BTC')).toBe('eth');
    expect(coinIconName('USDT-USD')).toBe('usdt');
  });

  it("drops Yahoo's numeric disambiguation suffix but keeps real digits", () => {
    expect(coinIconName('PEPE24478-USD')).toBe('pepe');
    expect(coinIconName('UNI7083')).toBe('uni');
    expect(coinIconName('1INCH')).toBe('1inch');
    expect(coinIconName('EMC2')).toBe('emc2');
  });

  it('returns null for text that cannot be a ticker', () => {
    expect(coinIconName('')).toBeNull();
    expect(coinIconName(null)).toBeNull();
    expect(coinIconName('../etc')).toBeNull();
    expect(coinIconName('BTC EUR')).toBeNull();
  });

  it('builds the fallback initials', () => {
    expect(coinInitials('KAS-USD')).toBe('KAS');
    expect(coinInitials('DOGECOIN')).toBe('DOG');
    expect(coinInitials('')).toBe('?');
  });
});

describe('walletLogo', () => {
  it('recognises exchanges and hardware wallets in a wallet name', () => {
    expect(walletLogo('Binance')).toBe('wallets/binance.png');
    expect(walletLogo('binance earn')).toBe('wallets/binance.png');
    expect(walletLogo('Ledger Nano X')).toBe('wallets/ledger.png');
    expect(walletLogo('Coinbase')).toBe('wallets/coinbase.png');
    expect(walletLogo('Kraken')).toBe('wallets/kraken.png');
    expect(walletLogo('Trezor')).toBe('wallets/trezor.png');
    expect(walletLogo('SafePal S1')).toBe('wallets/safepal.png');
    expect(walletLogo('Safe Pal')).toBe('wallets/safepal.png');
  });

  it('has no logo for other names', () => {
    expect(walletLogo('Cold wallet')).toBeNull();
    expect(walletLogo('Crypto')).toBeNull();
    expect(walletLogo(null)).toBeNull();
  });
});
