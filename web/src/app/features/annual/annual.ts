import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { liveResource } from '../../core/resource';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import type { EChartsOption } from 'echarts';
import { Api } from '../../core/api';
import { AnnualRow } from '../../core/models';
import { DataEvents } from '../../core/data-events';
import { MoneyPipe, MonthNamePipe, PercentPipe } from '../../core/format';
import { Prefs } from '../../core/prefs';
import { ChartComponent } from '../../shared/chart';
import {
  SERIES_COLORS,
  baseChart,
  categoryAxis,
  moneyAxis,
  moneyTooltip,
} from '../../shared/chart-options';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { NgIcon } from '@ng-icons/core';
import { APP_ICONS, PAGE_ICONS } from '../../shared/icons';
import { PageHeaderComponent } from '../../shared/page-header';
import { KpiComponent } from '../../shared/kpi';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';

@Component({
  selector: 'app-annual',
  imports: [
    PageHeaderComponent,
    KpiComponent,
    HlmTooltipImports,
    NgIcon,
    HlmTableImports,
    HlmButtonImports,
    ChartComponent,
    TranslatePipe,
    MoneyPipe,
    PercentPipe,
    MonthNamePipe,
  ],
  providers: [APP_ICONS],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-page-header [icon]="icons.annual" [title]="'annual.title' | translate">
      <div class="inline-flex items-center gap-1">
        <button
          hlmBtn
          variant="outline"
          size="icon"
          (click)="year.set(year() - 1)"
          [attr.aria-label]="'common.previousYear' | translate"
          [hlmTooltip]="'common.previousYear' | translate"
        >
          <ng-icon name="lucideChevronLeft" />
        </button>
        <span
          class="num border-input bg-background dark:bg-input/30 inline-flex h-9 w-20 items-center justify-center rounded-md border text-sm font-semibold shadow-xs"
          >{{ year() }}</span
        >
        <button
          hlmBtn
          variant="outline"
          size="icon"
          (click)="year.set(year() + 1)"
          [attr.aria-label]="'common.nextYear' | translate"
          [hlmTooltip]="'common.nextYear' | translate"
        >
          <ng-icon name="lucideChevronRight" />
        </button>
      </div>
    </app-page-header>

    @if (annual.value(); as a) {
      <section class="mb-6 grid grid-cols-2 gap-3 md:grid-cols-3 xl:grid-cols-5">
        <app-kpi
          [label]="'kpi.income' | translate"
          [value]="a.totals.income"
          [color]="colors.income"
          icon="lucideArrowDownLeft"
        />
        <app-kpi
          [label]="'kpi.expenses' | translate"
          [value]="a.totals.totalExpenses"
          [color]="colors.expenses"
          icon="lucideArrowUpRight"
        />
        <app-kpi
          [label]="'kpi.invested' | translate"
          [value]="a.totals.invested"
          [color]="colors.invested"
          icon="lucideBriefcase"
        />
        <app-kpi
          [label]="'kpi.saved' | translate"
          [value]="a.totals.saved"
          [color]="colors.saved"
          icon="lucidePiggyBank"
        />
        <app-kpi
          class="col-span-2 md:col-span-1"
          [label]="'kpi.avgSavingsRate' | translate"
          [value]="a.totals.averageMonthlySavingsRate"
          kind="percent"
          color="#f59e0b"
          icon="lucideGauge"
        />
      </section>

      <section class="card overflow-x-auto !p-0">
        <h2 class="card-title px-5 pt-5">
          <ng-icon name="lucideCalendarDays" />{{ 'annual.title' | translate }} {{ a.year }}
        </h2>
        <!-- Phones: one line per month (balance and savings rate); tap it for every figure. -->
        <ul class="divide-border divide-y border-t sm:hidden" data-testid="annual-list">
          <li
            class="text-muted-foreground flex items-center gap-3 px-5 py-2 text-[11px] uppercase tracking-wide"
            aria-hidden="true"
          >
            <span class="flex-1">{{ 'common.month' | translate }}</span>
            <span>{{ 'kpi.netBalance' | translate }}</span>
            <span class="w-12 text-right">{{ 'kpi.savingsRate' | translate }}</span>
            <span class="w-4"></span>
          </li>
          @for (r of a.months; track r.month.period.month) {
            <li [class.text-muted-foreground]="!r.month.transactionCount">
              <details class="group">
                <summary class="flex cursor-pointer list-none items-center gap-3 px-5 py-3">
                  <span class="flex-1 font-medium">{{ r.month.period.month | monthName }}</span>
                  <span
                    class="num text-sm font-medium"
                    [class.tone-neg]="r.month.netBalance < 0"
                    [class.tone-pos]="r.month.transactionCount && r.month.netBalance > 0"
                    >{{ r.month.netBalance | money }}</span
                  >
                  <span class="num text-muted-foreground w-12 text-right text-xs">{{
                    r.month.savingsRate | pct
                  }}</span>
                  <ng-icon
                    name="lucideChevronDown"
                    class="text-muted-foreground shrink-0 transition-transform group-open:rotate-180"
                    aria-hidden="true"
                  />
                </summary>
                <dl class="grid grid-cols-2 gap-x-4 gap-y-1.5 px-5 pb-3 text-xs">
                  @for (f of fields(r); track f.key) {
                    <dt class="text-muted-foreground">{{ f.key | translate }}</dt>
                    <dd class="num text-right">
                      {{ f.pct ? (f.value | pct) : (f.value | money) }}
                    </dd>
                  }
                </dl>
              </details>
            </li>
          }
          <li class="bg-muted flex items-center gap-3 px-5 py-3 font-semibold">
            <span class="flex-1">{{ 'annual.total' | translate }}</span>
            <span class="num text-sm">{{ a.totals.netBalance | money }}</span>
            <span class="num w-12 text-right text-xs">{{
              a.totals.averageMonthlySavingsRate | pct
            }}</span>
            <span class="w-4"></span>
          </li>
        </ul>
        <table hlmTable class="hidden whitespace-nowrap sm:table">
          <thead hlmTHead>
            <tr hlmTr>
              <th hlmTh class="sticky left-0 z-10 bg-card">{{ 'common.month' | translate }}</th>
              <th hlmTh class="text-right">{{ 'kpi.income' | translate }}</th>
              <th hlmTh class="text-right">{{ 'kpi.expenseBudget' | translate }}</th>
              <th hlmTh class="text-right">{{ 'kpi.fixedExpenses' | translate }}</th>
              <th hlmTh class="text-right">{{ 'kpi.variableExpenses' | translate }}</th>
              <th hlmTh class="text-right">{{ 'kpi.expenses' | translate }}</th>
              <th hlmTh class="text-right">{{ 'kpi.invested' | translate }}</th>
              <th hlmTh class="text-right">{{ 'kpi.saved' | translate }}</th>
              <th hlmTh class="text-right">{{ 'kpi.netBalance' | translate }}</th>
              <th hlmTh class="text-right">{{ 'kpi.savingsRate' | translate }}</th>
              <th hlmTh class="text-right">{{ 'annual.cumInvested' | translate }}</th>
              <th hlmTh class="text-right">{{ 'annual.cumSaved' | translate }}</th>
              <th hlmTh class="text-right">{{ 'kpi.investedPct' | translate }}</th>
              <th hlmTh class="text-right">{{ 'kpi.savedPct' | translate }}</th>
            </tr>
          </thead>
          <tbody hlmTBody>
            @for (r of a.months; track r.month.period.month) {
              <tr hlmTr [class.text-muted-foreground]="!r.month.transactionCount">
                <td hlmTd class="sticky left-0 z-10 bg-card font-medium">
                  {{ r.month.period.month | monthName }}
                </td>
                <td hlmTd class="num text-right">{{ r.month.income | money }}</td>
                <td hlmTd class="num text-right text-muted-foreground">
                  {{ r.month.expenseBudget | money }}
                </td>
                <td hlmTd class="num text-right">{{ r.month.fixedExpenses | money }}</td>
                <td hlmTd class="num text-right">{{ r.month.variableExpenses | money }}</td>
                <td hlmTd class="num text-right">{{ r.month.totalExpenses | money }}</td>
                <td hlmTd class="num text-right">{{ r.month.invested | money }}</td>
                <td hlmTd class="num text-right">{{ r.month.saved | money }}</td>
                <td
                  hlmTd
                  class="num text-right font-medium"
                  [class.tone-neg]="r.month.netBalance < 0"
                  [class.tone-pos]="r.month.transactionCount && r.month.netBalance > 0"
                >
                  {{ r.month.netBalance | money }}
                </td>
                <td hlmTd class="num text-right">{{ r.month.savingsRate | pct }}</td>
                <td hlmTd class="num text-right">
                  {{ r.hasIncome ? (r.cumulativeInvested | money) : '' }}
                </td>
                <td hlmTd class="num text-right">
                  {{ r.hasIncome ? (r.cumulativeSaved | money) : '' }}
                </td>
                <td hlmTd class="num text-right">{{ r.month.investmentRate | pct }}</td>
                <td hlmTd class="num text-right">{{ r.month.savingsOnlyRate | pct }}</td>
              </tr>
            }
          </tbody>
          <tfoot hlmTFoot>
            <tr hlmTr class="bg-muted font-semibold">
              <td hlmTd class="sticky left-0 z-10 bg-muted px-3 py-3">
                {{ 'annual.total' | translate }}
              </td>
              <td hlmTd class="num px-3 text-right">{{ a.totals.income | money }}</td>
              <td hlmTd class="num px-3 text-right">{{ a.totals.expenseBudget | money }}</td>
              <td hlmTd class="num px-3 text-right">{{ a.totals.fixedExpenses | money }}</td>
              <td hlmTd class="num px-3 text-right">{{ a.totals.variableExpenses | money }}</td>
              <td hlmTd class="num px-3 text-right">{{ a.totals.totalExpenses | money }}</td>
              <td hlmTd class="num px-3 text-right">{{ a.totals.invested | money }}</td>
              <td hlmTd class="num px-3 text-right">{{ a.totals.saved | money }}</td>
              <td hlmTd class="num px-3 text-right">{{ a.totals.netBalance | money }}</td>
              <td hlmTd class="num px-3 text-right">
                {{ a.totals.averageMonthlySavingsRate | pct }}
              </td>
              <td hlmTd colspan="2"></td>
              <td hlmTd class="num px-3 text-right">{{ a.totals.weightedInvestmentRate | pct }}</td>
              <td hlmTd class="num px-3 text-right">
                {{ a.totals.weightedSavingsOnlyRate | pct }}
              </td>
            </tr>
          </tfoot>
        </table>
      </section>
      <p class="mt-2 text-xs text-muted-foreground">
        {{ 'annual.rateNote' | translate: { weighted: (a.totals.weightedSavingsRate | pct) } }}
      </p>

      <section class="mt-6 grid gap-4 lg:grid-cols-2">
        <div class="card">
          <h2 class="card-title">
            <ng-icon name="lucideChartColumn" />{{ 'charts.monthlyFlows' | translate }}
          </h2>
          <app-chart class="h-72" [option]="flows()" />
        </div>
        <div class="card">
          <h2 class="card-title">
            <ng-icon name="lucideChartArea" />{{ 'charts.cumulative' | translate }}
          </h2>
          <app-chart class="h-72" [option]="cumulative()" />
        </div>
      </section>
    }
  `,
})
export class AnnualComponent {
  protected readonly icons = PAGE_ICONS;
  protected readonly colors = SERIES_COLORS;
  private readonly api = inject(Api);
  private readonly events = inject(DataEvents);
  private readonly prefs = inject(Prefs);
  private readonly i18n = inject(TranslateService);
  protected readonly year = signal(new Date().getFullYear());

  /** A month's figures, as the table's columns, for the phone layout. */
  protected fields(r: AnnualRow): { key: string; value: number | null; pct?: boolean }[] {
    const m = r.month;
    return [
      { key: 'kpi.income', value: m.income },
      { key: 'kpi.expenseBudget', value: m.expenseBudget },
      { key: 'kpi.fixedExpenses', value: m.fixedExpenses },
      { key: 'kpi.variableExpenses', value: m.variableExpenses },
      { key: 'kpi.expenses', value: m.totalExpenses },
      { key: 'kpi.invested', value: m.invested },
      { key: 'kpi.saved', value: m.saved },
      ...(r.hasIncome
        ? [
            { key: 'annual.cumInvested', value: r.cumulativeInvested },
            { key: 'annual.cumSaved', value: r.cumulativeSaved },
          ]
        : []),
      { key: 'kpi.investedPct', value: m.investmentRate, pct: true },
      { key: 'kpi.savedPct', value: m.savingsOnlyRate, pct: true },
    ];
  }
  protected readonly annual = liveResource({
    params: () => ({ year: this.year(), v: this.events.version() }),
    stream: ({ params }) => this.api.annual(params.year),
  });

  private readonly labels = computed(() =>
    Array.from({ length: 12 }, (_, i) =>
      new Intl.DateTimeFormat(this.prefs.locale(), { month: 'short' }).format(new Date(2000, i, 1)),
    ),
  );

  protected readonly flows = computed<EChartsOption>(() => {
    const m = this.annual.value()?.months ?? [];
    this.prefs.translations();
    const t = (k: string) => this.i18n.instant(k);
    const active = (r: (typeof m)[number], v: number) => (r.month.transactionCount ? v : null);
    const bar = (name: string, color: string, data: (number | null)[]) => ({
      name,
      type: 'bar' as const,
      stack: 'out',
      data,
      itemStyle: { color },
    });
    return {
      ...baseChart,
      tooltip: moneyTooltip(this.prefs.locale()),
      xAxis: categoryAxis(this.labels()),
      yAxis: moneyAxis(this.prefs.locale()),
      series: [
        bar(
          t('kpi.fixedExpenses'),
          SERIES_COLORS.expenses,
          m.map((r) => active(r, r.month.fixedExpenses)),
        ),
        bar(
          t('kpi.variableExpenses'),
          SERIES_COLORS.expensesLight,
          m.map((r) => active(r, r.month.variableExpenses)),
        ),
        bar(
          t('kpi.invested'),
          SERIES_COLORS.invested,
          m.map((r) => active(r, r.month.invested)),
        ),
        bar(
          t('kpi.saved'),
          SERIES_COLORS.saved,
          m.map((r) => active(r, r.month.saved)),
        ),
        {
          name: t('kpi.income'),
          type: 'line',
          data: m.map((r) => active(r, r.month.income)),
          itemStyle: { color: SERIES_COLORS.income },
          symbol: 'circle',
        },
      ],
    };
  });

  protected readonly cumulative = computed<EChartsOption>(() => {
    const m = this.annual.value()?.months ?? [];
    this.prefs.translations();
    const last = m.map((r) => r.month.transactionCount > 0).lastIndexOf(true);
    const upToLast = (i: number, v: number) => (i <= last ? v : null);
    return {
      ...baseChart,
      tooltip: moneyTooltip(this.prefs.locale()),
      xAxis: categoryAxis(this.labels()),
      yAxis: moneyAxis(this.prefs.locale()),
      series: [
        {
          name: this.i18n.instant('annual.cumInvested'),
          type: 'line',
          areaStyle: { opacity: 0.12 },
          data: m.map((r, i) => upToLast(i, r.cumulativeInvested)),
          itemStyle: { color: SERIES_COLORS.invested },
        },
        {
          name: this.i18n.instant('annual.cumSaved'),
          type: 'line',
          areaStyle: { opacity: 0.12 },
          data: m.map((r, i) => upToLast(i, r.cumulativeSaved)),
          itemStyle: { color: SERIES_COLORS.saved },
        },
      ],
    };
  });
}
