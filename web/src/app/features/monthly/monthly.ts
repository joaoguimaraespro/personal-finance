import { formatMoney } from '../../core/display-currency';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { liveResource } from '../../core/resource';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { NgIcon } from '@ng-icons/core';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import type { EChartsOption } from 'echarts';
import { firstValueFrom, map } from 'rxjs';
import { Api } from '../../core/api';
import { DataEvents } from '../../core/data-events';
import { DayPipe, MoneyPipe, PercentPipe, currentPeriod } from '../../core/format';
import { AllocationStatus, BucketLine, MonthlySummary } from '../../core/models';
import { QuickAdd } from '../../core/data-events';
import { canRecord, effectiveStatus, remaining } from './allocation';
import { Prefs } from '../../core/prefs';
import { Toasts } from '../../core/toast';
import { CategoryLabelPipe, categoryLabel } from '../../shared/category-label';
import { bucketLabel, BucketLabelPipe } from '../../shared/bucket-label';
import { ChartComponent } from '../../shared/chart';
import { StatusBadgeComponent, StatusTone } from '../../shared/status-badge';
import { SERIES_COLORS } from '../../shared/chart-options';
import { KpiComponent } from '../../shared/kpi';
import { MonthPickerComponent } from '../../shared/month-picker';
import { ProgressComponent } from '../../shared/progress';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { APP_ICONS, PAGE_ICONS } from '../../shared/icons';
import { PageHeaderComponent } from '../../shared/page-header';
import { EmptyStateComponent } from '../../shared/empty-state';

type Reference = 'previous' | 'average' | 'budget';

@Component({
  selector: 'app-monthly',
  imports: [
    NgIcon,
    PageHeaderComponent,
    EmptyStateComponent,
    HlmTableImports,
    HlmButtonImports,
    MonthPickerComponent,
    KpiComponent,
    ProgressComponent,
    ChartComponent,
    StatusBadgeComponent,
    TranslatePipe,
    MoneyPipe,
    DayPipe,
    PercentPipe,
    CategoryLabelPipe,
    BucketLabelPipe,
    RouterLink,
  ],
  providers: [APP_ICONS],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-page-header [icon]="icons.monthly" [title]="'monthly.title' | translate">
      <div class="flex max-w-full flex-wrap items-center gap-3">
        <div class="segmented max-w-full overflow-x-auto">
          @for (r of references; track r) {
            <button [class.active]="reference() === r" (click)="reference.set(r)">
              {{ 'monthly.vs.' + r | translate }}
            </button>
          }
        </div>
        <app-month-picker [(period)]="period" />
      </div>
    </app-page-header>

    @if (report.value(); as r) {
      @if (r.partialFrom) {
        <!-- The first month: only what happened since the start is here, so it is not compared with full months. -->
        <div
          class="mb-4 flex gap-2.5 rounded-lg border border-amber-500/30 bg-amber-500/10 p-3 text-sm text-amber-900 dark:text-amber-100"
          role="note"
        >
          <ng-icon name="lucideInfo" class="mt-0.5 shrink-0" aria-hidden="true" />
          <div>
            <p class="font-medium">
              {{ 'monthly.partialTitle' | translate: { date: (r.partialFrom | day) } }}
            </p>
            <p class="mt-0.5 text-xs opacity-90">{{ 'monthly.partialText' | translate }}</p>
          </div>
        </div>
      }
      <h2 class="mb-2 flex items-center gap-2 text-sm font-semibold">
        <ng-icon name="lucideWallet" class="text-primary" aria-hidden="true" />{{
          'dashboard.everyday' | translate
        }}
      </h2>
      <section class="grid grid-cols-2 gap-3 md:grid-cols-3 xl:grid-cols-5">
        <app-kpi
          [label]="'kpi.income' | translate"
          [value]="r.current.income"
          icon="lucideArrowDownLeft"
          [color]="colors.income"
          [reference]="ref('income')"
          [referenceLabel]="refLabel()"
        />
        <app-kpi
          [label]="'kpi.fixedExpenses' | translate"
          [value]="r.current.fixedExpenses"
          icon="lucideReceipt"
          [color]="colors.expenses"
          [reference]="ref('fixedExpenses')"
          [referenceLabel]="refLabel()"
          [higherIsBetter]="false"
        />
        <app-kpi
          [label]="'kpi.variableExpenses' | translate"
          [value]="r.current.variableExpenses"
          icon="lucideShoppingCart"
          color="#f97316"
          [reference]="ref('variableExpenses')"
          [referenceLabel]="refLabel()"
          [higherIsBetter]="false"
        />
        <app-kpi
          [label]="'kpi.expenses' | translate"
          [value]="r.current.totalExpenses"
          icon="lucideArrowUpRight"
          [color]="colors.expenses"
          [reference]="ref('totalExpenses')"
          [referenceLabel]="refLabel()"
          [higherIsBetter]="false"
        />
        <app-kpi
          [label]="'kpi.netBalance' | translate"
          [value]="r.current.netBalance"
          icon="lucideScale"
          [color]="colors.net"
          [reference]="ref('netBalance')"
          [referenceLabel]="refLabel()"
        />
      </section>

      <h2 class="mt-6 mb-2 flex items-center gap-2 text-sm font-semibold">
        <ng-icon name="lucidePiggyBank" class="text-primary" aria-hidden="true" />
        {{ 'dashboard.investments' | translate }} / {{ 'kpi.saved' | translate }}
      </h2>
      <section class="grid grid-cols-2 gap-3 md:grid-cols-3">
        <app-kpi
          [label]="'kpi.invested' | translate"
          [value]="r.current.invested"
          icon="lucideBriefcase"
          [color]="colors.invested"
          [reference]="ref('invested')"
          [referenceLabel]="refLabel()"
        />
        <app-kpi
          [label]="'kpi.saved' | translate"
          [value]="r.current.saved"
          icon="lucidePiggyBank"
          [color]="colors.saved"
          [reference]="ref('saved')"
          [referenceLabel]="refLabel()"
        />
        <app-kpi
          [label]="'kpi.savingsRate' | translate"
          [value]="r.current.savingsRate"
          icon="lucideGauge"
          kind="percent"
          color="#f59e0b"
          [reference]="ref('savingsRate')"
          [referenceLabel]="refLabel()"
        />
      </section>

      <section class="mt-6 grid gap-4 lg:grid-cols-[3fr_2fr]">
        <div class="card overflow-x-auto !p-0">
          <div class="flex items-center justify-between px-5 pt-5">
            <h2 class="card-title">
              <ng-icon name="lucideLayers" />{{ 'monthly.allocation' | translate }}
            </h2>
            <a
              routerLink="/budgets"
              class="mb-4 inline-flex items-center gap-1 text-xs font-medium text-primary hover:underline"
              ><ng-icon name="lucidePencil" aria-hidden="true" />{{
                'monthly.editBudget' | translate
              }}</a
            >
          </div>
          <!-- One row per bucket: how much of its target was set aside, with its status and what is left. -->
          <ul class="space-y-4 px-5 pb-2">
            @for (b of r.current.buckets; track b.bucketId) {
              <li>
                <div class="flex flex-wrap items-baseline justify-between gap-x-3 gap-y-1">
                  <span class="flex min-w-0 items-center gap-2 text-sm font-medium">
                    <span
                      class="inline-block h-2 w-2 shrink-0 rounded-full"
                      [style.background]="b.isInvestment ? colors.invested : colors.saved"
                    ></span
                    ><span class="truncate">{{ b.name | bucketLabel }}</span>
                  </span>
                  <span class="num text-sm">
                    <span class="font-semibold">{{ b.actual | money }}</span>
                    <span class="text-muted-foreground"> / {{ b.target | money }}</span>
                  </span>
                </div>
                <div class="bg-muted mt-1.5 h-2 overflow-hidden rounded-full">
                  <div
                    class="h-full rounded-full transition-[width]"
                    [style.width.%]="share(b.actual, b.target) * 100"
                    [style.background]="b.isInvestment ? colors.invested : colors.saved"
                  ></div>
                </div>
                @if (b.fromBrokers) {
                  <p class="text-muted-foreground mt-1 text-[11px]">
                    {{ 'monthly.fromBrokers' | translate: { amount: (b.fromBrokers | money) } }}
                  </p>
                }
                <div class="mt-2">
                  <div class="flex flex-wrap items-center gap-2">
                    @if (statusOf(b); as s) {
                      <app-status-badge [tone]="toneOf(s)">{{
                        'allocation.' + s | translate
                      }}</app-status-badge>
                    }
                    <!-- Only the exceptions are set by hand: the status otherwise follows what was set aside. -->
                    @if (manualFor(b.bucketId)) {
                      <button
                        type="button"
                        class="text-muted-foreground hover:text-foreground text-xs underline-offset-2 hover:underline"
                        (click)="setCheck(b.bucketId, 'Auto')"
                      >
                        {{ 'common.undo' | translate }}
                      </button>
                    } @else if (statusOf(b) !== 'Done') {
                      <button
                        type="button"
                        class="text-muted-foreground hover:text-foreground text-xs underline-offset-2 hover:underline"
                        (click)="setCheck(b.bucketId, 'NotApplicable')"
                      >
                        {{ 'allocation.notThisMonth' | translate }}
                      </button>
                    }
                    @if (recordable(b)) {
                      <button
                        hlmBtn
                        size="sm"
                        variant="outline"
                        [attr.aria-label]="
                          ('allocation.record' | translate) + ' · ' + (b.name | bucketLabel)
                        "
                        (click)="record(b)"
                      >
                        <ng-icon name="lucidePlus" aria-hidden="true" />{{
                          'allocation.record' | translate
                        }}
                        <span class="num text-muted-foreground">{{ left(b) | money }}</span>
                      </button>
                    }
                  </div>
                </div>
              </li>
            } @empty {
              <li class="text-muted-foreground py-4 text-center text-sm">
                <span class="inline-flex items-center gap-2"
                  ><ng-icon name="lucideInfo" aria-hidden="true" />{{
                    'monthly.noBudget' | translate
                  }}</span
                >
              </li>
            }
            <!-- The expense budget: what was spent against what is left for spending this month. -->
            <li class="border-t pt-4">
              <div class="flex flex-wrap items-baseline justify-between gap-x-3 gap-y-1">
                <span class="text-sm font-medium">{{ 'kpi.expenseBudget' | translate }}</span>
                <span class="num text-sm">
                  <span class="font-semibold">{{ r.current.totalExpenses | money }}</span>
                  <span class="text-muted-foreground">
                    / {{ r.current.expenseBudget | money }}</span
                  >
                </span>
              </div>
              <div class="bg-muted mt-1.5 h-2 overflow-hidden rounded-full">
                <div
                  class="h-full rounded-full"
                  [style.width.%]="share(r.current.totalExpenses, r.current.expenseBudget) * 100"
                  [style.background]="
                    (r.current.expenseBudgetBalance ?? 0) < 0
                      ? colors.expenses
                      : colors.expensesLight
                  "
                ></div>
              </div>
              <p
                class="num mt-1 text-xs"
                [class.tone-neg]="(r.current.expenseBudgetBalance ?? 0) < 0"
                [class.text-muted-foreground]="(r.current.expenseBudgetBalance ?? 0) >= 0"
              >
                {{
                  ((r.current.expenseBudgetBalance ?? 0) < 0
                    ? 'monthly.overBudget'
                    : 'monthly.leftToSpend'
                  ) | translate: { amount: (abs(r.current.expenseBudgetBalance ?? 0) | money) }
                }}
              </p>
            </li>
          </ul>
          <dl class="grid grid-cols-2 gap-4 px-5 py-4 text-sm">
            <div>
              <dt class="text-xs text-muted-foreground">{{ 'monthly.unallocated' | translate }}</dt>
              <dd class="num font-medium">{{ r.current.unallocated | money }}</dd>
            </div>
            @if (r.current.debtRepaid) {
              <div>
                <dt
                  class="text-xs text-muted-foreground"
                  [title]="'monthly.debtRepaidHint' | translate"
                >
                  {{ 'monthly.debtRepaid' | translate }}
                </dt>
                <dd class="num font-medium">{{ r.current.debtRepaid | money }}</dd>
              </div>
            }
            <div>
              <dt class="text-xs text-muted-foreground">
                {{ 'monthly.freeCashFlow' | translate }}
              </dt>
              <dd class="num font-medium">{{ r.current.freeCashFlow | money }}</dd>
            </div>
          </dl>
        </div>

        <!-- Where the month's spending went: the biggest categories, the rest together as "Other". -->
        <div class="card flex flex-col">
          <h2 class="card-title">
            <ng-icon name="lucideChartPie" />{{ 'monthly.byCategory' | translate }}
          </h2>
          @if (spending().total > 0) {
            <div
              class="flex flex-1 flex-col items-center gap-5 sm:flex-row lg:flex-col 2xl:flex-row"
            >
              <div class="relative size-44 shrink-0">
                <app-chart class="block size-44" [option]="donut()" />
                <div
                  class="pointer-events-none absolute inset-0 flex flex-col items-center justify-center"
                >
                  <span class="num text-lg font-semibold">{{ spending().total | money }}</span>
                  <span class="text-muted-foreground text-[11px]">{{
                    'monthly.spent' | translate
                  }}</span>
                </div>
              </div>
              <ul class="w-full min-w-0 flex-1 space-y-2.5">
                @for (c of spending().items; track c.key) {
                  <li>
                    <div class="flex items-baseline justify-between gap-3 text-sm">
                      <span class="flex min-w-0 items-center gap-2">
                        <span
                          class="inline-block h-2 w-2 shrink-0 rounded-full"
                          [style.background]="c.color"
                        ></span
                        ><span class="truncate">{{ c.name }}</span>
                      </span>
                      <span class="num shrink-0">
                        {{ c.amount | money }}
                        <span class="text-muted-foreground text-xs">{{ c.share | pct: 0 }}</span>
                      </span>
                    </div>
                    <div class="bg-muted mt-1 h-1.5 overflow-hidden rounded-full">
                      <div
                        class="h-full rounded-full"
                        [style.width.%]="c.share * 100"
                        [style.background]="c.color"
                      ></div>
                    </div>
                  </li>
                }
              </ul>
            </div>
          } @else {
            <p class="text-muted-foreground py-10 text-center text-sm">
              {{ 'monthly.noSpending' | translate }}
            </p>
          }
        </div>
      </section>

      <section class="card mt-6 overflow-x-auto !p-0">
        <h2 class="card-title px-5 pt-5">
          <ng-icon name="lucideTags" />{{ 'monthly.categories' | translate }}
        </h2>
        <table hlmTable>
          <thead hlmTHead>
            <tr hlmTr>
              <th hlmTh>{{ 'tx.category' | translate }}</th>
              <th hlmTh>{{ 'tx.nature' | translate }}</th>
              <th hlmTh class="text-right">{{ 'monthly.actual' | translate }}</th>
              <th hlmTh class="w-48">{{ 'monthly.budget' | translate }}</th>
              <th hlmTh class="text-right">{{ 'monthly.vs.previous' | translate }}</th>
              <th hlmTh class="text-right">{{ 'monthly.vs.average' | translate }}</th>
            </tr>
          </thead>
          <tbody hlmTBody>
            @for (c of categories.value() ?? []; track c.categoryId) {
              <tr hlmTr>
                <td hlmTd>
                  <a
                    class="hover:underline"
                    [routerLink]="['/transactions']"
                    [queryParams]="{
                      categoryId: c.categoryId,
                      from: period() + '-01',
                      to: monthEnd(),
                    }"
                  >
                    <span
                      class="mr-2 inline-block h-2 w-2 rounded-full"
                      [style.background]="c.color"
                    ></span
                    >{{ c | categoryLabel }}
                  </a>
                </td>
                <td hlmTd class="text-xs text-muted-foreground">
                  {{ c.nature ? ('nature.' + c.nature | translate) : '' }}
                </td>
                <td hlmTd class="num text-right font-medium">{{ c.actual | money }}</td>
                <td hlmTd>
                  @if (c.budget !== null) {
                    <app-progress tone="auto" [value]="c.budget ? c.actual / c.budget : 1" />
                    <p
                      class="num mt-1 text-xs"
                      [class.tone-neg]="c.status === 'over'"
                      [class.text-muted-foreground]="c.status !== 'over'"
                    >
                      {{ c.budget | money }} · {{ 'budgetStatus.' + c.status | translate }}
                    </p>
                  }
                </td>
                <td hlmTd class="num text-right text-muted-foreground">
                  {{ r.previous.transactionCount > 0 ? (c.previousMonth | money) : '—' }}
                </td>
                <td hlmTd class="num text-right text-muted-foreground">
                  {{ c.trailingAverage | money }}
                </td>
              </tr>
            } @empty {
              <tr hlmTr>
                <td hlmTd colspan="6" class="whitespace-normal">
                  <app-empty-state icon="lucideReceipt" [text]="'monthly.noExpenses' | translate" />
                </td>
              </tr>
            }
          </tbody>
        </table>
      </section>
    }
  `,
})
export class MonthlyComponent {
  protected readonly icons = PAGE_ICONS;
  private readonly api = inject(Api);
  private readonly events = inject(DataEvents);
  private readonly prefs = inject(Prefs);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly toasts = inject(Toasts);
  private readonly i18n = inject(TranslateService);
  protected readonly colors = SERIES_COLORS;
  protected readonly references: Reference[] = ['previous', 'average', 'budget'];
  private readonly quickAdd = inject(QuickAdd);

  private readonly queryPeriod = toSignal(
    this.route.queryParamMap.pipe(map((q) => q.get('period'))),
  );
  protected readonly period = signal(currentPeriod());
  protected readonly reference = signal<Reference>('previous');
  protected readonly monthEnd = computed(() => {
    const [y, m] = this.period().split('-').map(Number);
    return `${this.period()}-${String(new Date(y, m, 0).getDate()).padStart(2, '0')}`;
  });

  private readonly key = computed(() => ({ period: this.period(), v: this.events.version() }));
  protected readonly report = liveResource({
    params: this.key,
    stream: ({ params }) => this.api.monthly(params.period),
  });
  protected readonly categories = liveResource({
    params: this.key,
    stream: ({ params }) => this.api.categoryBreakdown(params.period),
  });
  protected readonly checks = liveResource({
    params: this.key,
    stream: ({ params }) => this.api.allocationChecks(params.period),
  });

  protected readonly refLabel = computed(() =>
    this.i18n.instant(`monthly.vsShort.${this.reference()}`),
  );

  constructor() {
    effect(() => {
      const q = this.queryPeriod();
      if (q && /^\d{4}-\d{2}$/.test(q)) this.period.set(q);
    });
    effect(() => {
      void this.router.navigate([], { queryParams: { period: this.period() }, replaceUrl: true });
    });
  }

  protected ref(field: keyof MonthlySummary & string): number | null {
    const r = this.report.value();
    if (!r) return null;
    switch (this.reference()) {
      case 'previous':
        // A month with nothing recorded is not a reference: comparing with it shows every figure as a change.
        return r.previous.transactionCount > 0
          ? ((r.previous[field] as number | null) ?? null)
          : null;
      case 'average':
        return r.trailingAverage.months
          ? ((r.trailingAverage as unknown as Record<string, number | null>)[field] ?? null)
          : null;
      case 'budget': {
        const c = r.current;
        if (field === 'totalExpenses') return c.expenseBudget;
        if (field === 'invested') return c.investmentTarget;
        if (field === 'saved') return c.savingsTarget;
        return null;
      }
    }
  }

  /** The categories spent on most this month (up to 6), the rest together, with each one's share. */
  protected readonly spending = computed(() => {
    this.prefs.translations();
    const lines = (this.categories.value() ?? [])
      .filter((l) => l.actual > 0)
      .sort((a, b) => b.actual - a.actual);
    const total = lines.reduce((sum, l) => sum + l.actual, 0);
    const top = lines.slice(0, 6).map((l) => ({
      key: l.categoryId,
      name: categoryLabel(this.i18n, l),
      amount: l.actual,
      color: l.color ?? '#a1a1aa',
    }));
    const rest = lines.slice(6);
    if (rest.length) {
      top.push({
        key: 'other',
        name: this.i18n.instant('monthly.otherCategories', { count: rest.length }),
        amount: rest.reduce((sum, l) => sum + l.actual, 0),
        color: '#a1a1aa',
      });
    }
    return { total, items: top.map((c) => ({ ...c, share: total ? c.amount / total : 0 })) };
  });

  protected readonly donut = computed<EChartsOption>(() => ({
    tooltip: {
      trigger: 'item',
      valueFormatter: (v: unknown) =>
        typeof v === 'number' ? formatMoney(this.prefs.locale(), v) : '',
    },
    series: [
      {
        type: 'pie',
        center: ['50%', '50%'],
        radius: ['68%', '96%'],
        padAngle: 1.5,
        itemStyle: { borderRadius: 3 },
        label: { show: false },
        data: this.spending().items.map((c) => ({
          name: c.name,
          value: c.amount,
          itemStyle: { color: c.color },
        })),
      },
    ],
  }));

  /** How far an amount went towards a target (0 when there is none), at most 1 for a bar. */
  protected share(actual: number, target: number | null | undefined): number {
    return target && target > 0 ? Math.min(Math.max(actual / target, 0), 1) : actual > 0 ? 1 : 0;
  }

  protected readonly abs = Math.abs;

  /** The status picked by hand, if any; without one it follows the figures ("Automatic"). */
  protected manualFor(bucketId: string): AllocationStatus | undefined {
    return this.checks.value()?.find((c) => c.bucketId === bucketId)?.status;
  }

  /** The status shown: one set by hand ("not this month"), otherwise what the figures say. */
  protected statusOf = (b: BucketLine) => effectiveStatus(this.manualFor(b.bucketId), b);
  protected toneOf = (s: AllocationStatus): StatusTone =>
    s === 'Done'
      ? 'success'
      : s === 'Partial'
        ? 'warning'
        : s === 'NotApplicable'
          ? 'neutral'
          : 'info';

  protected recordable = (b: BucketLine) => canRecord(this.manualFor(b.bucketId), b);
  protected left = (b: BucketLine) => remaining(b);

  /** Opens the movement that sets aside the rest, prefilled: contribution or savings, the bucket and the amount left. */
  protected record(b: BucketLine) {
    const [y, m] = this.period().split('-').map(Number);
    const now = new Date();
    const current = now.getFullYear() === y && now.getMonth() + 1 === m;
    this.quickAdd.addWith({
      type: b.isInvestment ? 'InvestmentContribution' : 'Savings',
      amount: remaining(b),
      bucketId: b.bucketId,
      occurredOn: current ? undefined : this.monthEnd(),
      description: this.i18n.instant('allocation.entryDescription', {
        bucket: bucketLabel(this.i18n, b.name),
      }),
    });
  }

  protected async setCheck(bucketId: string, status: AllocationStatus | 'Auto') {
    try {
      await firstValueFrom(
        status === 'Auto'
          ? this.api.clearAllocationCheck(this.period(), bucketId)
          : this.api.setAllocationCheck(this.period(), bucketId, status),
      );
      this.checks.reload();
      this.events.bump(); // the notification centre follows
    } catch (err) {
      this.toasts.error(err);
    }
  }
}
