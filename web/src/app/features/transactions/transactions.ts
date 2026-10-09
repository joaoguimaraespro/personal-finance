import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NgTemplateOutlet } from '@angular/common';
import { liveResource } from '../../core/resource';
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
import { Prefs } from '../../core/prefs';
import { Toasts } from '../../core/toast';
import { CategoryLabelPipe, categoryLabel } from '../../shared/category-label';
import { ModalComponent } from '../../shared/modal';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { SelectComponent, SelectOption } from '../../shared/select';
import { DateFieldComponent } from '../../shared/date-field';
import { NgIcon } from '@ng-icons/core';
import { APP_ICONS, PAGE_ICONS } from '../../shared/icons';
import { PageHeaderComponent } from '../../shared/page-header';
import { EmptyStateComponent } from '../../shared/empty-state';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';

const TYPE_TONE: Record<TransactionType, string> = {
  // Spending is the normal case: plain text keeps a list of expenses calm; money in stands out in green.
  Expense: '',
  Income: 'text-emerald-700 dark:text-emerald-400',
  Transfer: 'text-muted-foreground',
  Savings: 'text-cyan-600 dark:text-cyan-400',
  InvestmentContribution: 'text-violet-600 dark:text-violet-400',
  InvestmentSale: 'text-violet-600 dark:text-violet-400',
};

/** Badge colour when the row has no category colour (transfers, investments, savings). */
const TYPE_COLOR: Record<TransactionType, string> = {
  Expense: '#71717a',
  Income: '#059669',
  Transfer: '#71717a',
  Savings: '#0891b2',
  InvestmentContribution: '#7c3aed',
  InvestmentSale: '#7c3aed',
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
    PageHeaderComponent,
    EmptyStateComponent,
    HlmTooltipImports,
    NgTemplateOutlet,
    NgIcon,
    DateFieldComponent,
    SelectComponent,
    HlmTableImports,
    HlmInputImports,
    HlmButtonImports,
    TranslatePipe,
    MoneyPipe,
    DayPipe,
    CategoryLabelPipe,
    ModalComponent,
  ],
  providers: [APP_ICONS],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-page-header
      class="!mb-4"
      [icon]="icons.transactions"
      [title]="'nav.transactions' | translate"
      [subtitle]="'tx.count' | translate: { count: page.value()?.total ?? 0 }"
    />

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

    <section
      class="card mb-4 grid grid-cols-1 gap-3 !p-4 sm:grid-cols-2 lg:grid-cols-6"
      [attr.data-folded]="!filtersOpen()"
    >
      <div class="flex gap-2 sm:col-span-2">
        <div class="relative flex-1">
          <ng-icon
            name="lucideSearch"
            class="text-muted-foreground pointer-events-none absolute top-1/2 left-3 -translate-y-1/2"
            aria-hidden="true"
          />
          <input
            hlmInput
            class="w-full pl-9"
            type="search"
            [placeholder]="'common.search' | translate"
            [attr.aria-label]="'common.search' | translate"
            [value]="search()"
            (input)="search.set($any($event.target).value); pageNo.set(1)"
          />
        </div>
        <!-- Phones: the other filters fold away behind one button so the list starts on screen. -->
        <button
          hlmBtn
          variant="outline"
          class="sm:hidden"
          [attr.aria-expanded]="filtersOpen()"
          (click)="filtersOpen.update((o) => !o)"
        >
          <ng-icon name="lucideSlidersHorizontal" />{{ 'tx.filters' | translate }}
          @if (activeFilters()) {
            <span
              class="bg-primary text-primary-foreground rounded-full px-1.5 text-[11px] leading-4"
              >{{ activeFilters() }}</span
            >
          }
        </button>
      </div>
      <app-date-field
        class="fold"
        [value]="from()"
        [placeholder]="'common.from' | translate"
        (valueChange)="from.set($event); pageNo.set(1)"
        [ariaLabel]="'common.from' | translate"
        clearable
      />
      <app-date-field
        class="fold"
        [value]="to()"
        [placeholder]="'common.to' | translate"
        (valueChange)="to.set($event); pageNo.set(1)"
        [ariaLabel]="'common.to' | translate"
        clearable
      />
      <app-select
        class="fold"
        [options]="accountOptions()"
        [value]="accountId()"
        [ariaLabel]="'tx.account' | translate"
        (valueChange)="accountId.set($event); pageNo.set(1)"
      />
      <app-select
        class="fold"
        [options]="categoryOptions()"
        [value]="categoryId()"
        [ariaLabel]="'tx.category' | translate"
        (valueChange)="categoryId.set($event); pageNo.set(1)"
      />
      <div class="fold flex flex-wrap gap-2 sm:col-span-2 lg:col-span-6">
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
      <!-- Grouped by day, with the day's net; the newest day first (the API sorts by date). -->
      @for (d of days(); track d.date) {
        <div
          class="bg-muted/40 text-muted-foreground flex items-center justify-between border-b px-4 py-2 text-xs font-medium first:rounded-t-xl md:px-5"
        >
          <span class="first-letter:uppercase">{{ d.date | day: 'full' }}</span>
          @if (d.total !== 0) {
            <span class="num" [class.tone-pos]="d.total > 0">{{
              d.total | money: 'EUR' : true
            }}</span>
          }
        </div>
        <ul class="divide-y border-b last:border-b-0">
          @for (t of d.items; track t.id) {
            <li class="group relative">
              <div
                class="hover:bg-muted/40 flex items-center gap-3 px-4 py-3 transition-colors md:px-5"
                [class.cursor-pointer]="t.editable"
                (click)="open(t)"
              >
                <span
                  class="flex size-9 shrink-0 items-center justify-center rounded-full text-xs font-semibold"
                  [style.background-color]="dotColor(t) + '22'"
                  [style.color]="dotColor(t)"
                  aria-hidden="true"
                  >{{ initial(t) }}</span
                >
                <div class="min-w-0 flex-1">
                  <!-- Descriptions are user/imported data; rendered as text only, never as HTML. -->
                  <div class="truncate font-medium">{{ title(t) }}</div>
                  <div class="text-muted-foreground truncate text-xs">
                    @if (t.splits.length) {
                      <ng-container *ngTemplateOutlet="splitToggle; context: { $implicit: t }" />
                    } @else {
                      {{
                        t.categoryKey
                          ? (t | categoryLabel)
                          : (t.bucketName ?? ('type.' + t.type | translate))
                      }}
                    }
                    <span class="hidden sm:inline">
                      · {{ t.accountName }}
                      @if (t.counterAccountName) {
                        → {{ t.counterAccountName }}
                      }
                    </span>
                    @if (t.nature) {
                      <span class="hidden lg:inline">· {{ 'nature.' + t.nature | translate }}</span>
                    }
                    @if (t.asset; as a) {
                      · <span class="num">{{ a.symbol }}</span>
                      @if (a.quantity !== null) {
                        <span class="num hidden sm:inline">
                          · {{ a.quantity }} ×
                          {{ a.unitPrice !== null ? (a.unitPrice | money: t.currency) : '—' }}</span
                        >
                      }
                    }
                    @if (t.source !== 'Manual') {
                      ·
                      <span class="badge bg-muted !px-1.5 !py-0">{{
                        'source.' + t.source | translate
                      }}</span>
                    }
                  </div>
                  @if (expanded().has(t.id)) {
                    <ng-container *ngTemplateOutlet="splitList; context: { $implicit: t }" />
                  }
                </div>
                <div class="flex shrink-0 items-center gap-1">
                  <!-- Desktop: actions appear on hover or keyboard focus; phones use the row (tap). -->
                  <div
                    class="hidden items-center opacity-0 transition-opacity group-focus-within:opacity-100 group-hover:opacity-100 md:flex"
                  >
                    <ng-container *ngTemplateOutlet="actions; context: { $implicit: t }" />
                  </div>
                  <div class="text-right">
                    <div class="num font-semibold whitespace-nowrap" [class]="tone(t.type)">
                      @if (t.source === 'InterestEstimate') {
                        <span aria-hidden="true" [attr.title]="'interest.estimatedHint' | translate"
                          >≈</span
                        >
                      }
                      {{ sign(t.type) }}{{ shownAmount(t) | money: t.currency }}
                    </div>
                    @if (t.categoryAmount !== null && t.categoryAmount !== undefined) {
                      <div class="text-muted-foreground num text-[11px]">
                        {{
                          'split.ofTotal' | translate: { amount: (t.amount | money: t.currency) }
                        }}
                      </div>
                    } @else if (t.currency !== 'EUR') {
                      <div class="text-muted-foreground num text-[11px]">
                        {{ t.baseAmount | money }}
                      </div>
                    }
                    @if (t.source === 'InterestEstimate') {
                      <span class="badge bg-muted !px-1.5 !py-0 text-[10px]">{{
                        'source.InterestEstimate' | translate
                      }}</span>
                    }
                  </div>
                </div>
              </div>
              @if (actionsFor() === t.id) {
                <!-- Phones: tapping a row shows its actions. -->
                <div class="flex justify-end gap-1 px-4 pb-3 md:hidden">
                  <ng-container *ngTemplateOutlet="actions; context: { $implicit: t }" />
                </div>
              }
            </li>
          }
        </ul>
      } @empty {
        <ng-container *ngTemplateOutlet="empty" />
      }
      @if ((page.value()?.total ?? 0) > pageSize) {
        <div class="flex items-center justify-end gap-2 p-3">
          <button
            hlmBtn
            variant="outline"
            size="icon"
            [disabled]="pageNo() === 1"
            (click)="pageNo.set(pageNo() - 1)"
            [attr.aria-label]="'common.previousPage' | translate"
            [hlmTooltip]="'common.previousPage' | translate"
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
            [attr.aria-label]="'common.nextPage' | translate"
            [hlmTooltip]="'common.nextPage' | translate"
          >
            <ng-icon name="lucideChevronRight" />
          </button>
        </div>
      }
    </section>

    <!-- A split transaction: "2 categories", expanding to its lines. -->
    <ng-template #splitToggle let-t>
      <button
        type="button"
        class="hover:text-foreground inline-flex items-center gap-1 underline-offset-2 hover:underline"
        data-testid="split-toggle"
        [attr.aria-expanded]="expanded().has(t.id)"
        (click)="$event.stopPropagation(); toggleSplit(t.id)"
      >
        <ng-icon name="lucideChartPie" aria-hidden="true" />{{
          'split.count' | translate: { count: t.splits.length }
        }}<ng-icon
          name="lucideChevronDown"
          aria-hidden="true"
          class="transition-transform"
          [class.rotate-180]="expanded().has(t.id)"
        />
      </button>
    </ng-template>

    <ng-template #splitList let-t>
      <ul class="text-muted-foreground mt-1 space-y-0.5 text-xs" data-testid="split-list">
        @for (s of asTransaction(t).splits; track s.categoryId) {
          <li class="flex justify-between gap-3">
            <span class="truncate"
              >{{ s | categoryLabel }}
              @if (s.note) {
                <span class="opacity-75">· {{ s.note }}</span>
              }
            </span>
            <span class="num whitespace-nowrap">{{
              s.amount | money: asTransaction(t).currency
            }}</span>
          </li>
        }
      </ul>
    </ng-template>

    <ng-template #actions let-t>
      <button
        hlmBtn
        variant="ghost"
        size="icon-sm"
        (click)="$event.stopPropagation(); showHistory(t)"
        [attr.aria-label]="'tx.history' | translate"
        [hlmTooltip]="'tx.history' | translate"
      >
        <ng-icon name="lucideHistory" />
      </button>
      @if (t.editable) {
        <button
          hlmBtn
          variant="ghost"
          size="icon-sm"
          (click)="$event.stopPropagation(); quick.edit(t)"
          [attr.aria-label]="'common.edit' | translate"
          [hlmTooltip]="'common.edit' | translate"
        >
          <ng-icon name="lucidePencil" />
        </button>
        <button
          hlmBtn
          variant="ghost"
          size="icon-sm"
          class="text-muted-foreground hover:text-destructive"
          (click)="$event.stopPropagation(); remove(t)"
          [attr.aria-label]="'common.delete' | translate"
          [hlmTooltip]="'common.delete' | translate"
        >
          <ng-icon name="lucideTrash2" />
        </button>
      }
    </ng-template>

    <ng-template #empty>
      <app-empty-state
        [icon]="hasFilters() ? 'lucideSearch' : icons.transactions"
        [title]="'tx.empty' | translate"
        [text]="(hasFilters() ? 'tx.emptyFiltered' : 'tx.emptyHint') | translate"
      >
        @if (hasFilters()) {
          <button hlmBtn variant="outline" size="sm" (click)="clear()">
            <ng-icon name="lucideX" />{{ 'common.clear' | translate }}
          </button>
        } @else {
          <button hlmBtn size="sm" (click)="quick.add()">
            <ng-icon name="lucidePlus" />{{ 'tx.add' | translate }}
          </button>
        }
      </app-empty-state>
    </ng-template>

    <app-modal [open]="!!history()" [title]="'tx.history' | translate" (closed)="history.set(null)">
      <ol class="space-y-3">
        @for (h of history() ?? []; track $index) {
          <li class="rounded-xl border border-border p-3 text-sm">
            <div class="flex justify-between">
              <span class="inline-flex items-center gap-1.5 font-medium"
                ><ng-icon [name]="auditIcon[h.action]" class="text-primary" aria-hidden="true" />{{
                  'audit.' + h.action | translate
                }}</span
              >
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
  protected readonly icons = PAGE_ICONS;
  protected readonly auditIcon: Record<AuditEntry['action'], string> = {
    Created: 'lucidePlus',
    Updated: 'lucidePencil',
    Deleted: 'lucideTrash2',
    Restored: 'lucideRotateCcw',
  };
  private readonly api = inject(Api);
  private readonly events = inject(DataEvents);
  private readonly toasts = inject(Toasts);
  private readonly i18n = inject(TranslateService);
  private readonly prefs = inject(Prefs);
  protected readonly quick = inject(QuickAdd);
  private readonly query = toSignal(inject(ActivatedRoute).queryParamMap);

  /** '' is the explicit combined view. */
  protected readonly flowTabs: (TransactionFlow | '')[] = [
    'Everyday',
    'Investment',
    'Movement',
    '',
  ];
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
  /** Split transactions whose lines are shown. */
  protected readonly expanded = signal<ReadonlySet<string>>(new Set());

  protected readonly accounts = liveResource({ stream: () => this.api.accounts(true) });
  protected readonly categories = liveResource({ stream: () => this.api.categories() });
  protected readonly accountOptions = computed<SelectOption[]>(() => {
    this.prefs.translations();
    return [
      { value: '', label: this.i18n.instant('tx.allAccounts') },
      ...(this.accounts.value() ?? []).map((a) => ({ value: a.id, label: a.name })),
    ];
  });
  protected readonly categoryOptions = computed<SelectOption[]>(() => {
    this.prefs.translations();
    return [
      { value: '', label: this.i18n.instant('tx.allCategories') },
      ...(this.categories.value() ?? []).map((c) => ({
        value: c.id,
        label: (c.parentId ? '— ' : '') + categoryLabel(this.i18n, c),
      })),
    ];
  });

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

  protected readonly page = liveResource({
    params: this.filter,
    stream: ({ params }) => {
      const { v: _v, ...filter } = params;
      return this.api.transactions(filter);
    },
  });

  protected readonly pages = computed(() =>
    Math.max(1, Math.ceil((this.page.value()?.total ?? 0) / this.pageSize)),
  );
  /** Phones: whether the filters under the search box are shown. */
  protected readonly filtersOpen = signal(false);
  /** How many filters besides the search are set (shown on the phone's Filters button). */
  protected readonly activeFilters = computed(
    () =>
      [this.from(), this.to(), this.accountId(), this.categoryId()].filter(Boolean).length +
      this.typeFilter().length,
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

  /** The page's rows by day, newest first, each with its net in EUR (money in minus money out). */
  protected readonly days = computed(() => {
    const groups: { date: string; total: number; items: Transaction[] }[] = [];
    for (const t of this.page.value()?.items ?? []) {
      let g = groups.at(-1);
      if (!g || g.date !== t.occurredOn) {
        g = { date: t.occurredOn, total: 0, items: [] };
        groups.push(g);
      }
      g.items.push(t);
      const share = t.categoryAmount != null && t.amount ? t.categoryAmount / t.amount : 1;
      const direction = SIGN[t.type] === '+' ? 1 : SIGN[t.type] === '−' ? -1 : 0;
      g.total = Math.round((g.total + direction * t.baseAmount * share) * 100) / 100;
    }
    return groups;
  });

  /** Category colour (or a neutral one), for the row's badge. */
  private readonly categoryColors = computed(
    () => new Map((this.categories.value() ?? []).map((c) => [c.id, c.color ?? null])),
  );

  protected dotColor(t: Transaction): string {
    return (t.categoryId && this.categoryColors().get(t.categoryId)) || TYPE_COLOR[t.type];
  }

  protected initial(t: Transaction): string {
    return (this.title(t).trim()[0] ?? '·').toUpperCase();
  }

  /** Phones: the row whose actions are shown (tap a row to show or hide them). */
  protected readonly actionsFor = signal<string | null>(null);

  protected open(t: Transaction) {
    if (window.matchMedia('(min-width: 768px)').matches) {
      if (t.editable) this.quick.edit(t);
    } else {
      this.actionsFor.update((id) => (id === t.id ? null : t.id));
    }
  }

  /** Filtered by category, a split row shows only its part in that category. */
  protected shownAmount = (t: Transaction) => t.categoryAmount ?? t.amount;

  protected asTransaction = (t: unknown) => t as Transaction;

  protected toggleSplit(id: string) {
    this.expanded.update((set) => {
      const next = new Set(set);
      if (!next.delete(id)) next.add(id);
      return next;
    });
  }
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
