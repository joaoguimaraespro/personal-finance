import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { rxResource, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import type { EChartsOption } from 'echarts';
import { firstValueFrom, map } from 'rxjs';
import { Api } from '../../core/api';
import { DataEvents } from '../../core/data-events';
import { MoneyPipe, currentPeriod } from '../../core/format';
import { AllocationStatus, MonthlySummary } from '../../core/models';
import { Prefs } from '../../core/prefs';
import { Toasts } from '../../core/toast';
import { CategoryLabelPipe, categoryLabel } from '../../shared/category-label';
import { ChartComponent } from '../../shared/chart';
import { SERIES_COLORS } from '../../shared/chart-options';
import { KpiComponent } from '../../shared/kpi';
import { MonthPickerComponent } from '../../shared/month-picker';
import { ProgressComponent } from '../../shared/progress';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { UiSelect } from '../../shared/select';

type Reference = 'previous' | 'average' | 'budget';

@Component({
  selector: 'app-monthly',
  imports: [
    UiSelect,
    HlmTableImports,
    MonthPickerComponent,
    KpiComponent,
    ProgressComponent,
    ChartComponent,
    TranslatePipe,
    MoneyPipe,
    CategoryLabelPipe,
    RouterLink,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mb-6 flex flex-wrap items-center justify-between gap-3">
      <h1 class="text-2xl font-semibold tracking-tight">{{ 'monthly.title' | translate }}</h1>
      <div class="flex flex-wrap items-center gap-3">
        <div class="segmented">
          @for (r of references; track r) {
            <button [class.active]="reference() === r" (click)="reference.set(r)">
              {{ 'monthly.vs.' + r | translate }}
            </button>
          }
        </div>
        <app-month-picker [(period)]="period" />
      </div>
    </div>

    @if (report.value(); as r) {
      <section class="grid grid-cols-2 gap-3 md:grid-cols-4">
        <app-kpi
          [label]="'kpi.income' | translate"
          [value]="r.current.income"
          [color]="colors.income"
          [reference]="ref('income')"
          [referenceLabel]="refLabel()"
        />
        <app-kpi
          [label]="'kpi.fixedExpenses' | translate"
          [value]="r.current.fixedExpenses"
          [color]="colors.expenses"
          [reference]="ref('fixedExpenses')"
          [referenceLabel]="refLabel()"
          [higherIsBetter]="false"
        />
        <app-kpi
          [label]="'kpi.variableExpenses' | translate"
          [value]="r.current.variableExpenses"
          color="#f97316"
          [reference]="ref('variableExpenses')"
          [referenceLabel]="refLabel()"
          [higherIsBetter]="false"
        />
        <app-kpi
          [label]="'kpi.expenses' | translate"
          [value]="r.current.totalExpenses"
          [color]="colors.expenses"
          [reference]="ref('totalExpenses')"
          [referenceLabel]="refLabel()"
          [higherIsBetter]="false"
        />
        <app-kpi
          [label]="'kpi.invested' | translate"
          [value]="r.current.invested"
          [color]="colors.invested"
          [reference]="ref('invested')"
          [referenceLabel]="refLabel()"
        />
        <app-kpi
          [label]="'kpi.saved' | translate"
          [value]="r.current.saved"
          [color]="colors.saved"
          [reference]="ref('saved')"
          [referenceLabel]="refLabel()"
        />
        <app-kpi
          [label]="'kpi.netBalance' | translate"
          [value]="r.current.netBalance"
          [color]="colors.net"
          [reference]="ref('netBalance')"
          [referenceLabel]="refLabel()"
        />
        <app-kpi
          [label]="'kpi.savingsRate' | translate"
          [value]="r.current.savingsRate"
          kind="percent"
          color="#f59e0b"
          [reference]="ref('savingsRate')"
          [referenceLabel]="refLabel()"
        />
      </section>

      <section class="mt-6 grid gap-4 lg:grid-cols-[3fr_2fr]">
        <div class="card overflow-x-auto !p-0">
          <div class="flex items-center justify-between px-5 pt-5">
            <h2 class="card-title">{{ 'monthly.allocation' | translate }}</h2>
            <a routerLink="/budgets" class="mb-4 text-xs text-primary">{{
              'monthly.editBudget' | translate
            }}</a>
          </div>
          <table hlmTable>
            <thead hlmTHead>
              <tr hlmTr>
                <th hlmTh>{{ 'common.bucket' | translate }}</th>
                <th hlmTh class="text-right">{{ 'monthly.target' | translate }}</th>
                <th hlmTh class="text-right">{{ 'monthly.actual' | translate }}</th>
                <th hlmTh class="text-right">{{ 'monthly.difference' | translate }}</th>
                <th hlmTh>{{ 'common.status' | translate }}</th>
              </tr>
            </thead>
            <tbody hlmTBody>
              @for (b of r.current.buckets; track b.bucketId) {
                <tr hlmTr>
                  <td hlmTd>
                    <span
                      class="mr-2 inline-block h-2 w-2 rounded-full"
                      [style.background]="b.isInvestment ? colors.invested : colors.saved"
                    ></span
                    >{{ b.name }}
                  </td>
                  <td hlmTd class="num text-right text-muted-foreground">{{ b.target | money }}</td>
                  <td hlmTd class="num text-right font-medium">{{ b.actual | money }}</td>
                  <td
                    hlmTd
                    class="num text-right"
                    [class.text-rose-600]="(b.difference ?? 0) < 0"
                    [class.text-emerald-600]="(b.difference ?? 0) >= 0"
                  >
                    {{ b.difference | money: 'EUR' : true }}
                  </td>
                  <td hlmTd>
                    <select
                      uiSelect
                      class="w-auto text-xs"
                      [value]="checkFor(b.bucketId)"
                      (change)="setCheck(b.bucketId, $any($event.target).value)"
                    >
                      @for (s of statuses; track s) {
                        <option [value]="s">{{ 'allocation.' + s | translate }}</option>
                      }
                    </select>
                  </td>
                </tr>
              } @empty {
                <tr hlmTr>
                  <td hlmTd colspan="5" class="text-center text-muted-foreground">
                    {{ 'monthly.noBudget' | translate }}
                  </td>
                </tr>
              }
              <tr hlmTr class="bg-muted/50">
                <td hlmTd class="font-medium">{{ 'kpi.expenseBudget' | translate }}</td>
                <td hlmTd class="num text-right text-muted-foreground">
                  {{ r.current.expenseBudget | money }}
                </td>
                <td hlmTd class="num text-right font-medium">
                  {{ r.current.totalExpenses | money }}
                </td>
                <td
                  hlmTd
                  class="num text-right"
                  [class.text-rose-600]="(r.current.expenseBudgetBalance ?? 0) < 0"
                >
                  {{ r.current.expenseBudgetBalance | money: 'EUR' : true }}
                </td>
                <td hlmTd></td>
              </tr>
            </tbody>
          </table>
          <dl class="grid grid-cols-2 gap-4 px-5 py-4 text-sm">
            <div>
              <dt class="text-xs text-muted-foreground">{{ 'monthly.unallocated' | translate }}</dt>
              <dd class="num font-medium">{{ r.current.unallocated | money }}</dd>
            </div>
            <div>
              <dt class="text-xs text-muted-foreground">
                {{ 'monthly.freeCashFlow' | translate }}
              </dt>
              <dd class="num font-medium">{{ r.current.freeCashFlow | money }}</dd>
            </div>
          </dl>
        </div>

        <div class="card">
          <h2 class="card-title">{{ 'monthly.byCategory' | translate }}</h2>
          <app-chart class="h-64" [option]="donut()" />
        </div>
      </section>

      <section class="card mt-6 overflow-x-auto !p-0">
        <h2 class="card-title px-5 pt-5">{{ 'monthly.categories' | translate }}</h2>
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
                      [class.text-rose-600]="c.status === 'over'"
                      [class.text-muted-foreground]="c.status !== 'over'"
                    >
                      {{ c.budget | money }} · {{ 'budgetStatus.' + c.status | translate }}
                    </p>
                  }
                </td>
                <td hlmTd class="num text-right text-muted-foreground">
                  {{ c.previousMonth | money }}
                </td>
                <td hlmTd class="num text-right text-muted-foreground">
                  {{ c.trailingAverage | money }}
                </td>
              </tr>
            } @empty {
              <tr hlmTr>
                <td hlmTd colspan="6" class="py-8 text-center text-muted-foreground">
                  {{ 'monthly.noExpenses' | translate }}
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
  private readonly api = inject(Api);
  private readonly events = inject(DataEvents);
  private readonly prefs = inject(Prefs);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly toasts = inject(Toasts);
  private readonly i18n = inject(TranslateService);
  protected readonly colors = SERIES_COLORS;
  protected readonly references: Reference[] = ['previous', 'average', 'budget'];
  protected readonly statuses: AllocationStatus[] = ['Todo', 'Done', 'Partial', 'NotApplicable'];

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
  protected readonly report = rxResource({
    params: this.key,
    stream: ({ params }) => this.api.monthly(params.period),
  });
  protected readonly categories = rxResource({
    params: this.key,
    stream: ({ params }) => this.api.categoryBreakdown(params.period),
  });
  protected readonly checks = rxResource({
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
        return (r.previous[field] as number | null) ?? null;
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

  protected readonly donut = computed<EChartsOption>(() => {
    const lines = this.categories.value() ?? [];
    this.prefs.translations();
    return {
      tooltip: {
        trigger: 'item',
        valueFormatter: (v: unknown) =>
          typeof v === 'number'
            ? new Intl.NumberFormat(this.prefs.locale(), {
                style: 'currency',
                currency: 'EUR',
              }).format(v)
            : '',
      },
      legend: {
        type: 'scroll',
        orient: 'vertical',
        right: 0,
        top: 'middle',
        icon: 'circle',
        itemWidth: 8,
        textStyle: { color: '#a1a1aa', fontSize: 12 },
      },
      series: [
        {
          type: 'pie',
          center: ['32%', '50%'],
          radius: ['50%', '80%'],
          itemStyle: { borderRadius: 4, borderWidth: 2, borderColor: 'transparent' },
          label: { show: false },
          data: lines
            .filter((l) => l.actual > 0)
            .map((l) => ({
              name: categoryLabel(this.i18n, l),
              value: l.actual,
              itemStyle: { color: l.color ?? '#a1a1aa' },
            })),
        },
      ],
    };
  });

  protected checkFor(bucketId: string): AllocationStatus {
    return this.checks.value()?.find((c) => c.bucketId === bucketId)?.status ?? 'Todo';
  }

  protected async setCheck(bucketId: string, status: AllocationStatus) {
    try {
      await firstValueFrom(this.api.setAllocationCheck(this.period(), bucketId, status));
      this.checks.reload();
    } catch (err) {
      this.toasts.error(err);
    }
  }
}
