import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
  untracked,
} from '@angular/core';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { NgIcon } from '@ng-icons/core';
import { firstValueFrom } from 'rxjs';
import { Api } from '../../core/api';
import { Confirm } from '../../core/confirm';
import { Prefs } from '../../core/prefs';
import { DayPipe, MoneyPipe, today } from '../../core/format';
import { CoinMatch, ManualHolding, RewardKind } from '../../core/models';
import { Toasts, problemMessage } from '../../core/toast';
import { DateFieldComponent } from '../../shared/date-field';
import { APP_ICONS } from '../../shared/icons';
import { ModalComponent } from '../../shared/modal';
import { SelectComponent, SelectOption } from '../../shared/select';
import { CoinSearchComponent } from './coin-search';

type Field = 'coin' | 'quantity' | 'price' | 'location' | 'heldSince' | 'notes';
type RewardField = 'quantity' | 'date';

/** Server error codes with a translated, friendlier message. */
const SERVER_MESSAGES: Record<string, string> = {
  'ManualHolding.NoPrice': 'crypto.error.noPrice',
  'ManualHolding.Duplicate': 'crypto.error.duplicate',
  'ManualHolding.PricesOff': 'crypto.error.pricesOff',
};

/** Accepts "1234.5", "1 234,5" and "1.234,5"; null when it is not a number. */
export function parseAmount(text: string): number | null {
  let t = text.trim().replace(/[\s €]/g, '');
  if (!t) return null;
  if (t.includes(',') && t.includes('.')) {
    t =
      t.lastIndexOf(',') > t.lastIndexOf('.')
        ? t.replace(/\./g, '').replace(',', '.')
        : t.replace(/,/g, '');
  } else {
    t = t.replace(',', '.');
  }
  if (!/^-?\d*\.?\d+$/.test(t)) return null;
  const n = Number(t);
  return Number.isFinite(n) ? n : null;
}

/**
 * Add or edit a coin held outside the connected brokers (an exchange account, a cold wallet): coin, quantity,
 * average buy price in EUR and where it is kept. In edit mode, staking/Earn/airdrop rewards can be added too.
 */
@Component({
  selector: 'app-crypto-dialog',
  imports: [
    ModalComponent,
    CoinSearchComponent,
    DateFieldComponent,
    SelectComponent,
    HlmInputImports,
    HlmButtonImports,
    NgIcon,
    TranslatePipe,
    DayPipe,
    MoneyPipe,
  ],
  providers: [APP_ICONS],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-modal
      [open]="open()"
      [title]="(holding() ? 'crypto.editTitle' : 'crypto.addTitle') | translate"
      (closed)="closed.emit()"
    >
      <form class="space-y-4" novalidate (submit)="$event.preventDefault(); save()">
        <div>
          <label class="label" for="crypto-coin">{{ 'crypto.coin' | translate }}</label>
          @if (coin(); as c) {
            <div class="flex items-center gap-2 rounded-md border px-3 py-2 text-sm">
              <ng-icon name="lucideCoins" class="text-amber-500" aria-hidden="true" />
              <span class="font-medium">{{ c.symbol }}</span>
              <span class="min-w-0 flex-1 truncate text-muted-foreground">{{ c.name }}</span>
              @if (!holding()) {
                <button type="button" hlmBtn variant="ghost" size="sm" (click)="coin.set(null)">
                  {{ 'crypto.change' | translate }}
                </button>
              }
            </div>
          } @else {
            <app-coin-search
              inputId="crypto-coin"
              [invalid]="!!errors().coin"
              (picked)="pickCoin($event)"
            />
          }
          @if (errors().coin; as e) {
            <p class="field-error">{{ e | translate }}</p>
          } @else if (!coin()) {
            <p class="mt-1.5 text-xs text-muted-foreground">{{ 'crypto.coinHint' | translate }}</p>
          }
        </div>

        <div class="grid gap-4 sm:grid-cols-2">
          <div>
            <label class="label" for="crypto-qty">{{ 'crypto.quantity' | translate }}</label>
            <input
              id="crypto-qty"
              hlmInput
              class="num w-full"
              inputmode="decimal"
              autocomplete="off"
              placeholder="0,5"
              [attr.aria-invalid]="!!errors().quantity || null"
              [value]="quantity()"
              (input)="quantity.set($any($event.target).value)"
            />
            @if (errors().quantity; as e) {
              <p class="field-error">{{ e | translate }}</p>
            }
          </div>
          <div>
            <label class="label" for="crypto-price">{{ 'crypto.avgPrice' | translate }}</label>
            <input
              id="crypto-price"
              hlmInput
              class="num w-full"
              inputmode="decimal"
              autocomplete="off"
              placeholder="40 000"
              [attr.aria-invalid]="!!errors().price || null"
              [value]="price()"
              (input)="price.set($any($event.target).value)"
            />
            @if (errors().price; as e) {
              <p class="field-error">{{ e | translate }}</p>
            } @else {
              <p class="mt-1.5 text-xs text-muted-foreground">
                {{ 'crypto.avgPriceHint' | translate }}
              </p>
            }
          </div>
        </div>

        <div class="grid gap-4 sm:grid-cols-2">
          <div>
            <label class="label" for="crypto-location">{{ 'crypto.location' | translate }}</label>
            <input
              id="crypto-location"
              hlmInput
              class="w-full"
              list="crypto-locations"
              autocomplete="off"
              maxlength="80"
              [placeholder]="'crypto.locationPlaceholder' | translate"
              [attr.aria-invalid]="!!errors().location || null"
              [value]="location()"
              (input)="location.set($any($event.target).value)"
            />
            <datalist id="crypto-locations">
              @for (l of locationSuggestions(); track l) {
                <option [value]="l"></option>
              }
            </datalist>
            @if (errors().location; as e) {
              <p class="field-error">{{ e | translate }}</p>
            }
          </div>
          <div>
            <label class="label" for="crypto-since">{{ 'crypto.heldSince' | translate }}</label>
            <app-date-field
              inputId="crypto-since"
              [value]="heldSince()"
              (valueChange)="heldSince.set($event || today)"
            />
            @if (errors().heldSince; as e) {
              <p class="field-error">{{ e | translate }}</p>
            } @else {
              <p class="mt-1.5 text-xs text-muted-foreground">
                {{ 'crypto.heldSinceHint' | translate }}
              </p>
            }
          </div>
        </div>

        <div>
          <label class="label" for="crypto-notes">{{ 'crypto.notes' | translate }}</label>
          <textarea
            id="crypto-notes"
            hlmInput
            class="min-h-16 w-full py-2"
            maxlength="500"
            [placeholder]="'crypto.notesPlaceholder' | translate"
            [value]="notes()"
            (input)="notes.set($any($event.target).value)"
          ></textarea>
        </div>

        @if (serverError(); as e) {
          <p class="field-error" role="alert">{{ e }}</p>
        }

        <div class="flex flex-wrap items-center justify-between gap-2 pt-1">
          @if (holding()) {
            <button
              type="button"
              hlmBtn
              variant="ghost"
              class="text-destructive hover:text-destructive"
              (click)="remove()"
            >
              <ng-icon name="lucideTrash2" aria-hidden="true" />{{ 'common.delete' | translate }}
            </button>
          } @else {
            <span></span>
          }
          <div class="flex gap-2">
            <button type="button" hlmBtn variant="outline" (click)="closed.emit()">
              {{ 'common.cancel' | translate }}
            </button>
            <button hlmBtn [disabled]="busy()">
              {{ (holding() ? 'common.save' : 'crypto.add') | translate }}
            </button>
          </div>
        </div>
      </form>

      @if (holding(); as h) {
        <section class="mt-6 border-t pt-5" aria-labelledby="crypto-rewards">
          <h3 id="crypto-rewards" class="text-sm font-semibold">
            {{ 'crypto.rewards' | translate }}
          </h3>
          <p class="mt-1 text-xs text-muted-foreground">{{ 'crypto.rewardsHint' | translate }}</p>

          @if (h.rewards.length) {
            <ul class="mt-3 divide-y rounded-md border text-sm">
              @for (r of h.rewards; track r.id) {
                <li class="flex items-center gap-2 px-3 py-2">
                  <div class="min-w-0 flex-1">
                    <span class="num font-medium">+{{ r.quantity }} {{ h.symbol }}</span>
                    <span class="text-muted-foreground">
                      · {{ 'crypto.rewardKind.' + r.kind | translate }} ·
                      {{ r.receivedOn | day: 'short' }}</span
                    >
                    @if (r.note) {
                      <div class="truncate text-xs text-muted-foreground">{{ r.note }}</div>
                    }
                  </div>
                  <span class="num shrink-0 text-emerald-700 dark:text-emerald-400">{{
                    r.valueBase | money
                  }}</span>
                  <button
                    type="button"
                    hlmBtn
                    variant="ghost"
                    size="icon-sm"
                    [attr.aria-label]="'crypto.removeReward' | translate"
                    (click)="removeReward(r.id)"
                  >
                    <ng-icon name="lucideTrash2" aria-hidden="true" />
                  </button>
                </li>
              }
            </ul>
          }

          <form
            class="mt-3 grid grid-cols-2 gap-3 sm:grid-cols-[1fr_1fr_1fr_auto] sm:items-end"
            novalidate
            (submit)="$event.preventDefault(); addReward()"
          >
            <div>
              <label class="label" for="reward-qty">{{ 'crypto.quantity' | translate }}</label>
              <input
                id="reward-qty"
                hlmInput
                class="num w-full"
                inputmode="decimal"
                autocomplete="off"
                [attr.aria-invalid]="!!rewardErrors().quantity || null"
                [value]="rewardQty()"
                (input)="rewardQty.set($any($event.target).value)"
              />
            </div>
            <div>
              <label class="label" for="reward-date">{{ 'crypto.receivedOn' | translate }}</label>
              <app-date-field
                inputId="reward-date"
                [value]="rewardDate()"
                (valueChange)="rewardDate.set($event || today)"
              />
            </div>
            <div class="col-span-2 sm:col-span-1">
              <label class="label" for="reward-kind">{{ 'crypto.rewardType' | translate }}</label>
              <app-select
                inputId="reward-kind"
                [options]="kindOptions()"
                [value]="rewardKind()"
                (valueChange)="rewardKind.set($any($event))"
              />
            </div>
            <button hlmBtn variant="outline" class="col-span-2 sm:col-span-1" [disabled]="busy()">
              <ng-icon name="lucidePlus" aria-hidden="true" />{{ 'crypto.addReward' | translate }}
            </button>
            @if (rewardErrors().quantity ?? rewardErrors().date; as e) {
              <p class="field-error col-span-2 sm:col-span-4">{{ e | translate }}</p>
            }
          </form>
        </section>
      }
    </app-modal>
  `,
  styles: `
    .field-error {
      margin-top: 0.375rem;
      font-size: 0.75rem;
      color: var(--destructive);
    }
    textarea[hlmInput] {
      height: auto;
    }
  `,
})
export class CryptoDialogComponent {
  private readonly api = inject(Api);
  private readonly confirm = inject(Confirm);
  private readonly toasts = inject(Toasts);
  private readonly i18n = inject(TranslateService);
  private readonly prefs = inject(Prefs);

  readonly open = input(false);
  /** The holding to edit; null to add a new one. */
  readonly holding = input<ManualHolding | null>(null);
  readonly locations = input<string[]>([]);
  readonly closed = output<void>();
  /** Something changed: the portfolio should reload. */
  readonly changed = output<void>();

  protected readonly today = today();
  protected readonly coin = signal<CoinMatch | null>(null);
  protected readonly quantity = signal('');
  protected readonly price = signal('');
  protected readonly location = signal('');
  protected readonly heldSince = signal(today());
  protected readonly notes = signal('');
  protected readonly errors = signal<Partial<Record<Field, string>>>({});
  protected readonly serverError = signal<string | null>(null);
  protected readonly busy = signal(false);

  protected readonly rewardQty = signal('');
  protected readonly rewardDate = signal(today());
  protected readonly rewardKind = signal<RewardKind>('Staking');
  protected readonly rewardErrors = signal<Partial<Record<RewardField, string>>>({});

  protected readonly locationSuggestions = computed(() => {
    const typed = this.location().trim().toLowerCase();
    return this.locations().filter((l) => l.toLowerCase() !== typed);
  });

  protected readonly kindOptions = computed<SelectOption[]>(() =>
    (['Staking', 'Earn', 'Airdrop', 'Other'] as RewardKind[]).map((k) => ({
      value: k,
      label: this.i18n.instant('crypto.rewardKind.' + k),
    })),
  );

  constructor() {
    // Reset the form when the dialog opens or another holding is shown — not when the same holding is
    // reloaded after a reward was added.
    effect(() => {
      const open = this.open();
      const h = this.holding();
      if (!open) {
        this.shownFor = undefined;
        return;
      }
      const key = h?.id ?? null;
      if (this.shownFor === key) return;
      this.shownFor = key;
      untracked(() => this.reset(h));
    });
  }

  private shownFor: string | null | undefined;

  private reset(h: ManualHolding | null) {
    this.coin.set(h ? { id: h.coinId, symbol: h.symbol, name: h.name } : null);
    this.quantity.set(h ? this.plain(h.quantity) : '');
    this.price.set(h ? this.plain(h.averagePrice) : '');
    this.location.set(h?.location ?? (this.locations().length === 1 ? this.locations()[0] : ''));
    this.heldSince.set(h?.heldSince ?? today());
    this.notes.set(h?.notes ?? '');
    this.errors.set({});
    this.serverError.set(null);
    this.rewardQty.set('');
    this.rewardDate.set(today());
    this.rewardKind.set('Staking');
    this.rewardErrors.set({});
  }

  /** A number as the user would type it in their locale (0,05), without grouping. */
  private plain(n: number): string {
    return new Intl.NumberFormat(this.prefs.locale(), {
      useGrouping: false,
      maximumFractionDigits: 12,
    }).format(n);
  }

  protected pickCoin(c: CoinMatch) {
    this.coin.set(c);
    this.errors.update((e) => ({ ...e, coin: undefined }));
  }

  private validate(): { quantity: number; price: number } | null {
    const errors: Partial<Record<Field, string>> = {};
    const quantity = parseAmount(this.quantity());
    const price = parseAmount(this.price());
    if (!this.coin()) errors.coin = 'crypto.error.coin';
    if (quantity === null) errors.quantity = 'crypto.error.quantity';
    else if (quantity <= 0) errors.quantity = 'crypto.error.quantityPositive';
    if (price === null) errors.price = 'crypto.error.price';
    else if (price < 0) errors.price = 'crypto.error.priceNegative';
    if (this.location().trim().length > 80) errors.location = 'crypto.error.location';
    if (this.heldSince() > today()) errors.heldSince = 'crypto.error.future';
    this.errors.set(errors);
    return Object.keys(errors).length ? null : { quantity: quantity!, price: price! };
  }

  protected async save() {
    this.serverError.set(null);
    const values = this.validate();
    if (!values) return;
    const body = {
      quantity: values.quantity,
      averagePrice: values.price,
      location: this.location().trim() || null,
      notes: this.notes().trim() || null,
      heldSince: this.heldSince(),
    };
    this.busy.set(true);
    try {
      const h = this.holding();
      if (h) {
        await firstValueFrom(this.api.updateManualHolding(h.id, body));
      } else {
        const c = this.coin()!;
        await firstValueFrom(
          this.api.createManualHolding({ ...body, coinId: c.id, symbol: c.symbol, name: c.name }),
        );
      }
      this.toasts.show(this.i18n.instant(h ? 'crypto.saved' : 'crypto.added'));
      this.changed.emit();
      this.closed.emit();
    } catch (err) {
      this.serverError.set(this.message(err));
    } finally {
      this.busy.set(false);
    }
  }

  protected async remove() {
    const h = this.holding();
    if (!h) return;
    const ok = await this.confirm.ask(
      this.i18n.instant('crypto.confirmDelete', { symbol: h.symbol, location: h.location }),
      { destructive: true, confirmLabel: this.i18n.instant('common.delete') },
    );
    if (!ok) return;
    try {
      await firstValueFrom(this.api.deleteManualHolding(h.id));
      this.changed.emit();
      this.closed.emit();
    } catch (err) {
      this.toasts.error(err);
    }
  }

  protected async addReward() {
    const h = this.holding();
    if (!h) return;
    const quantity = parseAmount(this.rewardQty());
    const errors: Partial<Record<RewardField, string>> = {};
    if (quantity === null || quantity <= 0) errors.quantity = 'crypto.error.rewardQuantity';
    if (this.rewardDate() > today()) errors.date = 'crypto.error.future';
    this.rewardErrors.set(errors);
    if (Object.keys(errors).length) return;
    this.busy.set(true);
    try {
      await firstValueFrom(
        this.api.addReward(h.id, {
          quantity,
          receivedOn: this.rewardDate(),
          kind: this.rewardKind(),
        }),
      );
      this.rewardQty.set('');
      this.toasts.show(this.i18n.instant('crypto.rewardAdded'));
      this.changed.emit();
    } catch (err) {
      this.toasts.show(this.message(err), 'error');
    } finally {
      this.busy.set(false);
    }
  }

  protected async removeReward(rewardId: string) {
    const h = this.holding();
    if (!h) return;
    try {
      await firstValueFrom(this.api.deleteReward(h.id, rewardId));
      this.changed.emit();
    } catch (err) {
      this.toasts.error(err);
    }
  }

  private message(err: unknown): string {
    const code = (err as { error?: { code?: string } })?.error?.code;
    const key = code ? SERVER_MESSAGES[code] : undefined;
    return key ? this.i18n.instant(key) : problemMessage(err);
  }
}
