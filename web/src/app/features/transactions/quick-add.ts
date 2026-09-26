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
import { rxResource } from '@angular/core/rxjs-interop';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { firstValueFrom, of } from 'rxjs';
import { Api } from '../../core/api';
import { DataEvents, QuickAdd } from '../../core/data-events';
import { today } from '../../core/format';
import { ExpenseNature, TransactionRequest, TransactionType } from '../../core/models';
import { Prefs } from '../../core/prefs';
import { Toasts } from '../../core/toast';
import { ModalComponent } from '../../shared/modal';
import { CategoryLabelPipe } from '../../shared/category-label';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmTextareaImports } from '@spartan-ng/helm/textarea';
import { UiSelect } from '../../shared/select';
import { DateFieldComponent } from '../../shared/date-field';

const TYPES: TransactionType[] = [
  'Expense',
  'Income',
  'Transfer',
  'Savings',
  'InvestmentContribution',
];

@Component({
  selector: 'app-quick-add',
  imports: [
    DateFieldComponent,
    UiSelect,
    HlmTextareaImports,
    HlmInputImports,
    HlmButtonImports,
    ModalComponent,
    TranslatePipe,
    CategoryLabelPipe,
  ],
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
        <div class="segmented flex w-full flex-wrap">
          @for (t of types; track t) {
            <button type="button" class="flex-1" [class.active]="type() === t" (click)="setType(t)">
              {{ 'type.' + t | translate }}
            </button>
          }
        </div>

        <div class="grid grid-cols-[1fr_auto] gap-3">
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
              (input)="amount.set($any($event.target).value)"
            />
          </div>
          <div>
            <label class="label" for="qa-currency">{{ 'tx.currency' | translate }}</label>
            <select
              id="qa-currency"
              uiSelect
              class="h-12 w-24"
              [value]="currency()"
              (change)="currency.set($any($event.target).value)"
            >
              @for (c of currencies; track c) {
                <option [value]="c">{{ c }}</option>
              }
            </select>
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
            <select
              uiSelect
              class="mt-2"
              [value]="categoryId() ?? ''"
              (change)="pickCategory($any($event.target).value)"
            >
              <option value="">{{ 'tx.allCategories' | translate }}</option>
              @for (c of categoryOptions(); track c.id) {
                <option [value]="c.id">{{ c.parentId ? '— ' : '' }}{{ c | categoryLabel }}</option>
              }
            </select>
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

        @if (type() === 'Savings' || type() === 'InvestmentContribution') {
          <div>
            <label class="label" for="qa-bucket">{{ 'tx.bucket' | translate }}</label>
            <select
              id="qa-bucket"
              uiSelect
              [value]="bucketId() ?? ''"
              (change)="bucketId.set($any($event.target).value || null)"
            >
              <option value="">—</option>
              @for (b of bucketOptions(); track b.id) {
                <option [value]="b.id">{{ b.name }}</option>
              }
            </select>
          </div>
        }

        <div class="grid grid-cols-2 gap-3">
          <div>
            <label class="label" for="qa-account">{{
              (isMovement() ? 'tx.fromAccount' : 'tx.account') | translate
            }}</label>
            <select
              id="qa-account"
              uiSelect
              [value]="accountId() ?? ''"
              (change)="accountId.set($any($event.target).value)"
            >
              @for (a of manualAccounts(); track a.id) {
                <option [value]="a.id">{{ a.name }}</option>
              }
            </select>
          </div>
          <div>
            <label class="label" for="qa-date">{{ 'tx.date' | translate }}</label>
            <app-date-field inputId="qa-date" [value]="date()" (valueChange)="date.set($event)" />
          </div>
        </div>

        @if (isMovement()) {
          <div>
            <label class="label" for="qa-to">{{ 'tx.toAccount' | translate }}</label>
            <select
              id="qa-to"
              uiSelect
              [value]="counterAccountId() ?? ''"
              (change)="counterAccountId.set($any($event.target).value || null)"
            >
              <option value="">
                {{ type() === 'Transfer' ? '—' : ('tx.external' | translate) }}
              </option>
              @for (a of accounts.value() ?? []; track a.id) {
                @if (a.id !== accountId() && !a.archived) {
                  <option [value]="a.id">{{ a.name }}</option>
                }
              }
            </select>
          </div>
        }

        @if (type() === 'Savings' && (goals.value() ?? []).length) {
          <div>
            <label class="label" for="qa-goal">{{ 'tx.goal' | translate }}</label>
            <select
              id="qa-goal"
              uiSelect
              [value]="goalId() ?? ''"
              (change)="goalId.set($any($event.target).value || null)"
            >
              <option value="">—</option>
              @for (g of goals.value() ?? []; track g.id) {
                <option [value]="g.id">{{ g.name }}</option>
              }
            </select>
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

        <div class="flex items-center justify-between gap-2 pt-2">
          <span class="hidden text-xs text-muted-foreground sm:inline">{{
            'tx.shortcuts' | translate
          }}</span>
          <div class="flex gap-2">
            @if (!quick.editing()) {
              <button
                type="button"
                hlmBtn
                variant="outline"
                class="whitespace-nowrap"
                [disabled]="saving()"
                (click)="save(true)"
              >
                {{ 'tx.saveAndNew' | translate }}
              </button>
            }
            <button type="submit" hlmBtn [disabled]="saving()">
              {{ 'common.save' | translate }}
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

  protected readonly types = TYPES;
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

  private readonly active = computed(() => this.quick.open());
  protected readonly accounts = rxResource({
    params: () => this.active() || undefined,
    stream: () => this.api.accounts(),
  });
  protected readonly categories = rxResource({
    params: () => this.active() || undefined,
    stream: () => this.api.categories(),
  });
  protected readonly buckets = rxResource({
    params: () => this.active() || undefined,
    stream: () => this.api.buckets(),
  });
  protected readonly goals = rxResource({
    params: () => this.active() || undefined,
    stream: () => this.api.goals(),
  });
  protected readonly defaults = rxResource({
    params: () => (this.active() && !this.quick.editing()) || undefined,
    stream: () => this.api.quickAddDefaults(),
  });

  protected readonly manualAccounts = computed(() =>
    (this.accounts.value() ?? []).filter((a) => a.isManual && !a.archived),
  );
  protected readonly isMovement = computed(() =>
    ['Transfer', 'Savings', 'InvestmentContribution'].includes(this.type()),
  );
  protected readonly categoryOptions = computed(() => {
    const wanted = this.type() === 'Income' ? 'Income' : 'Expense';
    return (this.categories.value() ?? []).filter((c) => c.type === wanted && !c.archived);
  });
  protected readonly bucketOptions = computed(() => {
    const group = this.type() === 'Savings' ? 'Savings' : 'Investment';
    return (this.buckets.value() ?? []).filter((b) => b.group === group && !b.archived);
  });
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
  }

  protected setType(t: TransactionType) {
    this.type.set(t);
    this.categoryId.set(null);
    this.bucketId.set(null);
    this.counterAccountId.set(null);
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

  /** Keeps type, account, currency and date between entries so a batch of receipts is fast to type in. */
  private reset(full: boolean) {
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
  const cleaned = text.replace(/[€\s]/g, '');
  if (!cleaned) return null;
  const lastComma = cleaned.lastIndexOf(',');
  const lastDot = cleaned.lastIndexOf('.');
  const decimalSep = lastComma > lastDot ? ',' : '.';
  const thousandSep = decimalSep === ',' ? '.' : ',';
  const normalised = cleaned.split(thousandSep).join('').replace(decimalSep, '.');
  const value = Number(normalised);
  return Number.isFinite(value) ? Math.round(value * 10000) / 10000 : null;
}
