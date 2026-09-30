import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  computed,
  effect,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { liveResource } from '../../core/resource';
import { NgIcon } from '@ng-icons/core';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { firstValueFrom, of } from 'rxjs';
import { Api } from '../../core/api';
import { DataEvents, QuickAdd } from '../../core/data-events';
import { today } from '../../core/format';
import {
  AssetClass,
  AssetPriceSource,
  ExpenseNature,
  FLOW_TYPES,
  InstrumentMatch,
  InvestmentAsset,
  InvestmentAssetKind,
  TransactionFlow,
  TransactionRequest,
  TransactionType,
  flowOf,
} from '../../core/models';
import { Prefs } from '../../core/prefs';
import { Toasts } from '../../core/toast';
import { ModalComponent } from '../../shared/modal';
import { CategoryLabelPipe, categoryLabel } from '../../shared/category-label';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmTextareaImports } from '@spartan-ng/helm/textarea';
import { SelectComponent, SelectOption } from '../../shared/select';
import { DateFieldComponent } from '../../shared/date-field';
import { InstrumentSearchComponent } from './instrument-search';
import { APP_ICONS } from '../../shared/icons';

const FLOWS: TransactionFlow[] = ['Everyday', 'Investment', 'Movement'];
const ASSET_KINDS: InvestmentAssetKind[] = ['Stock', 'Etf', 'Crypto', 'Fund', 'Bond', 'Other'];

/** Default allocation bucket (by system key) for each asset type. */
const BUCKET_FOR_KIND: Record<InvestmentAssetKind, string> = {
  Stock: 'stocks-etfs',
  Etf: 'stocks-etfs',
  Fund: 'stocks-etfs',
  Other: 'stocks-etfs',
  Crypto: 'crypto',
  Bond: 'bonds',
};

@Component({
  selector: 'app-quick-add',
  imports: [
    NgIcon,
    DateFieldComponent,
    SelectComponent,
    HlmTextareaImports,
    HlmInputImports,
    HlmButtonImports,
    ModalComponent,
    TranslatePipe,
    CategoryLabelPipe,
    InstrumentSearchComponent,
  ],
  providers: [APP_ICONS],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-modal
      [open]="quick.open()"
      [title]="(quick.editing() ? 'tx.edit' : 'tx.add') | translate"
      width="34rem"
      (closed)="quick.close()"
    >
      <form
        class="space-y-4"
        (submit)="$event.preventDefault(); save(false)"
        (keydown.control.enter)="save(true)"
      >
        <!-- Two levels: everyday money, investments and transfers are different things and reported apart. -->
        <div class="space-y-2">
          <div class="segmented flex h-auto min-h-9 w-full">
            @for (f of flows; track f) {
              <button
                type="button"
                class="min-w-0 flex-1 !whitespace-normal py-1 text-center leading-tight"
                [class.active]="flow() === f"
                (click)="setFlow(f)"
              >
                {{ 'flow.' + f | translate }}
              </button>
            }
          </div>
          <div class="flex flex-wrap gap-2">
            @for (t of flowTypes(); track t) {
              <button
                type="button"
                class="chip"
                [class.chip-active]="type() === t"
                (click)="setType(t)"
              >
                {{ 'type.' + t | translate }}
              </button>
            }
          </div>
        </div>

        @if (isInvestment()) {
          <section class="space-y-3 rounded-xl border border-dashed p-3">
            <div>
              <span class="label">{{ 'asset.kind' | translate }}</span>
              <div class="flex flex-wrap gap-2">
                @for (k of assetKinds; track k) {
                  <button
                    type="button"
                    class="chip"
                    [class.chip-active]="assetKind() === k"
                    (click)="setAssetKind(k)"
                  >
                    {{ 'investmentKind.' + k | translate }}
                  </button>
                }
              </div>
            </div>

            @if (assetKind() === 'Crypto') {
              <p class="text-muted-foreground text-xs">{{ 'asset.cryptoManual' | translate }}</p>
            } @else {
              <app-instrument-search (picked)="pickInstrument($event)" />
            }

            <div class="grid grid-cols-1 gap-3 sm:grid-cols-[8rem_1fr]">
              <div>
                <label class="label" for="qa-symbol">{{ 'asset.symbol' | translate }}</label>
                <input
                  id="qa-symbol"
                  hlmInput
                  class="uppercase"
                  maxlength="32"
                  autocomplete="off"
                  [value]="symbol()"
                  (input)="symbol.set($any($event.target).value); markManual()"
                />
              </div>
              <div>
                <label class="label" for="qa-asset-name">{{ 'asset.name' | translate }}</label>
                <input
                  id="qa-asset-name"
                  hlmInput
                  maxlength="200"
                  autocomplete="off"
                  [value]="assetName()"
                  (input)="assetName.set($any($event.target).value)"
                />
              </div>
            </div>

            <div class="grid grid-cols-2 gap-3">
              <div>
                <label class="label" for="qa-qty">{{ 'asset.quantity' | translate }}</label>
                <input
                  id="qa-qty"
                  hlmInput
                  class="num"
                  inputmode="decimal"
                  autocomplete="off"
                  [value]="quantity()"
                  (input)="quantity.set($any($event.target).value); recalcAmount()"
                />
              </div>
              <div>
                <label class="label" for="qa-price">{{ 'asset.unitPrice' | translate }}</label>
                <input
                  id="qa-price"
                  hlmInput
                  class="num"
                  inputmode="decimal"
                  autocomplete="off"
                  [value]="unitPrice()"
                  (input)="
                    unitPrice.set($any($event.target).value); priceNote.set(''); recalcAmount()
                  "
                />
              </div>
            </div>
            @if (priceNote()) {
              <p class="text-muted-foreground text-xs" aria-live="polite">{{ priceNote() }}</p>
            }
            @if (isin()) {
              <p class="text-muted-foreground text-xs">ISIN {{ isin() }}</p>
            }
          </section>
        }

        <div class="grid grid-cols-[minmax(0,1fr)_auto] gap-3">
          <div>
            <label class="label" for="qa-amount">{{ 'tx.amount' | translate }}</label>
            <input
              #amountInput
              id="qa-amount"
              hlmInput
              class="num h-12 text-2xl font-semibold md:text-2xl"
              inputmode="decimal"
              autocomplete="off"
              placeholder="0,00"
              [value]="amount()"
              (input)="amount.set($any($event.target).value); amountTouched.set(true)"
            />
          </div>
          <div>
            <label class="label" for="qa-currency">{{ 'tx.currency' | translate }}</label>
            <app-select
              inputId="qa-currency"
              class="w-24"
              triggerClass="data-[size=default]:h-12"
              [options]="currencyOptions"
              [value]="currency()"
              (valueChange)="currency.set($event)"
            />
          </div>
        </div>

        @if (currency() !== 'EUR') {
          <div>
            <label class="label" for="qa-fx">{{
              'tx.fxRate' | translate: { currency: currency() }
            }}</label>
            <input
              id="qa-fx"
              hlmInput
              class="num"
              inputmode="decimal"
              [value]="fxRate()"
              (input)="fxRate.set($any($event.target).value)"
            />
          </div>
        }

        @if (type() === 'Expense' || type() === 'Income') {
          <div>
            <span class="label">{{ 'tx.category' | translate }}</span>
            <div class="flex flex-wrap gap-2">
              @for (c of suggested(); track c.id) {
                <button
                  type="button"
                  class="chip"
                  [class.chip-active]="categoryId() === c.id"
                  (click)="pickCategory(c.id)"
                >
                  <span class="h-2 w-2 rounded-full" [style.background]="c.color"></span
                  >{{ c | categoryLabel }}
                </button>
              }
            </div>
            <app-select
              class="mt-2"
              [options]="categorySelectOptions()"
              [value]="categoryId() ?? ''"
              [ariaLabel]="'tx.category' | translate"
              (valueChange)="pickCategory($event)"
            />
          </div>
        }

        @if (type() === 'Expense') {
          <div class="segmented">
            @for (n of natures; track n) {
              <button type="button" [class.active]="nature() === n" (click)="nature.set(n)">
                {{ 'nature.' + n | translate }}
              </button>
            }
          </div>
        }

        @if (type() === 'Savings' || isInvestment()) {
          <div>
            <label class="label" for="qa-bucket">{{ 'tx.bucket' | translate }}</label>
            <app-select
              inputId="qa-bucket"
              [options]="bucketSelectOptions()"
              [value]="bucketId() ?? ''"
              (valueChange)="bucketId.set($event || null)"
            />
          </div>
        }

        <div class="grid grid-cols-1 gap-3 sm:grid-cols-2">
          <div>
            <label class="label" for="qa-account">{{ accountLabel() | translate }}</label>
            <app-select
              inputId="qa-account"
              [options]="accountOptions()"
              [value]="accountId() ?? ''"
              (valueChange)="accountId.set($event)"
            />
          </div>
          <div>
            <label class="label" for="qa-date">{{ 'tx.date' | translate }}</label>
            <app-date-field inputId="qa-date" [value]="date()" (valueChange)="setDate($event)" />
          </div>
        </div>

        @if (isMovement() || isInvestment()) {
          <div>
            <label class="label" for="qa-to">{{
              (isInvestment() ? 'tx.brokerAccount' : 'tx.toAccount') | translate
            }}</label>
            <app-select
              inputId="qa-to"
              [options]="counterAccountOptions()"
              [value]="counterAccountId() ?? ''"
              (valueChange)="counterAccountId.set($event || null)"
            />
          </div>
        }

        @if (type() === 'Savings' && (goals.value() ?? []).length) {
          <div>
            <label class="label" for="qa-goal">{{ 'tx.goal' | translate }}</label>
            <app-select
              inputId="qa-goal"
              [options]="goalOptions()"
              [value]="goalId() ?? ''"
              (valueChange)="goalId.set($event || null)"
            />
          </div>
        }

        <div>
          <label class="label" for="qa-desc">{{ 'tx.description' | translate }}</label>
          <input
            id="qa-desc"
            hlmInput
            maxlength="200"
            [value]="description()"
            (input)="description.set($any($event.target).value)"
          />
        </div>

        <details class="text-sm" [open]="!!notes()">
          <summary class="cursor-pointer text-muted-foreground">
            {{ 'tx.notes' | translate }}
          </summary>
          <textarea
            hlmTextarea
            class="mt-2"
            rows="2"
            maxlength="2000"
            [value]="notes()"
            (input)="notes.set($any($event.target).value)"
          ></textarea>
        </details>

        @if (error()) {
          <p
            class="rounded-xl bg-rose-50 px-3 py-2 text-sm text-rose-700 dark:bg-rose-500/10 dark:text-rose-300"
          >
            {{ error() }}
          </p>
        }

        @if (isInvestment()) {
          <p class="text-muted-foreground text-xs">{{ 'tx.investmentHint' | translate }}</p>
        }

        <div
          class="flex flex-col-reverse gap-2 pt-2 sm:flex-row sm:items-center sm:justify-between"
        >
          <span class="hidden text-xs text-muted-foreground sm:inline">{{
            'tx.shortcuts' | translate
          }}</span>
          <div class="grid auto-cols-fr grid-flow-col gap-2 sm:flex">
            @if (!quick.editing()) {
              <button
                type="button"
                hlmBtn
                variant="outline"
                class="whitespace-nowrap"
                [disabled]="saving()"
                (click)="save(true)"
              >
                <ng-icon name="lucidePlus" />{{ 'tx.saveAndNew' | translate }}
              </button>
            }
            <button type="submit" hlmBtn [disabled]="saving()">
              <ng-icon
                [name]="saving() ? 'lucideLoaderCircle' : 'lucideSave'"
                [class]="saving() ? 'motion-safe:animate-spin' : ''"
              />{{ 'common.save' | translate }}
            </button>
          </div>
        </div>
      </form>
    </app-modal>
  `,
})
export class QuickAddComponent {
  protected readonly quick = inject(QuickAdd);
  private readonly api = inject(Api);
  private readonly events = inject(DataEvents);
  private readonly prefs = inject(Prefs);
  private readonly toasts = inject(Toasts);
  private readonly i18n = inject(TranslateService);
  private readonly amountInput = viewChild<ElementRef<HTMLInputElement>>('amountInput');

  protected readonly flows = FLOWS;
  protected readonly assetKinds = ASSET_KINDS;
  protected readonly natures: ExpenseNature[] = ['Variable', 'Fixed'];
  protected readonly currencies = [
    'EUR',
    'USD',
    'GBP',
    'CHF',
    'JPY',
    'CAD',
    'AUD',
    'SEK',
    'NOK',
    'DKK',
    'PLN',
    'BRL',
  ];
  protected readonly currencyOptions: SelectOption[] = this.currencies.map((c) => ({
    value: c,
    label: c,
  }));

  protected readonly type = signal<TransactionType>('Expense');
  protected readonly amount = signal('');
  protected readonly currency = signal('EUR');
  protected readonly fxRate = signal('');
  protected readonly accountId = signal<string | null>(null);
  protected readonly counterAccountId = signal<string | null>(null);
  protected readonly categoryId = signal<string | null>(null);
  protected readonly nature = signal<ExpenseNature>('Variable');
  protected readonly bucketId = signal<string | null>(null);
  protected readonly goalId = signal<string | null>(null);
  protected readonly date = signal(today());
  protected readonly description = signal('');
  protected readonly notes = signal('');
  protected readonly saving = signal(false);
  protected readonly error = signal('');

  // Investment entries: what was bought or sold.
  protected readonly assetKind = signal<InvestmentAssetKind>('Etf');
  protected readonly symbol = signal('');
  protected readonly assetName = signal('');
  protected readonly isin = signal<string | null>(null);
  protected readonly quantity = signal('');
  protected readonly unitPrice = signal('');
  protected readonly priceSource = signal<AssetPriceSource>('Manual');
  protected readonly priceNote = signal('');
  /** Once the user types an amount, quantity × price no longer overwrites it. */
  protected readonly amountTouched = signal(false);
  /** The instrument picked from the search, kept to refresh its price when the date changes. */
  private readonly picked = signal<InstrumentMatch | null>(null);
  private quoteRequest = 0;

  private readonly active = computed(() => this.quick.open());
  protected readonly accounts = liveResource({
    params: () => this.active() || undefined,
    stream: () => this.api.accounts(),
  });
  protected readonly categories = liveResource({
    params: () => this.active() || undefined,
    stream: () => this.api.categories(),
  });
  protected readonly buckets = liveResource({
    params: () => this.active() || undefined,
    stream: () => this.api.buckets(),
  });
  protected readonly goals = liveResource({
    params: () => this.active() || undefined,
    stream: () => this.api.goals(),
  });
  protected readonly defaults = liveResource({
    params: () => (this.active() && !this.quick.editing()) || undefined,
    stream: () => this.api.quickAddDefaults(),
  });

  protected readonly manualAccounts = computed(() =>
    (this.accounts.value() ?? []).filter((a) => a.isManual && !a.archived),
  );
  protected readonly flow = computed(() => flowOf(this.type()));
  protected readonly flowTypes = computed(() => FLOW_TYPES[this.flow()]);
  protected readonly isMovement = computed(() => this.flow() === 'Movement');
  protected readonly isInvestment = computed(() => this.flow() === 'Investment');
  protected readonly accountLabel = computed(() =>
    this.isInvestment() ? 'tx.cashAccount' : this.isMovement() ? 'tx.fromAccount' : 'tx.account',
  );
  protected readonly categoryOptions = computed(() => {
    const wanted = this.type() === 'Income' ? 'Income' : 'Expense';
    return (this.categories.value() ?? []).filter((c) => c.type === wanted && !c.archived);
  });
  protected readonly bucketOptions = computed(() => {
    const group = this.type() === 'Savings' ? 'Savings' : 'Investment';
    return (this.buckets.value() ?? []).filter((b) => b.group === group && !b.archived);
  });
  protected readonly categorySelectOptions = computed<SelectOption[]>(() => {
    this.prefs.translations();
    return [
      { value: '', label: this.i18n.instant('tx.allCategories') },
      ...this.categoryOptions().map((c) => ({
        value: c.id,
        label: (c.parentId ? '— ' : '') + categoryLabel(this.i18n, c),
      })),
    ];
  });
  protected readonly bucketSelectOptions = computed<SelectOption[]>(() => [
    { value: '', label: '—' },
    ...this.bucketOptions().map((b) => ({ value: b.id, label: b.name })),
  ]);
  protected readonly accountOptions = computed<SelectOption[]>(() =>
    this.manualAccounts().map((a) => ({ value: a.id, label: a.name })),
  );
  protected readonly counterAccountOptions = computed<SelectOption[]>(() => {
    this.prefs.translations();
    const none = this.type() === 'Transfer' ? '—' : this.i18n.instant('tx.external');
    return [
      { value: '', label: none },
      ...(this.accounts.value() ?? [])
        .filter((a) => a.id !== this.accountId() && !a.archived)
        .map((a) => ({ value: a.id, label: a.name })),
    ];
  });
  protected readonly goalOptions = computed<SelectOption[]>(() => [
    { value: '', label: '—' },
    ...(this.goals.value() ?? []).map((g) => ({ value: g.id, label: g.name })),
  ]);
  protected readonly suggested = computed(() => {
    const d = this.defaults.value();
    const recent =
      this.type() === 'Income'
        ? (d?.recentIncomeCategories ?? [])
        : (d?.recentExpenseCategories ?? []);
    const options = this.categoryOptions();
    const byId = new Map(options.map((c) => [c.id, c]));
    const picks = recent.map((id) => byId.get(id)).filter((c) => !!c);
    // First-time use: offer the most common top-level categories.
    const fallback = options.filter((c) => !c.parentId && !picks.includes(c));
    return [...picks, ...fallback].slice(0, 8);
  });

  constructor() {
    effect(() => {
      if (!this.quick.open()) return;
      const t = this.quick.editing();
      this.error.set('');
      if (t) {
        this.type.set(t.type);
        this.amount.set(String(t.amount).replace('.', ','));
        this.currency.set(t.currency);
        this.fxRate.set(t.currency === 'EUR' ? '' : String(t.fxRate));
        this.accountId.set(t.accountId);
        this.counterAccountId.set(t.counterAccountId);
        this.categoryId.set(t.categoryId);
        this.nature.set(t.nature ?? 'Variable');
        this.bucketId.set(t.bucketId);
        this.goalId.set(t.goalId);
        this.date.set(t.occurredOn);
        this.description.set(t.description ?? '');
        this.notes.set(t.notes ?? '');
        this.resetAsset();
        this.amountTouched.set(true);
        if (t.asset) {
          this.assetKind.set(t.asset.kind);
          this.symbol.set(t.asset.symbol);
          this.assetName.set(t.asset.name ?? '');
          this.isin.set(t.asset.isin);
          this.quantity.set(formatInput(t.asset.quantity));
          this.unitPrice.set(formatInput(t.asset.unitPrice));
          this.priceSource.set(t.asset.priceSource ?? 'Manual');
        }
      } else {
        this.reset(true);
      }
      setTimeout(() => this.amountInput()?.nativeElement.focus(), 50);
    });

    // Default account: last used on this device, else the server's most recent, else the first manual one.
    effect(() => {
      if (this.quick.editing() || this.accountId()) return;
      const manual = this.manualAccounts();
      const preferred = [
        this.prefs.lastAccountId(),
        this.defaults.value()?.accountId,
        manual[0]?.id,
      ];
      const id = preferred.find((p) => p && manual.some((a) => a.id === p));
      if (id) this.accountId.set(id);
    });

    // Investment entries need a bucket; pick the one matching the asset type once buckets have loaded.
    effect(() => {
      if (this.isInvestment() && !this.bucketId() && this.bucketOptions().length)
        this.defaultBucket();
    });
  }

  protected setFlow(f: TransactionFlow) {
    if (this.flow() !== f) this.setType(FLOW_TYPES[f][0]);
  }

  protected setType(t: TransactionType) {
    const wasInvestment = this.isInvestment();
    this.type.set(t);
    this.categoryId.set(null);
    this.counterAccountId.set(null);
    // Buy ↔ sell keeps the instrument and bucket; anything else starts clean.
    if (!(wasInvestment && this.isInvestment())) {
      this.bucketId.set(null);
      this.resetAsset();
    }
    if (this.isInvestment()) this.defaultBucket();
  }

  protected setAssetKind(kind: InvestmentAssetKind) {
    const wasCrypto = this.assetKind() === 'Crypto';
    this.assetKind.set(kind);
    // Switching to/from crypto: provider-filled details no longer apply (crypto is always manual).
    if (wasCrypto !== (kind === 'Crypto')) this.clearInstrument();
    this.bucketId.set(null);
    this.defaultBucket();
  }

  /** Autofill from a search suggestion: identity, currency and — when the provider has one — the price that day. */
  protected pickInstrument(m: InstrumentMatch) {
    this.picked.set(m);
    this.symbol.set(m.symbol);
    this.assetName.set(m.name);
    this.isin.set(m.isin);
    this.priceSource.set(m.provider);
    const kind = KIND_FOR_CLASS[m.assetClass];
    if (kind && kind !== this.assetKind()) {
      this.assetKind.set(kind);
      this.bucketId.set(null);
      this.defaultBucket();
    }
    if (this.currencies.includes(m.currency)) this.currency.set(m.currency);
    if (!this.description()) this.description.set(m.name);
    void this.fetchQuote();
  }

  /** Typing the symbol by hand means the details no longer come from a provider. */
  protected markManual() {
    if (this.picked()) {
      this.picked.set(null);
      this.isin.set(null);
    }
    this.priceSource.set('Manual');
    this.priceNote.set('');
  }

  protected setDate(value: string) {
    this.date.set(value);
    if (this.picked()) void this.fetchQuote();
  }

  protected recalcAmount() {
    if (this.amountTouched()) return;
    const qty = parseDecimal(this.quantity());
    const price = parseDecimal(this.unitPrice());
    if (qty && price !== null) {
      this.amount.set(formatInput(Math.round(qty * price * 100) / 100));
    }
  }

  private async fetchQuote() {
    const m = this.picked();
    if (!m || this.assetKind() === 'Crypto') return;
    const request = ++this.quoteRequest;
    this.priceNote.set(this.i18n.instant('asset.fetchingPrice'));
    try {
      const result = await firstValueFrom(
        this.api.instrumentQuote(m.provider, m.brokerSymbol, this.date(), m.isin),
      );
      if (request !== this.quoteRequest) return; // a newer pick or date change won
      const q = result.quote;
      if (!q) {
        this.priceNote.set(result.message ?? this.i18n.instant('asset.priceMissing'));
        return;
      }
      this.unitPrice.set(formatInput(q.price));
      if (this.currencies.includes(q.currency)) this.currency.set(q.currency);
      this.priceNote.set(
        this.i18n.instant('asset.priceFrom', {
          provider: this.i18n.instant('asset.provider.' + q.provider),
          basis: this.i18n.instant('asset.basis.' + q.basis),
          date: q.date,
        }),
      );
      this.recalcAmount();
    } catch {
      if (request === this.quoteRequest)
        this.priceNote.set(this.i18n.instant('asset.priceMissing'));
    }
  }

  private defaultBucket() {
    if (this.bucketId()) return;
    const key = BUCKET_FOR_KIND[this.assetKind()];
    const bucket = this.bucketOptions().find((b) => b.key === key) ?? this.bucketOptions()[0];
    if (bucket) this.bucketId.set(bucket.id);
  }

  private clearInstrument() {
    this.picked.set(null);
    this.quoteRequest++;
    this.symbol.set('');
    this.assetName.set('');
    this.isin.set(null);
    this.unitPrice.set('');
    this.priceSource.set('Manual');
    this.priceNote.set('');
  }

  private resetAsset() {
    this.clearInstrument();
    this.quantity.set('');
    this.amountTouched.set(false);
  }

  protected pickCategory(id: string) {
    this.categoryId.set(id || null);
    const category = this.categoryOptions().find((c) => c.id === id);
    if (category?.defaultNature) this.nature.set(category.defaultNature);
  }

  protected async save(addAnother: boolean) {
    const amount = parseAmount(this.amount());
    if (!amount || amount <= 0) {
      this.error.set(this.i18n.instant('tx.errors.amount'));
      return;
    }
    if (!this.accountId()) {
      this.error.set(this.i18n.instant('tx.errors.account'));
      return;
    }
    const asset = this.isInvestment() ? this.assetBody() : null;
    if (asset === undefined) {
      this.error.set(this.i18n.instant('tx.errors.symbol'));
      return;
    }

    const body: TransactionRequest = {
      type: this.type(),
      occurredOn: this.date(),
      amount,
      currency: this.currency(),
      fxRate: this.currency() === 'EUR' ? null : parseAmount(this.fxRate()),
      accountId: this.accountId()!,
      categoryId: this.categoryId(),
      nature: this.type() === 'Expense' ? this.nature() : null,
      counterAccountId: this.counterAccountId(),
      bucketId: this.bucketId(),
      goalId: this.type() === 'Savings' ? this.goalId() : null,
      description: this.description() || null,
      notes: this.notes() || null,
      asset,
    };

    this.saving.set(true);
    this.error.set('');
    try {
      const editing = this.quick.editing();
      if (editing) await firstValueFrom(this.api.updateTransaction(editing.id, body));
      else await firstValueFrom(this.api.createTransaction(body));
      this.prefs.lastAccountId.set(body.accountId);
      this.events.bump();
      this.toasts.show(this.i18n.instant(editing ? 'tx.updated' : 'tx.saved'));
      if (addAnother && !editing) {
        this.reset(false);
        this.amountInput()?.nativeElement.focus();
      } else {
        this.quick.close();
      }
    } catch (err) {
      const e = err as { error?: { detail?: string; errors?: Record<string, string[]> } };
      this.error.set(
        Object.values(e.error?.errors ?? {})[0]?.[0] ??
          e.error?.detail ??
          this.i18n.instant('common.error'),
      );
    } finally {
      this.saving.set(false);
    }
  }

  /** null = no instrument details given (allowed); undefined = details given without a symbol (invalid). */
  private assetBody(): InvestmentAsset | null | undefined {
    const symbol = this.symbol().trim();
    const quantity = parseDecimal(this.quantity());
    const unitPrice = parseDecimal(this.unitPrice());
    const name = this.assetName().trim();
    if (!symbol) return quantity !== null || unitPrice !== null || name ? undefined : null;
    const kind = this.assetKind();
    return {
      kind,
      symbol,
      name: name || null,
      isin: kind === 'Crypto' ? null : this.isin(),
      quantity,
      unitPrice,
      // Crypto never has a provider; the server enforces this too.
      priceSource: kind === 'Crypto' ? 'Manual' : this.priceSource(),
    };
  }

  /** Keeps type, account, currency and date between entries so a batch of receipts is fast to type in. */
  private reset(full: boolean) {
    this.resetAsset();
    this.amount.set('');
    this.description.set('');
    this.notes.set('');
    this.categoryId.set(null);
    this.goalId.set(null);
    this.fxRate.set('');
    if (full) {
      this.type.set('Expense');
      this.currency.set('EUR');
      this.date.set(today());
      this.bucketId.set(null);
      this.counterAccountId.set(null);
      this.nature.set('Variable');
    }
  }
}

/** Accepts "1.234,56", "1234.56", "45,9" and "€ 45.90". */
export function parseAmount(text: string): number | null {
  const value = parseDecimal(text);
  return value === null ? null : Math.round(value * 10000) / 10000;
}

/** Same formats as {@link parseAmount}, without rounding (crypto quantities need many decimals). */
export function parseDecimal(text: string): number | null {
  const cleaned = text.replace(/[€$£\s]/g, '');
  if (!cleaned) return null;
  const lastComma = cleaned.lastIndexOf(',');
  const lastDot = cleaned.lastIndexOf('.');
  const decimalSep = lastComma > lastDot ? ',' : '.';
  const thousandSep = decimalSep === ',' ? '.' : ',';
  const normalised = cleaned.split(thousandSep).join('').replace(decimalSep, '.');
  const value = Number(normalised);
  return Number.isFinite(value) ? Math.round(value * 1e10) / 1e10 : null;
}

/** Number → input text with a decimal comma, as the fields accept it. */
function formatInput(value: number | null): string {
  return value === null ? '' : String(value).replace('.', ',');
}

const KIND_FOR_CLASS: Partial<Record<AssetClass, InvestmentAssetKind>> = {
  Stock: 'Stock',
  Etf: 'Etf',
  Fund: 'Fund',
  Bond: 'Bond',
};
