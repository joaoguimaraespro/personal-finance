import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { liveResource } from '../../core/resource';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import type { EChartsOption } from 'echarts';
import { Api } from '../../core/api';
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
        <table hlmTable class="whitespace-nowrap">
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
          '#fb7185',
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
