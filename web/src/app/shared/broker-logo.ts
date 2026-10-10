import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { NgIcon } from '@ng-icons/core';
import { Broker } from '../core/models';
import { APP_ICONS } from './icons';

/**
 * Official broker symbol, used only to identify the broker (nominative use; trademarks belong to their
 * owners — see README). Brokers without a logo (e.g. Demo) fall back to a neutral icon.
 */
const LOGOS: Partial<Record<Broker, string>> = {
  Trading212: 'brokers/trading212.png',
  InteractiveBrokers: 'brokers/interactive-brokers.png',
  Binance: 'wallets/binance.png',
};

/** Exchanges and hardware wallets recognised in the name of a hand-entered crypto wallet (same terms as above). */
const WALLET_LOGOS: [RegExp, string][] = [
  [/\bbinance\b/i, 'wallets/binance.png'],
  [/\bcoinbase\b/i, 'wallets/coinbase.png'],
  [/\bkraken\b/i, 'wallets/kraken.png'],
  [/\bledger\b/i, 'wallets/ledger.png'],
  [/\btrezor\b/i, 'wallets/trezor.png'],
  [/\bsafe ?pal\b/i, 'wallets/safepal.png'],
];

/** Logo of a hand-entered wallet whose name mentions a known exchange or hardware wallet ("Binance Earn"). */
export function walletLogo(name: string | null | undefined): string | null {
  const n = name ?? '';
  return WALLET_LOGOS.find(([pattern]) => pattern.test(n))?.[1] ?? null;
}

@Component({
  selector: 'app-broker-logo',
  imports: [NgIcon],
  providers: [APP_ICONS],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    class: 'inline-flex shrink-0 items-center justify-center overflow-hidden',
    '[style.width.px]': 'size()',
    '[style.height.px]': 'size()',
    '[style.border-radius]': 'size() * 0.24 + "px"',
  },
  template: `
    @if (src(); as s) {
      <img [src]="s" alt="" class="size-full object-cover" loading="lazy" />
    } @else {
      <span class="bg-muted text-muted-foreground flex size-full items-center justify-center">
        <ng-icon
          [name]="broker() === 'Manual' ? 'lucideWallet' : 'lucideLandmark'"
          [style.font-size.px]="size() * 0.5"
        />
      </span>
    }
  `,
})
export class BrokerLogoComponent {
  readonly broker = input.required<Broker | string>();
  /** Account name; a hand-entered crypto wallet named after an exchange or device gets its logo. */
  readonly name = input<string | null>(null);
  readonly size = input(40);
  protected readonly src = computed(() =>
    this.broker() === 'Manual' ? walletLogo(this.name()) : (LOGOS[this.broker() as Broker] ?? null),
  );
}
