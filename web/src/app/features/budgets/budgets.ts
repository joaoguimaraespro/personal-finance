import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { Api } from '../../core/api';
import { DataEvents } from '../../core/data-events';
import { MonthNamePipe, currentPeriod } from '../../core/format';
import { Budget, BudgetItem, BudgetMode } from '../../core/models';
import { Toasts } from '../../core/toast';
import { CategoryLabelPipe } from '../../shared/category-label';
import { MonthPickerComponent } from '../../shared/month-picker';
import { parseAmount } from '../transactions/quick-add';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { UiSelect } from '../../shared/select';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideX } from '@ng-icons/lucide';

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
    NgIcon,
    UiSelect,
    HlmInputImports,
    HlmButtonImports,
    TranslatePipe,
    MonthPickerComponent,
    MonthNamePipe,
    CategoryLabelPipe,
  ],
  providers: [provideIcons({ lucideX })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mb-6 flex flex-wrap items-center justify-between gap-3">
      <div>
        <h1 class="text-2xl font-semibold tracking-tight">{{ 'nav.budgets' | translate }}</h1>
        <p class="text-sm text-muted-foreground">{{ 'budgets.subtitle' | translate }}</p>
      </div>
      <div class="flex items-center gap-2">
        <span class="text-sm text-muted-foreground">{{ 'budgets.effectiveFrom' | translate }}</span>
        <app-month-picker [(period)]="period" />
      </div>
    </div>

    <div class="grid gap-4 xl:grid-cols-[2fr_1fr]">
      <div class="space-y-4">
        <section class="card">
          <h2 class="card-title">{{ 'budgets.allocation' | translate }}</h2>
          <div class="space-y-2">
            @for (row of bucketRows(); track row.key) {
              <div
                class="grid grid-cols-[minmax(0,1fr)_7rem_5.5rem] items-center gap-2 sm:grid-cols-[1fr_9rem_8rem]"
              >
                <span class="truncate text-sm">{{ bucketName(row.bucketId) }}</span>
                <select
                  uiSelect
                  [value]="row.mode"
                  (change)="update(row.key, { mode: $any($event.target).value })"
                >
                  <option value="None">—</option>
                  <option value="PercentOfIncome">
                    {{ 'budgetMode.PercentOfIncome' | translate }}
                  </option>
                  <option value="FixedAmount">{{ 'budgetMode.FixedAmount' | translate }}</option>
                </select>
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
                <span class="truncate text-sm font-medium">{{ 'kpi.expenseBudget' | translate }}</span>
                <select
                  uiSelect
                  [value]="pool.mode"
                  (change)="update(pool.key, { mode: $any($event.target).value })"
                >
                  <option value="None">—</option>
                  <option value="Remainder">{{ 'budgetMode.Remainder' | translate }}</option>
                  <option value="PercentOfIncome">
                    {{ 'budgetMode.PercentOfIncome' | translate }}
                  </option>
                  <option value="FixedAmount">{{ 'budgetMode.FixedAmount' | translate }}</option>
                </select>
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
          <p class="mt-3 text-xs text-muted-foreground">{{ 'budgets.percentHint' | translate }}</p>
        </section>

        <section class="card">
          <div class="mb-4 flex flex-wrap items-center justify-between gap-2">
            <h2 class="card-title !mb-0">{{ 'budgets.categoryLimits' | translate }}</h2>
            <select
              uiSelect
              class="w-full text-sm sm:w-auto"
              (change)="addCategory($any($event.target).value); $any($event.target).value = ''"
            >
              <option value="">+ {{ 'budgets.addCategory' | translate }}</option>
              @for (c of availableCategories(); track c.id) {
                <option [value]="c.id">{{ c | categoryLabel }}</option>
              }
            </select>
          </div>
          <div class="space-y-2">
            @for (row of categoryRows(); track row.key) {
              <div
                class="grid grid-cols-[minmax(0,1fr)_6.5rem_5rem_2rem] items-center gap-2 sm:grid-cols-[1fr_9rem_8rem_2rem]"
              >
                <span class="truncate text-sm">{{ categoryFor(row.categoryId) | categoryLabel }}</span>
                <select
                  uiSelect
                  [value]="row.mode"
                  (change)="update(row.key, { mode: $any($event.target).value })"
                >
                  <option value="FixedAmount">{{ 'budgetMode.FixedAmount' | translate }}</option>
                  <option value="PercentOfIncome">
                    {{ 'budgetMode.PercentOfIncome' | translate }}
                  </option>
                </select>
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
                  aria-label="Remove"
                >
                  <ng-icon name="lucideX" />
                </button>
              </div>
            } @empty {
              <p class="text-sm text-muted-foreground">
                {{ 'budgets.noCategoryLimits' | translate }}
              </p>
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
          <button hlmBtn (click)="save()">{{ 'budgets.saveVersion' | translate }}</button>
        </div>
      </div>

      <section class="card h-fit">
        <h2 class="card-title">{{ 'budgets.versions' | translate }}</h2>
        <ul class="space-y-2">
          @for (b of budgets.value() ?? []; track b.id) {
            <li class="flex items-center justify-between rounded-xl border border-border px-3 py-2">
              <button class="text-left text-sm" (click)="period.set(b.effectiveFrom)">
                <span class="font-medium"
                  >{{ monthOf(b.effectiveFrom) | monthName }}
                  {{ b.effectiveFrom.slice(0, 4) }}</span
                >
                @if (b.note) {
                  <span class="block text-xs text-muted-foreground">{{ b.note }}</span>
                }
              </button>
              <button
                hlmBtn
                variant="ghost"
                size="sm"
                class="text-destructive hover:text-destructive"
                (click)="remove(b)"
              >
                {{ 'common.delete' | translate }}
              </button>
            </li>
          } @empty {
            <li class="text-sm text-muted-foreground">{{ 'budgets.none' | translate }}</li>
          }
        </ul>
      </section>
    </div>
  `,
})
export class BudgetsComponent {
  private readonly api = inject(Api);
  private readonly events = inject(DataEvents);
  private readonly toasts = inject(Toasts);
  private readonly i18n = inject(TranslateService);
  protected readonly period = signal(currentPeriod());
  protected readonly note = signal('');
  protected readonly rows = signal<Row[]>([]);

  protected readonly budgets = rxResource({
    params: () => this.events.version(),
    stream: () => this.api.budgets(),
  });
  protected readonly buckets = rxResource({ stream: () => this.api.buckets() });
  protected readonly categories = rxResource({ stream: () => this.api.categories() });

  protected readonly bucketRows = computed(() => this.rows().filter((r) => r.target === 'Bucket'));
  protected readonly poolRow = computed(() => this.rows().find((r) => r.target === 'ExpensePool'));
  protected readonly categoryRows = computed(() =>
    this.rows().filter((r) => r.target === 'Category'),
  );
  protected readonly availableCategories = computed(() => {
    const used = new Set(this.categoryRows().map((r) => r.categoryId));
    return (this.categories.value() ?? []).filter((c) => c.type === 'Expense' && !used.has(c.id));
  });

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
