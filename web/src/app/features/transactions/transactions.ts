import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { rxResource, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { Api, TransactionFilter } from '../../core/api';
import { DataEvents, QuickAdd } from '../../core/data-events';
import { DayPipe, MoneyPipe } from '../../core/format';
import {
  AuditEntry,
  FLOW_TYPES,
  Transaction,
  TransactionFlow,
  TransactionType,
} from '../../core/models';
import { Toasts } from '../../core/toast';
import { CategoryLabelPipe } from '../../shared/category-label';
import { ModalComponent } from '../../shared/modal';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { UiSelect } from '../../shared/select';
import { DateFieldComponent } from '../../shared/date-field';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideChevronLeft,
  lucideChevronRight,
  lucideHistory,
  lucidePencil,
  lucideTrash2,
  lucideX,
} from '@ng-icons/lucide';

const TYPE_TONE: Record<TransactionType, string> = {
  Expense: 'text-rose-600 dark:text-rose-400',
  Income: 'text-emerald-600 dark:text-emerald-400',
  Transfer: 'text-muted-foreground',
  Savings: 'text-cyan-600 dark:text-cyan-400',
  InvestmentContribution: 'text-violet-600 dark:text-violet-400',
  InvestmentSale: 'text-violet-600 dark:text-violet-400',
};

const SIGN: Partial<Record<TransactionType, string>> = {
  Expense: '−',
  Income: '+',
  InvestmentContribution: '−',
  InvestmentSale: '+',
};

@Component({
  selector: 'app-transactions',
  imports: [
    NgIcon,
    DateFieldComponent,
    UiSelect,
    HlmTableImports,
    HlmInputImports,
    HlmButtonImports,
    TranslatePipe,
    MoneyPipe,
    DayPipe,
    CategoryLabelPipe,
    ModalComponent,
  ],
  providers: [
    provideIcons({
      lucideChevronLeft,
      lucideChevronRight,
      lucideHistory,
      lucidePencil,
      lucideTrash2,
      lucideX,
    }),
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mb-4 flex flex-wrap items-center justify-between gap-3">
      <h1 class="text-2xl font-semibold tracking-tight">{{ 'nav.transactions' | translate }}</h1>
      <span class="text-sm text-muted-foreground">{{
        'tx.count' | translate: { count: page.value()?.total ?? 0 }
      }}</span>
    </div>

    <!-- Everyday money and investments are listed separately; "All" is the explicit combined view. -->
    <div class="-mx-4 mb-4 overflow-x-auto px-4 sm:mx-0 sm:px-0">
      <div class="segmented" role="tablist">
        @for (f of flowTabs; track f) {
          <button
            type="button"
            role="tab"
            [attr.aria-selected]="flow() === f"
            [class.active]="flow() === f"
            (click)="setFlow(f)"
          >
            {{ 'flow.' + (f || 'all') | translate }}
          </button>
        }
      </div>
    </div>

    <section class="card mb-4 grid grid-cols-1 gap-3 !p-4 sm:grid-cols-2 lg:grid-cols-6">
      <input
        hlmInput
        class="sm:col-span-2"
        type="search"
        [placeholder]="'common.search' | translate"
        [value]="search()"
        (input)="search.set($any($event.target).value); pageNo.set(1)"
      />
      <app-date-field
        [value]="from()"
        [placeholder]="'common.from' | translate"
        (valueChange)="from.set($event); pageNo.set(1)"
        [ariaLabel]="'common.from' | translate"
        clearable
      />
      <app-date-field
        [value]="to()"
        [placeholder]="'common.to' | translate"
        (valueChange)="to.set($event); pageNo.set(1)"
        [ariaLabel]="'common.to' | translate"
        clearable
      />
      <select
        uiSelect
        [value]="accountId()"
        (change)="accountId.set($any($event.target).value); pageNo.set(1)"
      >
        <option value="">{{ 'tx.allAccounts' | translate }}</option>
        @for (a of accounts.value() ?? []; track a.id) {
          <option [value]="a.id">{{ a.name }}</option>
        }
      </select>
      <select
        uiSelect
        [value]="categoryId()"
        (change)="categoryId.set($any($event.target).value); pageNo.set(1)"
      >
        <option value="">{{ 'tx.allCategories' | translate }}</option>
        @for (c of categories.value() ?? []; track c.id) {
          <option [value]="c.id">{{ c.parentId ? '— ' : '' }}{{ c | categoryLabel }}</option>
        }
      </select>
      <div class="flex flex-wrap gap-2 sm:col-span-2 lg:col-span-6">
        @for (t of visibleTypes(); track t) {
          <button
            class="chip"
            [class.chip-active]="typeFilter().includes(t)"
            (click)="toggleType(t)"
          >
            {{ 'type.' + t | translate }}
          </button>
        }
        @if (hasFilters()) {
          <button class="chip" (click)="clear()">
            <ng-icon name="lucideX" />{{ 'common.clear' | translate }}
          </button>
        }
      </div>
    </section>

    <section class="card overflow-hidden !p-0">
      <!-- Phones: one card per row instead of a wide table. -->
      <ul class="divide-y md:hidden">
        @for (t of page.value()?.items ?? []; track t.id) {
          <li class="flex items-start gap-3 px-4 py-3">
            <div class="min-w-0 flex-1">
              <div class="truncate font-medium">{{ title(t) }}</div>
              <div class="text-muted-foreground truncate text-xs">
                {{ t.occurredOn | day: 'short' }} · {{ 'type.' + t.type | translate }} ·
                {{ t.categoryKey ? (t | categoryLabel) : (t.bucketName ?? t.accountName) }}
              </div>
              @if (t.asset; as a) {
                <div class="text-muted-foreground num truncate text-xs">
                  {{ a.symbol }}
                  @if (a.quantity !== null) {
                    · {{ a.quantity }} ×
                    {{ a.unitPrice !== null ? (a.unitPrice | money: t.currency) : '—' }}
                  }
                </div>
              }
            </div>
            <div class="flex shrink-0 flex-col items-end gap-1">
              <span class="num font-semibold whitespace-nowrap" [class]="tone(t.type)">
                {{ sign(t.type) }}{{ t.amount | money: t.currency }}
              </span>
              <div class="flex">
                <button
                  hlmBtn
                  variant="ghost"
                  size="icon-sm"
                  (click)="showHistory(t)"
                  [attr.aria-label]="'tx.history' | translate"
                >
                  <ng-icon name="lucideHistory" />
                </button>
                @if (t.editable) {
                  <button
                    hlmBtn
                    variant="ghost"
                    size="icon-sm"
                    (click)="quick.edit(t)"
                    [attr.aria-label]="'common.edit' | translate"
                  >
                    <ng-icon name="lucidePencil" />
                  </button>
                  <button
                    hlmBtn
                    variant="ghost"
                    size="icon-sm"
                    class="text-destructive hover:text-destructive"
                    (click)="remove(t)"
                    [attr.aria-label]="'common.delete' | translate"
                  >
                    <ng-icon name="lucideTrash2" />
                  </button>
                }
              </div>
            </div>
          </li>
        } @empty {
          <li class="px-4 py-12 text-center text-muted-foreground">{{ 'tx.empty' | translate }}</li>
        }
      </ul>

      <div class="hidden overflow-x-auto md:block">
      <table hlmTable>
        <thead hlmTHead>
          <tr hlmTr>
            <th hlmTh>{{ 'tx.date' | translate }}</th>
            <th hlmTh>{{ 'tx.description' | translate }}</th>
            <th hlmTh>{{ 'tx.category' | translate }}</th>
            <th hlmTh>{{ 'tx.account' | translate }}</th>
            <th hlmTh class="text-right">{{ 'tx.amount' | translate }}</th>
            <th hlmTh></th>
          </tr>
        </thead>
        <tbody hlmTBody>
          @for (t of page.value()?.items ?? []; track t.id) {
            <tr hlmTr class="group hover:bg-muted/50">
              <td hlmTd class="text-muted-foreground whitespace-nowrap">
                {{ t.occurredOn | day: 'short' }}
              </td>
              <td hlmTd class="max-w-72 whitespace-normal">
                <!-- Descriptions are user/imported data; rendered as text only, never as HTML. -->
                <div class="truncate font-medium">{{ title(t) }}</div>
                <div class="text-xs text-muted-foreground">
                  {{ 'type.' + t.type | translate }}
                  @if (t.asset; as a) {
                    · <span class="num">{{ a.symbol }}</span>
                    @if (a.quantity !== null) {
                      <span class="num">
                        · {{ a.quantity }} ×
                        {{ a.unitPrice !== null ? (a.unitPrice | money: t.currency) : '—' }}</span
                      >
                    }
                  }
                  @if (t.nature) {
                    · {{ 'nature.' + t.nature | translate }}
                  }
                  @if (t.source !== 'Manual') {
                    ·
                    <span class="badge bg-muted !px-1.5 !py-0">{{
                      'source.' + t.source | translate
                    }}</span>
                  }
                </div>
              </td>
              <td hlmTd>{{ t.categoryKey ? (t | categoryLabel) : (t.bucketName ?? '—') }}</td>
              <td hlmTd class="text-muted-foreground">
                {{ t.accountName }}
                @if (t.counterAccountName) {
                  → {{ t.counterAccountName }}
                }
              </td>
              <td
                hlmTd
                class="num text-right font-semibold whitespace-nowrap"
                [class]="tone(t.type)"
              >
                {{ sign(t.type) }}{{ t.amount | money: t.currency }}
                @if (t.currency !== 'EUR') {
                  <div class="text-xs font-normal text-muted-foreground">
                    {{ t.baseAmount | money }}
                  </div>
                }
              </td>
              <td hlmTd class="text-right whitespace-nowrap">
                <button
                  hlmBtn
                  variant="ghost"
                  size="icon-sm"
                  (click)="showHistory(t)"
                  [attr.aria-label]="'tx.history' | translate"
                >
                  <ng-icon name="lucideHistory" />
                </button>
                @if (t.editable) {
                  <button
                    hlmBtn
                    variant="ghost"
                    size="icon-sm"
                    (click)="quick.edit(t)"
                    [attr.aria-label]="'common.edit' | translate"
                  >
                    <ng-icon name="lucidePencil" />
                  </button>
                  <button
                    hlmBtn
                    variant="ghost"
                    size="icon-sm"
                    class="text-destructive hover:text-destructive"
                    (click)="remove(t)"
                    [attr.aria-label]="'common.delete' | translate"
                  >
                    <ng-icon name="lucideTrash2" />
                  </button>
                }
              </td>
            </tr>
          } @empty {
            <tr hlmTr>
              <td hlmTd colspan="6" class="py-12 text-center text-muted-foreground">
                {{ 'tx.empty' | translate }}
              </td>
            </tr>
          }
        </tbody>
      </table>
      </div>
      @if ((page.value()?.total ?? 0) > pageSize) {
        <div class="flex items-center justify-end gap-2 p-3">
          <button
            hlmBtn
            variant="outline"
            size="icon"
            [disabled]="pageNo() === 1"
            (click)="pageNo.set(pageNo() - 1)"
            aria-label="Previous page"
          >
            <ng-icon name="lucideChevronLeft" />
          </button>
          <span class="num text-sm">{{ pageNo() }} / {{ pages() }}</span>
          <button
            hlmBtn
            variant="outline"
            size="icon"
            [disabled]="pageNo() >= pages()"
            (click)="pageNo.set(pageNo() + 1)"
            aria-label="Next page"
          >
            <ng-icon name="lucideChevronRight" />
          </button>
        </div>
      }
    </section>

    <app-modal [open]="!!history()" [title]="'tx.history' | translate" (closed)="history.set(null)">
      <ol class="space-y-3">
        @for (h of history() ?? []; track $index) {
          <li class="rounded-xl border border-border p-3 text-sm">
            <div class="flex justify-between">
              <span class="font-medium">{{ 'audit.' + h.action | translate }}</span>
              <span class="text-xs text-muted-foreground">{{ h.atUtc | day }} · {{ h.actor }}</span>
            </div>
            <ul class="mt-2 space-y-0.5 font-mono text-xs text-muted-foreground">
              @for (c of changes(h); track c.field) {
                <li>
                  {{ c.field }}:
                  @if (c.from !== undefined) {
                    <s>{{ c.from }}</s> →
                  }
                  {{ c.to }}
                </li>
              }
            </ul>
          </li>
        }
      </ol>
    </app-modal>
  `,
})
export class TransactionsComponent {
  private readonly api = inject(Api);
  private readonly events = inject(DataEvents);
  private readonly toasts = inject(Toasts);
  private readonly i18n = inject(TranslateService);
  protected readonly quick = inject(QuickAdd);
  private readonly query = toSignal(inject(ActivatedRoute).queryParamMap);

  /** '' is the explicit combined view. */
  protected readonly flowTabs: (TransactionFlow | '')[] = ['Everyday', 'Investment', 'Movement', ''];
  protected readonly flow = signal<TransactionFlow | ''>(
    (this.query()?.get('flow') as TransactionFlow | null) ?? 'Everyday',
  );
  protected readonly visibleTypes = computed<TransactionType[]>(() => {
    const f = this.flow();
    return f ? FLOW_TYPES[f] : Object.values(FLOW_TYPES).flat();
  });
  protected readonly pageSize = 50;
  protected readonly search = signal('');
  protected readonly from = signal(this.query()?.get('from') ?? '');
  protected readonly to = signal(this.query()?.get('to') ?? '');
  protected readonly accountId = signal(this.query()?.get('accountId') ?? '');
  protected readonly categoryId = signal(this.query()?.get('categoryId') ?? '');
  protected readonly typeFilter = signal<TransactionType[]>([]);
  protected readonly pageNo = signal(1);
  protected readonly history = signal<AuditEntry[] | null>(null);

  protected readonly accounts = rxResource({ stream: () => this.api.accounts(true) });
  protected readonly categories = rxResource({ stream: () => this.api.categories() });

  private readonly filter = computed<TransactionFilter & { v: number }>(() => ({
    search: this.search(),
    from: this.from(),
    to: this.to(),
    accountId: this.accountId(),
    categoryId: this.categoryId(),
    type: this.typeFilter(),
    flow: this.flow(),
    page: this.pageNo(),
    pageSize: this.pageSize,
    v: this.events.version(),
  }));

  protected readonly page = rxResource({
    params: this.filter,
    stream: ({ params }) => {
      const { v: _v, ...filter } = params;
      return this.api.transactions(filter);
    },
  });

  protected readonly pages = computed(() =>
    Math.max(1, Math.ceil((this.page.value()?.total ?? 0) / this.pageSize)),
  );
  protected readonly hasFilters = computed(
    () =>
      !!(
        this.search() ||
        this.from() ||
        this.to() ||
        this.accountId() ||
        this.categoryId() ||
        this.typeFilter().length
      ),
  );

  protected tone = (type: TransactionType) => TYPE_TONE[type];
  protected sign = (type: TransactionType) => SIGN[type] ?? '';

  /** Descriptions are user/imported data and rendered as text only. */
  protected title(t: Transaction) {
    return (
      t.description ||
      (t.asset ? (t.asset.name ?? t.asset.symbol) : this.i18n.instant('type.' + t.type))
    );
  }

  protected setFlow(f: TransactionFlow | '') {
    this.flow.set(f);
    this.typeFilter.set([]);
    this.pageNo.set(1);
  }

  protected toggleType(t: TransactionType) {
    this.typeFilter.update((list) =>
      list.includes(t) ? list.filter((x) => x !== t) : [...list, t],
    );
    this.pageNo.set(1);
  }

  protected clear() {
    this.search.set('');
    this.from.set('');
    this.to.set('');
    this.accountId.set('');
    this.categoryId.set('');
    this.typeFilter.set([]);
    this.pageNo.set(1);
  }

  protected async remove(t: Transaction) {
    try {
      await firstValueFrom(this.api.deleteTransaction(t.id));
      this.events.bump();
      this.toasts.show(this.i18n.instant('tx.deleted'), 'info', {
        label: this.i18n.instant('common.undo'),
        run: async () => {
          await firstValueFrom(this.api.restoreTransaction(t.id));
          this.events.bump();
        },
      });
    } catch (err) {
      this.toasts.error(err);
    }
  }

  protected async showHistory(t: Transaction) {
    try {
      this.history.set(await firstValueFrom(this.api.transactionHistory(t.id)));
    } catch (err) {
      this.toasts.error(err);
    }
  }

  protected changes(entry: AuditEntry): { field: string; from?: unknown; to: unknown }[] {
    const parsed = JSON.parse(entry.changes) as Record<string, { from?: unknown; to: unknown }>;
    const hidden = new Set([
      'Id',
      'ExternalId',
      'ImportId',
      'ExpectedTransactionId',
      'BaseCurrency',
      'TimeZone',
    ]);
    return Object.entries(parsed)
      .filter(([field, value]) => !hidden.has(field) && value.to !== null)
      .map(([field, value]) => ({ field, from: value.from, to: value.to }));
  }
}
