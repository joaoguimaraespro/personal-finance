import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { NgIcon } from '@ng-icons/core';
import { MoneyPipe, PercentPipe } from '../../core/format';
import { AccountTotal, Broker, PortfolioSummary } from '../../core/models';
import { Prefs } from '../../core/prefs';
import { BrokerLogoComponent } from '../../shared/broker-logo';
import { APP_ICONS } from '../../shared/icons';

interface WalletCard {
  /** '' for all wallets, else the account id. */
  key: string;
  name: string;
  broker: Broker | null;
  detail: string;
  value: number;
  dayChange: number | null;
  dayChangePercent: number | null;
  returnPercent: number | null;
}

/**
 * One card per wallet — each broker account and each hand-entered crypto location — plus "All". Picking a card
 * scopes the whole portfolio page to it. Scrolls sideways inside itself when the cards don't fit.
 */
@Component({
  selector: 'app-wallet-strip',
  imports: [BrokerLogoComponent, NgIcon, MoneyPipe, TranslatePipe],
  providers: [APP_ICONS],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'block min-w-0' },
  template: `
    <div role="group" [attr.aria-label]="'wallets.title' | translate" class="strip">
      @for (c of cards(); track c.key) {
        <button
          type="button"
          class="wallet bg-card text-card-foreground rounded-xl border shadow-xs dark:shadow-none"
          [class.selected]="c.key === selected()"
          [attr.aria-pressed]="c.key === selected()"
          [attr.data-wallet]="c.key || 'all'"
          (click)="selectedChange.emit(c.key)"
        >
          <span class="flex min-w-0 items-center gap-2.5">
            @if (c.broker; as b) {
              <app-broker-logo [broker]="b" [name]="c.name" [size]="28" />
            } @else {
              <span
                class="bg-primary/10 text-primary flex size-7 shrink-0 items-center justify-center rounded-md"
              >
                <ng-icon name="lucideLayers" class="text-sm" aria-hidden="true" />
              </span>
            }
            <span class="min-w-0 flex-1">
              <span class="block truncate text-sm font-medium leading-tight">{{ c.name }}</span>
              <span class="text-muted-foreground block truncate text-[11px] leading-tight">{{
                c.detail
              }}</span>
            </span>
          </span>
          <span class="num block text-lg leading-tight font-semibold tracking-tight">{{
            c.value | money
          }}</span>
          <!-- Two labelled lines: today's move and the total return are different measures. -->
          <span class="block space-y-0.5 text-[11px] leading-tight">
            <span class="flex items-center justify-between gap-2">
              <span class="text-muted-foreground">{{ 'wallets.today' | translate }}</span>
              @if (c.dayChange !== null) {
                <span class="num truncate" [class]="tone(c.dayChange)"
                  >{{ c.dayChange | money: 'EUR' : true }} ({{
                    signedPct(c.dayChangePercent)
                  }})</span
                >
              } @else {
                <span class="text-muted-foreground">—</span>
              }
            </span>
            <span class="flex items-center justify-between gap-2">
              <span class="text-muted-foreground">{{ 'wallets.return' | translate }}</span>
              <span class="num shrink-0 font-medium" [class]="tone(c.returnPercent)">{{
                signedPct(c.returnPercent)
              }}</span>
            </span>
          </span>
        </button>
      }
    </div>
  `,
  styles: `
    /* Scrolls sideways inside itself; on phones it bleeds to the screen edges like a carousel. */
    .strip {
      display: flex;
      gap: 0.75rem;
      overflow-x: auto;
      scroll-snap-type: x mandatory;
      scroll-padding-inline: 1rem;
      margin-inline: -1rem;
      padding: 0.125rem 1rem 0.5rem;
    }
    .wallet {
      display: flex;
      flex: none;
      flex-direction: column;
      gap: 0.5rem;
      width: 12.5rem;
      padding: 0.875rem;
      text-align: left;
      scroll-snap-align: start;
      transition:
        box-shadow 0.2s,
        border-color 0.2s,
        background-color 0.2s;
    }
    .wallet:hover {
      border-color: color-mix(in oklab, var(--ring) 40%, transparent);
    }
    .wallet:focus-visible {
      outline: none;
      box-shadow: 0 0 0 3px color-mix(in oklab, var(--ring) 50%, transparent);
    }
    @media (min-width: 40rem) {
      .strip {
        margin-inline: 0;
        padding-inline: 0;
        scroll-padding-inline: 0;
      }
      .wallet {
        flex: 1 0 11rem;
        width: auto;
        max-width: 17rem;
      }
    }
    .wallet.selected {
      border-color: var(--primary);
      background-color: color-mix(in oklab, var(--primary) 6%, var(--card));
      box-shadow: 0 0 0 1px var(--primary);
    }
  `,
})
export class WalletStripComponent {
  private readonly i18n = inject(TranslateService);
  private readonly prefs = inject(Prefs);
  private readonly pct = new PercentPipe();

  /** The unscoped summary: its totals feed "All" and its accounts the other cards. */
  readonly summary = input.required<PortfolioSummary>();
  /** '' for all wallets, else the selected account id. */
  readonly selected = input('');
  readonly selectedChange = output<string>();

  protected readonly cards = computed<WalletCard[]>(() => {
    const s = this.summary();
    this.prefs.translations();
    const wallets = [...s.accounts].sort(
      (a, b) => b.marketValue + b.cash - (a.marketValue + a.cash) || a.name.localeCompare(b.name),
    );
    return [
      {
        key: '',
        name: this.i18n.instant('wallets.all'),
        broker: null,
        detail: this.i18n.instant('wallets.allHint'),
        value: s.totalValue,
        dayChange: s.dayChange,
        dayChangePercent: s.dayChangePercent,
        returnPercent: s.totalReturnPercent,
      },
      ...wallets.map((a) => ({
        key: a.accountId,
        name: a.name,
        broker: a.broker,
        detail: this.detail(a),
        value: a.marketValue + a.cash,
        dayChange: a.dayChange ?? null,
        dayChangePercent: a.dayChangePercent ?? null,
        returnPercent: a.totalReturnPercent ?? null,
      })),
    ];
  });

  /** "Trading 212 · 12 positions"; crypto wallets count coins. The provider is left out when it's the name. */
  private detail(a: AccountTotal): string {
    const crypto = a.broker === 'Manual';
    const count = a.positions ?? 0;
    const items = this.i18n.instant(
      crypto
        ? count === 1
          ? 'wallets.coinsOne'
          : 'wallets.coinsMany'
        : count === 1
          ? 'wallets.positionsOne'
          : 'wallets.positionsMany',
      { count },
    );
    const provider = this.i18n.instant(crypto ? 'wallets.crypto' : `source.${a.broker}`);
    return provider.toLowerCase() === a.name.trim().toLowerCase()
      ? items
      : `${provider} · ${items}`;
  }

  protected tone(value: number | null | undefined): string {
    if (value === null || value === undefined || value === 0) return 'text-muted-foreground';
    return value > 0
      ? 'text-emerald-700 dark:text-emerald-400'
      : 'text-rose-600 dark:text-rose-400';
  }

  protected signedPct(value: number | null | undefined): string {
    if (value === null || value === undefined) return '—';
    const text = this.pct.transform(Math.abs(value));
    return value > 0 ? `+${text}` : value < 0 ? `−${text}` : text;
  }
}
