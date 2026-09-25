import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import type { EChartsOption } from 'echarts';
import { Api } from '../../core/api';
import { DataEvents } from '../../core/data-events';
import { MoneyPipe, MonthNamePipe, PercentPipe } from '../../core/format';
import { Prefs } from '../../core/prefs';
import { ChartComponent } from '../../shared/chart';
import { SERIES_COLORS, baseChart, categoryAxis, moneyAxis, moneyTooltip } from '../../shared/chart-options';

@Component({
  selector: 'app-annual',
  imports: [ChartComponent, TranslatePipe, MoneyPipe, PercentPipe, MonthNamePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mb-6 flex flex-wrap items-center justify-between gap-3">
      <h1 class="text-2xl font-semibold tracking-tight">{{ 'annual.title' | translate }}</h1>
      <div class="inline-flex items-center gap-1 rounded-xl border border-slate-200 bg-white p-1 dark:border-slate-700 dark:bg-slate-900">
        <button class="btn btn-ghost !px-2 !py-1" (click)="year.set(year() - 1)" aria-label="Previous year">‹</button>
        <span class="num w-16 text-center text-sm font-semibold">{{ year() }}</span>
        <button class="btn btn-ghost !px-2 !py-1" (click)="year.set(year() + 1)" aria-label="Next year">›</button>
      </div>
    </div>

    @if (annual.value(); as a) {
      <section class="card overflow-x-auto !p-0">
        <table class="table whitespace-nowrap">
          <thead>
            <tr>
              <th class="sticky left-0 z-10 bg-white dark:bg-slate-900">{{ 'common.month' | translate }}</th>
              <th class="text-right">{{ 'kpi.income' | translate }}</th>
              <th class="text-right">{{ 'kpi.expenseBudget' | translate }}</th>
              <th class="text-right">{{ 'kpi.fixedExpenses' | translate }}</th>
              <th class="text-right">{{ 'kpi.variableExpenses' | translate }}</th>
              <th class="text-right">{{ 'kpi.expenses' | translate }}</th>
              <th class="text-right">{{ 'kpi.invested' | translate }}</th>
              <th class="text-right">{{ 'kpi.saved' | translate }}</th>
              <th class="text-right">{{ 'kpi.netBalance' | translate }}</th>
              <th class="text-right">{{ 'kpi.savingsRate' | translate }}</th>
              <th class="text-right">{{ 'annual.cumInvested' | translate }}</th>
              <th class="text-right">{{ 'annual.cumSaved' | translate }}</th>
              <th class="text-right">{{ 'kpi.investedPct' | translate }}</th>
              <th class="text-right">{{ 'kpi.savedPct' | translate }}</th>
            </tr>
          </thead>
          <tbody>
            @for (r of a.months; track r.month.period.month) {
              <tr [class.text-slate-400]="!r.month.transactionCount">
                <td class="sticky left-0 z-10 bg-white font-medium dark:bg-slate-900">{{ r.month.period.month | monthName }}</td>
                <td class="num text-right">{{ r.month.income | money }}</td>
                <td class="num text-right text-slate-500">{{ r.month.expenseBudget | money }}</td>
                <td class="num text-right">{{ r.month.fixedExpenses | money }}</td>
                <td class="num text-right">{{ r.month.variableExpenses | money }}</td>
                <td class="num text-right">{{ r.month.totalExpenses | money }}</td>
                <td class="num text-right">{{ r.month.invested | money }}</td>
                <td class="num text-right">{{ r.month.saved | money }}</td>
                <td class="num text-right font-medium" [class.text-rose-600]="r.month.netBalance < 0">{{ r.month.netBalance | money }}</td>
                <td class="num text-right">{{ r.month.savingsRate | pct }}</td>
                <td class="num text-right">{{ r.hasIncome ? (r.cumulativeInvested | money) : '' }}</td>
                <td class="num text-right">{{ r.hasIncome ? (r.cumulativeSaved | money) : '' }}</td>
                <td class="num text-right">{{ r.month.investmentRate | pct }}</td>
                <td class="num text-right">{{ r.month.savingsOnlyRate | pct }}</td>
              </tr>
            }
          </tbody>
          <tfoot>
            <tr class="bg-slate-50 font-semibold dark:bg-slate-800/40">
              <td class="sticky left-0 z-10 bg-slate-50 px-3 py-3 dark:bg-slate-800">{{ 'annual.total' | translate }}</td>
              <td class="num px-3 text-right">{{ a.totals.income | money }}</td>
              <td class="num px-3 text-right">{{ a.totals.expenseBudget | money }}</td>
              <td class="num px-3 text-right">{{ a.totals.fixedExpenses | money }}</td>
              <td class="num px-3 text-right">{{ a.totals.variableExpenses | money }}</td>
              <td class="num px-3 text-right">{{ a.totals.totalExpenses | money }}</td>
              <td class="num px-3 text-right">{{ a.totals.invested | money }}</td>
              <td class="num px-3 text-right">{{ a.totals.saved | money }}</td>
              <td class="num px-3 text-right">{{ a.totals.netBalance | money }}</td>
              <td class="num px-3 text-right">{{ a.totals.averageMonthlySavingsRate | pct }}</td>
              <td colspan="2"></td>
              <td class="num px-3 text-right">{{ a.totals.weightedInvestmentRate | pct }}</td>
              <td class="num px-3 text-right">{{ a.totals.weightedSavingsOnlyRate | pct }}</td>
            </tr>
          </tfoot>
        </table>
      </section>
      <p class="mt-2 text-xs text-slate-400">{{ 'annual.rateNote' | translate: { weighted: (a.totals.weightedSavingsRate | pct) } }}</p>

      <section class="mt-6 grid gap-4 lg:grid-cols-2">
        <div class="card">
          <h2 class="card-title">{{ 'charts.monthlyFlows' | translate }}</h2>
          <app-chart class="h-72" [option]="flows()" />
        </div>
        <div class="card">
          <h2 class="card-title">{{ 'charts.cumulative' | translate }}</h2>
          <app-chart class="h-72" [option]="cumulative()" />
        </div>
      </section>
    }
  `,
})
export class AnnualComponent {
  private readonly api = inject(Api);
  private readonly events = inject(DataEvents);
  private readonly prefs = inject(Prefs);
  private readonly i18n = inject(TranslateService);
  protected readonly year = signal(new Date().getFullYear());
  protected readonly annual = rxResource({
    params: () => ({ year: this.year(), v: this.events.version() }),
    stream: ({ params }) => this.api.annual(params.year),
  });

  private readonly labels = computed(() =>
    Array.from({ length: 12 }, (_, i) => new Intl.DateTimeFormat(this.prefs.locale(), { month: 'short' }).format(new Date(2000, i, 1))),
  );

  protected readonly flows = computed<EChartsOption>(() => {
    const m = this.annual.value()?.months ?? [];
    this.prefs.translations();
    const t = (k: string) => this.i18n.instant(k);
    const active = (r: (typeof m)[number], v: number) => (r.month.transactionCount ? v : null);
    const bar = (name: string, color: string, data: (number | null)[]) => ({ name, type: 'bar' as const, stack: 'out', data, itemStyle: { color } });
    return {
      ...baseChart,
      tooltip: moneyTooltip(this.prefs.locale()),
      xAxis: categoryAxis(this.labels()),
      yAxis: moneyAxis(this.prefs.locale()),
      series: [
        bar(t('kpi.fixedExpenses'), SERIES_COLORS.expenses, m.map((r) => active(r, r.month.fixedExpenses))),
        bar(t('kpi.variableExpenses'), '#fb7185', m.map((r) => active(r, r.month.variableExpenses))),
        bar(t('kpi.invested'), SERIES_COLORS.invested, m.map((r) => active(r, r.month.invested))),
        bar(t('kpi.saved'), SERIES_COLORS.saved, m.map((r) => active(r, r.month.saved))),
        { name: t('kpi.income'), type: 'line', data: m.map((r) => active(r, r.month.income)), itemStyle: { color: SERIES_COLORS.income }, symbol: 'circle' },
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
        { name: this.i18n.instant('annual.cumInvested'), type: 'line', areaStyle: { opacity: 0.12 }, data: m.map((r, i) => upToLast(i, r.cumulativeInvested)), itemStyle: { color: SERIES_COLORS.invested } },
        { name: this.i18n.instant('annual.cumSaved'), type: 'line', areaStyle: { opacity: 0.12 }, data: m.map((r, i) => upToLast(i, r.cumulativeSaved)), itemStyle: { color: SERIES_COLORS.saved } },
      ],
    };
  });
}
