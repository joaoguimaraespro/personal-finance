import { ChangeDetectionStrategy, Component, computed, input, linkedSignal } from '@angular/core';

/** Quote currencies a ticker may carry (Yahoo "BTC-EUR", exchange pairs "ETH/USDT"). */
const QUOTES = new Set(['EUR', 'USD', 'USDT', 'USDC', 'GBP', 'CHF', 'BTC', 'ETH']);

/**
 * File name (without .svg) of a coin's icon under /coins, from the ticker as it appears anywhere in the app:
 * "BTC" → "btc", "BTC-EUR" → "btc", "eth/usdt" → "eth", Yahoo's disambiguated "PEPE24478-USD" → "pepe".
 * Null when the text can't be a ticker, so no request is made.
 */
export function coinIconName(symbol: string | null | undefined): string | null {
  let s = (symbol ?? '').trim().toUpperCase();
  const pair = /^(.+?)[-/]([A-Z]+)$/.exec(s);
  if (pair && QUOTES.has(pair[2])) s = pair[1];
  // Yahoo appends a numeric id (4+ digits) when tickers clash; real tickers end in at most a couple of digits.
  s = s.replace(/^([A-Z][A-Z0-9]*?)\d{4,}$/, '$1');
  return /^[A-Z0-9]{1,12}$/.test(s) ? s.toLowerCase() : null;
}

/** Up to three letters for the fallback badge. */
export function coinInitials(symbol: string | null | undefined): string {
  const name = coinIconName(symbol);
  return (name ?? (symbol ?? '').replace(/[^a-z0-9]/gi, '')).slice(0, 3).toUpperCase() || '?';
}

/**
 * A coin's colour icon (cryptocurrency-icons, CC0, served as static files from /coins), loaded lazily. Coins
 * without an icon show their first letters instead.
 */
@Component({
  selector: 'app-coin-icon',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    class: 'inline-flex shrink-0 items-center justify-center',
    '[style.width.px]': 'size()',
    '[style.height.px]': 'size()',
    'aria-hidden': 'true',
  },
  template: `
    @if (src(); as s) {
      <img
        [src]="s"
        alt=""
        class="size-full"
        loading="lazy"
        decoding="async"
        [width]="size()"
        [height]="size()"
        (error)="failed.set(true)"
      />
    } @else {
      <span
        class="flex size-full items-center justify-center rounded-full bg-amber-500/15 font-semibold leading-none text-amber-700 dark:text-amber-400"
        [style.font-size.px]="size() * (initials().length > 2 ? 0.3 : 0.38)"
        >{{ initials() }}</span
      >
    }
  `,
})
export class CoinIconComponent {
  readonly symbol = input.required<string>();
  readonly size = input(20);

  private readonly name = computed(() => coinIconName(this.symbol()));
  /** Reset when the symbol changes, so a missing icon doesn't stick to the next coin. */
  protected readonly failed = linkedSignal({ source: this.name, computation: () => false });
  protected readonly src = computed(() =>
    this.name() && !this.failed() ? `coins/${this.name()}.svg` : null,
  );
  protected readonly initials = computed(() => coinInitials(this.symbol()));
}
