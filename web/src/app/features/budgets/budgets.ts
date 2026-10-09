import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { liveResource } from '../../core/resource';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { Api } from '../../core/api';
import { DataEvents } from '../../core/data-events';
import { MoneyPipe, MonthNamePipe, currentPeriod } from '../../core/format';
import { Budget, BudgetItem, BudgetMode } from '../../core/models';
import { Toasts } from '../../core/toast';
import { CategoryLabelPipe, categoryLabel } from '../../shared/category-label';
import { MonthPickerComponent } from '../../shared/month-picker';
import { parseAmount } from '../transactions/quick-add';
import { allocationSplit } from './allocation-split';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { SelectComponent, SelectOption } from '../../shared/select';
import { Prefs } from '../../core/prefs';
import { NgIcon } from '@ng-icons/core';
import { APP_ICONS, PAGE_ICONS } from '../../shared/icons';
import { PageHeaderComponent } from '../../shared/page-header';
import { EmptyStateComponent } from '../../shared/empty-state';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';

interface Row {
  key: string;
  target: BudgetItem['target'];
  bucketId: string | null;
  categoryId: string | null;
  mode: BudgetMode | 'None';
  value: string;
}

/** Edits the budget version that starts in the chosen month. Earlier months keep their own version. */
@Component({
  selector: 'app-budgets',
  imports: [
    PageHeaderComponent,
    EmptyStateComponent,
    HlmTooltipImports,
    NgIcon,
    SelectComponent,
    HlmInputImports,
    HlmButtonImports,
    TranslatePipe,
    MonthPickerComponent,
    MonthNamePipe,
    MoneyPipe,
    CategoryLabelPipe,
  ],
  providers: [APP_ICONS],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-page-header
      [icon]="icons.budgets"
      [title]="'nav.budgets' | translate"
      [subtitle]="'budgets.subtitle' | translate"
    >
      <div class="flex flex-wrap items-center gap-2">
        <span class="text-muted-foreground flex items-center gap-1.5 text-sm"
          ><ng-icon name="lucideCalendarClock" aria-hidden="true" />{{
            'budgets.effectiveFrom' | translate
          }}</span
        >
        <app-month-picker [(period)]="period" />
      </div>
    </app-page-header>

    <div class="grid gap-4 xl:grid-cols-[2fr_1fr]">
      <div class="space-y-4">
        <section class="card">
          <h2 class="card-title">
            <ng-icon name="lucideLayers" />{{ 'budgets.allocation' | translate }}
          </h2>
          <div class="space-y-2">
            @for (row of bucketRows(); track row.key) {
              <div
                class="grid grid-cols-[minmax(0,1fr)_7rem_5.5rem] items-center gap-2 sm:grid-cols-[1fr_9rem_8rem]"
              >
                <span class="truncate text-sm">{{ bucketName(row.bucketId) }}</span>
                <app-select
                  size="sm"
                  [options]="bucketModes()"
                  [value]="row.mode"
                  [ariaLabel]="bucketName(row.bucketId)"
                  (valueChange)="update(row.key, { mode: $any($event) })"
                />
                <input
                  hlmInput
                  class="num h-8 text-right"
                  inputmode="decimal"
                  [disabled]="row.mode === 'None'"
                  [value]="row.value"
                  (input)="update(row.key, { value: $any($event.target).value })"
                />
              </div>
            }
            @if (poolRow(); as pool) {
              <div
                class="grid grid-cols-[minmax(0,1fr)_7rem_5.5rem] items-center gap-2 border-t border-border pt-3 sm:grid-cols-[1fr_9rem_8rem]"
              >
                <span class="truncate text-sm font-medium">{{
                  'kpi.expenseBudget' | translate
                }}</span>
                <app-select
                  size="sm"
                  [options]="poolModes()"
                  [value]="pool.mode"
                  [ariaLabel]="'kpi.expenseBudget' | translate"
                  (valueChange)="update(pool.key, { mode: $any($event) })"
                />
                <input
                  hlmInput
                  class="num h-8 text-right"
                  inputmode="decimal"
                  [disabled]="pool.mode === 'None' || pool.mode === 'Remainder'"
                  [value]="pool.value"
                  (input)="update(pool.key, { value: $any($event.target).value })"
                />
              </div>
            }
          </div>
          <!-- Live preview of how income is divided by what is on screen (before saving). -->
          @let sp = split();
          <div class="mt-4" role="img" [attr.aria-label]="splitLabel()">
            <div class="bg-muted flex h-2.5 overflow-hidden rounded-full">
              <div class="bg-invest" [style.width.%]="sp.invest"></div>
              <div class="bg-save" [style.width.%]="sp.save"></div>
              @if (sp.expenses !== null) {
                <div class="bg-expense/70" [style.width.%]="sp.expenses"></div>
              }
            </div>
            <p class="mt-2 flex flex-wrap gap-x-4 gap-y-1 text-xs">
              <span class="flex items-center gap-1.5"
                ><span class="bg-invest size-2 rounded-full"></span
                >{{ 'budgets.split.invest' | translate }} <b class="num">{{ sp.invest }}%</b></span
              >
              <span class="flex items-center gap-1.5"
                ><span class="bg-save size-2 rounded-full"></span
                >{{ 'budgets.split.save' | translate }} <b class="num">{{ sp.save }}%</b></span
              >
              @if (sp.expenses !== null) {
                <span class="flex items-center gap-1.5"
                  ><span class="bg-expense/70 size-2 rounded-full"></span
                  >{{ 'budgets.split.expenses' | translate }}
                  <b class="num">{{ sp.expenses }}%</b></span
                >
              }
              @if (sp.fixed) {
                <span class="text-muted-foreground"
                  >+ {{ sp.fixed | money }} {{ 'budgets.split.fixed' | translate }}</span
                >
              }
              @if (sp.over) {
                <span class="tone-neg font-medium">{{ 'budgets.split.over' | translate }}</span>
              }
            </p>
          </div>
          <p class="text-muted-foreground mt-3 flex gap-1.5 text-xs">
            <ng-icon name="lucideInfo" class="mt-px shrink-0" aria-hidden="true" />{{
              'budgets.percentHint' | translate
            }}
          </p>
        </section>

        <section class="card">
          <div class="mb-4 flex flex-wrap items-center justify-between gap-2">
            <h2 class="card-title !mb-0">
              <ng-icon name="lucideTags" />{{ 'budgets.categoryLimits' | translate }}
            </h2>
            <app-select
              class="w-full sm:w-56"
              resetOnPick
              value=""
              [options]="addCategoryOptions()"
              (valueChange)="addCategory($event)"
            />
          </div>
          <div class="space-y-2">
            @for (row of categoryRows(); track row.key) {
              <div
                class="grid grid-cols-[minmax(0,1fr)_6.5rem_5rem_2rem] items-center gap-2 sm:grid-cols-[1fr_9rem_8rem_2rem]"
              >
                <span class="truncate text-sm">{{
                  categoryFor(row.categoryId) | categoryLabel
                }}</span>
                <app-select
                  size="sm"
                  [options]="categoryModes()"
                  [value]="row.mode"
                  [ariaLabel]="categoryFor(row.categoryId) | categoryLabel"
                  (valueChange)="update(row.key, { mode: $any($event) })"
                />
                <input
                  hlmInput
                  class="num h-8 text-right"
                  inputmode="decimal"
                  [value]="row.value"
                  (input)="update(row.key, { value: $any($event.target).value })"
                />
                <button
                  hlmBtn
                  variant="ghost"
                  size="icon-sm"
                  class="text-destructive hover:text-destructive"
                  (click)="removeRow(row.key)"
                  [attr.aria-label]="'common.remove' | translate"
                  [hlmTooltip]="'common.remove' | translate"
                >
                  <ng-icon name="lucideX" />
                </button>
              </div>
            } @empty {
              <app-empty-state
                icon="lucideTags"
                [text]="'budgets.noCategoryLimits' | translate"
                bordered
              />
            }
          </div>
        </section>

        <div class="flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-end">
          <input
            hlmInput
            class="sm:max-w-sm"
            [placeholder]="'budgets.note' | translate"
            [value]="note()"
            (input)="note.set($any($event.target).value)"
          />
          <button hlmBtn (click)="save()">
            <ng-icon name="lucideSave" />{{ 'budgets.saveVersion' | translate }}
          </button>
        </div>
      </div>

      <section class="card h-fit">
        <h2 class="card-title">
          <ng-icon name="lucideHistory" />{{ 'budgets.versions' | translate }}
        </h2>
        <ul class="space-y-2">
          @for (b of budgets.value() ?? []; track b.id) {
            <li
              class="hover:bg-muted/40 flex items-center justify-between gap-2 rounded-xl border border-border px-3 py-2 transition-colors"
              [class.border-primary]="b.effectiveFrom === period()"
            >
              <button
                class="flex min-w-0 items-start gap-2 text-left text-sm"
                (click)="period.set(b.effectiveFrom)"
              >
                <ng-icon
                  name="lucideCalendarDays"
                  class="text-muted-foreground mt-0.5 shrink-0"
                  aria-hidden="true"
                />
                <span class="min-w-0">
                  <span class="font-medium"
                    >{{ monthOf(b.effectiveFrom) | monthName }}
                    {{ b.effectiveFrom.slice(0, 4) }}</span
                  >
                  @if (b.note) {
                    <span class="block text-xs text-muted-foreground">{{ b.note }}</span>
                  }
                </span>
              </button>
              <button
                hlmBtn
                variant="ghost"
                size="icon-sm"
                class="text-destructive hover:text-destructive"
                (click)="remove(b)"
                [attr.aria-label]="'common.delete' | translate"
                [hlmTooltip]="'common.delete' | translate"
              >
                <ng-icon name="lucideTrash2" />
              </button>
            </li>
          } @empty {
            <li>
              <app-empty-state
                [icon]="icons.budgets"
                [text]="'budgets.none' | translate"
                bordered
              />
            </li>
          }
        </ul>
      </section>
    </div>
  `,
})
export class BudgetsComponent {
  protected readonly icons = PAGE_ICONS;
  private readonly api = inject(Api);
  private readonly events = inject(DataEvents);
  private readonly toasts = inject(Toasts);
  private readonly i18n = inject(TranslateService);
  private readonly prefs = inject(Prefs);
  protected readonly period = signal(currentPeriod());
  protected readonly note = signal('');
  protected readonly rows = signal<Row[]>([]);

  protected readonly budgets = liveResource({
    params: () => this.events.version(),
    stream: () => this.api.budgets(),
  });
  protected readonly buckets = liveResource({ stream: () => this.api.buckets() });
  protected readonly categories = liveResource({ stream: () => this.api.categories() });

  protected readonly bucketRows = computed(() => this.rows().filter((r) => r.target === 'Bucket'));
  protected readonly split = computed(() => {
    const investment = new Set(
      (this.buckets.value() ?? []).filter((b) => b.group === 'Investment').map((b) => b.id),
    );
    return allocationSplit(this.rows(), (id) => investment.has(id));
  });
  protected readonly splitLabel = computed(() => {
    this.prefs.translations();
    const sp = this.split();
    return [
      `${this.i18n.instant('budgets.split.invest')} ${sp.invest}%`,
      `${this.i18n.instant('budgets.split.save')} ${sp.save}%`,
      sp.expenses === null ? '' : `${this.i18n.instant('budgets.split.expenses')} ${sp.expenses}%`,
    ]
      .filter(Boolean)
      .join(', ');
  });
  protected readonly poolRow = computed(() => this.rows().find((r) => r.target === 'ExpensePool'));
  protected readonly categoryRows = computed(() =>
    this.rows().filter((r) => r.target === 'Category'),
  );
  protected readonly availableCategories = computed(() => {
    const used = new Set(this.categoryRows().map((r) => r.categoryId));
    return (this.categories.value() ?? []).filter((c) => c.type === 'Expense' && !used.has(c.id));
  });
  protected readonly addCategoryOptions = computed<SelectOption[]>(() => {
    this.prefs.translations();
    return [
      { value: '', label: `+ ${this.i18n.instant('budgets.addCategory')}` },
      ...this.availableCategories().map((c) => ({
        value: c.id,
        label: categoryLabel(this.i18n, c),
      })),
    ];
  });
  protected readonly bucketModes = computed(() =>
    this.modeOptions(['PercentOfIncome', 'FixedAmount'], true),
  );
  protected readonly poolModes = computed(() =>
    this.modeOptions(['Remainder', 'PercentOfIncome', 'FixedAmount'], true),
  );
  protected readonly categoryModes = computed(() =>
    this.modeOptions(['FixedAmount', 'PercentOfIncome']),
  );

  constructor() {
    // Start from the version effective in the chosen month (or blank), so edits are always relative to reality.
    effect(() => {
      const period = this.period();
      const versions = this.budgets.value();
      const buckets = this.buckets.value();
      if (!versions || !buckets) return;
      const effective = versions.find((b) => b.effectiveFrom <= period);
      this.note.set(effective?.effectiveFrom === period ? (effective.note ?? '') : '');
      this.rows.set(
        this.toRows(
          effective,
          buckets.filter((b) => !b.archived).map((b) => b.id),
        ),
      );
    });
  }

  protected monthOf = (period: string) => Number(period.slice(5, 7));
  protected bucketName = (id: string | null) =>
    this.buckets.value()?.find((b) => b.id === id)?.name ?? '';
  protected categoryFor = (id: string | null) =>
    this.categories.value()?.find((c) => c.id === id) ?? null;

  private modeOptions(modes: BudgetMode[], withNone = false): SelectOption[] {
    this.prefs.translations();
    const options = modes.map((m) => ({ value: m, label: this.i18n.instant(`budgetMode.${m}`) }));
    return withNone ? [{ value: 'None', label: '—' }, ...options] : options;
  }

  protected update(key: string, patch: Partial<Row>) {
    this.rows.update((rows) => rows.map((r) => (r.key === key ? { ...r, ...patch } : r)));
  }

  protected addCategory(categoryId: string) {
    if (!categoryId) return;
    this.rows.update((rows) => [
      ...rows,
      {
        key: `c:${categoryId}`,
        target: 'Category',
        bucketId: null,
        categoryId,
        mode: 'FixedAmount',
        value: '',
      },
    ]);
  }

  protected removeRow(key: string) {
    this.rows.update((rows) => rows.filter((r) => r.key !== key));
  }

  protected async save() {
    const items: BudgetItem[] = [];
    for (const r of this.rows()) {
      if (r.mode === 'None') continue;
      const raw = r.mode === 'Remainder' ? 0 : parseAmount(r.value);
      if (raw === null) continue;
      // Percentages are typed as 25 (%) and stored as the fraction 0.25.
      const value = r.mode === 'PercentOfIncome' ? raw / 100 : raw;
      items.push({
        target: r.target,
        mode: r.mode,
        value,
        bucketId: r.bucketId,
        categoryId: r.categoryId,
      });
    }
    try {
      await firstValueFrom(
        this.api.saveBudget(this.period(), { note: this.note() || null, items }),
      );
      this.events.bump();
      this.toasts.show(this.i18n.instant('budgets.saved'));
    } catch (err) {
      this.toasts.error(err);
    }
  }

  protected async remove(b: Budget) {
    try {
      await firstValueFrom(this.api.deleteBudget(b.effectiveFrom));
      this.events.bump();
    } catch (err) {
      this.toasts.error(err);
    }
  }

  private toRows(budget: Budget | undefined, bucketIds: string[]): Row[] {
    const display = (i: BudgetItem) =>
      i.mode === 'PercentOfIncome'
        ? String(+(i.value * 100).toFixed(4))
        : i.mode === 'Remainder'
          ? ''
          : String(i.value);
    const items = budget?.items ?? [];
    const bucketRows: Row[] = bucketIds.map((id) => {
      const item = items.find((i) => i.target === 'Bucket' && i.bucketId === id);
      return {
        key: `b:${id}`,
        target: 'Bucket',
        bucketId: id,
        categoryId: null,
        mode: item?.mode ?? 'None',
        value: item ? display(item) : '',
      };
    });
    const pool = items.find((i) => i.target === 'ExpensePool');
    const poolRow: Row = {
      key: 'pool',
      target: 'ExpensePool',
      bucketId: null,
      categoryId: null,
      mode: pool?.mode ?? 'Remainder',
      value: pool ? display(pool) : '',
    };
    const categoryRows: Row[] = items
      .filter((i) => i.target === 'Category')
      .map((i) => ({
        key: `c:${i.categoryId}`,
        target: 'Category',
        bucketId: null,
        categoryId: i.categoryId,
        mode: i.mode,
        value: display(i),
      }));
    return [...bucketRows, poolRow, ...categoryRows];
  }
}
