import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import type { EChartsOption } from 'echarts';
import { firstValueFrom } from 'rxjs';
import { Api } from '../../core/api';
import { DataEvents } from '../../core/data-events';
import { DayPipe, MoneyPipe, MonthNamePipe, PercentPipe } from '../../core/format';
import { Prefs } from '../../core/prefs';
import { Toasts } from '../../core/toast';
import { ChartComponent } from '../../shared/chart';
import { SERIES_COLORS, baseChart, categoryAxis, moneyAxis, moneyTooltip, percentAxis, percentTooltip } from '../../shared/chart-options';
import { KpiComponent } from '../../shared/kpi';
import { ProgressComponent } from '../../shared/progress';

@Component({
  selector: 'app-dashboard',
  imports: [KpiComponent, ChartComponent, ProgressComponent, TranslatePipe, MoneyPipe, PercentPipe, MonthNamePipe, DayPipe, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mb-6 flex flex-wrap items-center justify-between gap-3">
      <div>
        <h1 class="text-2xl font-semibold tracking-tight">{{ 'dashboard.title' | translate }}</h1>
        <p class="text-sm text-slate-500">{{ 'dashboard.subtitle' | translate }}</p>
      </div>
      <div class="inline-flex items-center gap-1 rounded-xl border border-slate-200 bg-white p-1 dark:border-slate-700 dark:bg-slate-900">
        <button class="btn btn-ghost !px-2 !py-1" (click)="year.set(year() - 1)" aria-label="Previous year">‹</button>
        <span class="num w-16 text-center text-sm font-semibold">{{ year() }}</span>
        <button class="btn btn-ghost !px-2 !py-1" (click)="year.set(year() + 1)" aria-label="Next year">›</button>
      </div>
    </div>

    @if (overview.value(); as o) {
      <section class="grid grid-cols-2 gap-3 md:grid-cols-3 xl:grid-cols-6">
        <app-kpi [label]="'kpi.income' | translate" [value]="o.income" [color]="colors.income" />
        <app-kpi [label]="'kpi.expenses' | translate" [value]="o.totalExpenses" [color]="colors.expenses" />
        <app-kpi [label]="'kpi.invested' | translate" [value]="o.invested" [color]="colors.invested" />
        <app-kpi [label]="'kpi.saved' | translate" [value]="o.saved" [color]="colors.saved" />
        <app-kpi [label]="'kpi.netBalance' | translate" [value]="o.netBalance" [color]="colors.net" />
        <app-kpi [label]="'kpi.avgSavingsRate' | translate" [value]="o.averageMonthlySavingsRate" kind="percent" color="#f59e0b" />
      </section>
      @if (o.weightedSavingsRate !== null) {
        <p class="mt-2 text-right text-xs text-slate-400">
          {{ 'dashboard.weightedRate' | translate: { rate: (o.weightedSavingsRate | pct) } }}
        </p>
      }
    }

    @if ((pending.value() ?? []).length) {
      <section class="card mt-6 border-amber-200 bg-amber-50/60 dark:border-amber-500/30 dark:bg-amber-500/5">
        <h2 class="card-title !text-amber-700 dark:!text-amber-300">{{ 'recurring.pendingTitle' | translate }}</h2>
        <ul class="divide-y divide-amber-100 dark:divide-amber-500/10">
          @for (e of pending.value(); track e.id) {
            <li class="flex flex-wrap items-center gap-3 py-2">
              <span class="flex-1 text-sm font-medium">{{ e.name }}</span>
              <span class="text-xs text-slate-500">{{ e.dueOn | day }}</span>
              <span class="num text-sm font-semibold">{{ e.amount | money: e.currency }}</span>
              <button class="btn !py-1 text-xs" (click)="skip(e.id)">{{ 'recurring.skip' | translate }}</button>
              <button class="btn btn-primary !py-1 text-xs" (click)="confirm(e.id)">{{ 'recurring.confirm' | translate }}</button>
            </li>
          }
        </ul>
      </section>
    }

    <section class="mt-6 grid gap-4 lg:grid-cols-2">
      <div class="card">
        <h2 class="card-title">{{ 'charts.incomeVsExpenses' | translate }}</h2>
        <app-chart class="h-72" [option]="incomeVsExpenses()" />
      </div>
      <div class="card">
        <h2 class="card-title">{{ 'charts.realRates' | translate }}</h2>
        <app-chart class="h-72" [option]="rates()" />
      </div>
    </section>

    <section class="mt-6 grid gap-4 2xl:grid-cols-[3fr_1fr]">
      <div class="card overflow-x-auto !p-0">
        <h2 class="card-title px-5 pt-5">{{ 'dashboard.monthlyView' | translate }}</h2>
        <table class="table">
          <thead>
            <tr>
              <th>{{ 'common.month' | translate }}</th>
              <th class="text-right">{{ 'kpi.income' | translate }}</th>
              <th class="text-right">{{ 'kpi.expenseBudget' | translate }}</th>
              <th class="text-right">{{ 'kpi.expenses' | translate }}</th>
              <th class="text-right">{{ 'kpi.netBalance' | translate }}</th>
              <th class="text-right">{{ 'kpi.savingsRate' | translate }}</th>
              <th class="text-right">{{ 'kpi.invested' | translate }}</th>
              <th class="text-right">{{ 'kpi.saved' | translate }}</th>
              <th>{{ 'common.status' | translate }}</th>
            </tr>
          </thead>
          <tbody>
            @for (row of annual.value()?.months ?? []; track row.month.period.month) {
              <tr class="cursor-pointer hover:bg-slate-50 dark:hover:bg-slate-800/50" [class.text-slate-400]="!row.month.transactionCount" (click)="openMonth(row.month.period.month)">
                <td class="font-medium">{{ row.month.period.month | monthName }}</td>
                <td class="num text-right">{{ row.month.income | money }}</td>
                <td class="num text-right text-slate-500">{{ row.month.expenseBudget | money }}</td>
                <td class="num text-right">{{ row.month.totalExpenses | money }}</td>
                <td class="num text-right font-medium">{{ row.month.netBalance | money }}</td>
                <td class="num text-right">{{ row.month.savingsRate | pct }}</td>
                <td class="num text-right">{{ row.month.invested | money }}</td>
                <td class="num text-right">{{ row.month.saved | money }}</td>
                <td>
                  @if (row.month.transactionCount) {
                    <span class="badge" [class]="row.month.status === 'Positive' ? 'bg-emerald-100 text-emerald-700 dark:bg-emerald-500/15 dark:text-emerald-300' : 'bg-rose-100 text-rose-700 dark:bg-rose-500/15 dark:text-rose-300'">
                      {{ 'status.' + row.month.status | translate }}
                    </span>
                  }
                </td>
              </tr>
            }
          </tbody>
        </table>
      </div>

      <div class="card">
        <div class="mb-4 flex items-center justify-between">
          <h2 class="card-title !mb-0">{{ 'nav.goals' | translate }}</h2>
          <a routerLink="/goals" class="text-xs text-brand-600">{{ 'common.viewAll' | translate }}</a>
        </div>
        @for (g of goals.value() ?? []; track g.id) {
          <div class="mb-4">
            <div class="mb-1 flex justify-between text-sm">
              <span class="font-medium">{{ g.name }}</span>
              <span class="num text-slate-500">{{ g.progress | pct: 0 }}</span>
            </div>
            <app-progress [value]="g.progress" />
            <p class="num mt-1 text-xs text-slate-400">{{ g.currentAmount | money }} / {{ g.targetAmount | money }}</p>
          </div>
        } @empty {
          <p class="text-sm text-slate-400">{{ 'goals.empty' | translate }}</p>
        }
      </div>
    </section>
  `,
})
export class DashboardComponent {
  private readonly api = inject(Api);
  private readonly events = inject(DataEvents);
  private readonly prefs = inject(Prefs);
  private readonly router = inject(Router);
  private readonly toasts = inject(Toasts);
  private readonly i18n = inject(TranslateService);
  protected readonly colors = SERIES_COLORS;
  protected readonly year = signal(new Date().getFullYear());

  private readonly key = computed(() => ({ year: this.year(), v: this.events.version() }));
  protected readonly overview = rxResource({ params: this.key, stream: ({ params }) => this.api.overview(params.year) });
  protected readonly annual = rxResource({ params: this.key, stream: ({ params }) => this.api.annual(params.year) });
  protected readonly pending = rxResource({ params: () => this.events.version(), stream: () => this.api.expected() });
  protected readonly goals = rxResource({ params: () => this.events.version(), stream: () => this.api.goals() });

  private readonly labels = computed(() => {
    const locale = this.prefs.locale();
    return Array.from({ length: 12 }, (_, i) => new Intl.DateTimeFormat(locale, { month: 'short' }).format(new Date(2000, i, 1)));
  });

  protected readonly incomeVsExpenses = computed<EChartsOption>(() => {
    const months = this.annual.value()?.months ?? [];
    this.prefs.translations();
    return {
      ...baseChart,
      tooltip: moneyTooltip(this.prefs.locale()),
      xAxis: categoryAxis(this.labels()),
      yAxis: moneyAxis(this.prefs.locale()),
      series: [
        { name: this.i18n.instant('kpi.income'), type: 'bar', data: months.map((m) => (m.month.transactionCount ? m.month.income : null)), itemStyle: { color: SERIES_COLORS.income, borderRadius: [4, 4, 0, 0] }, barGap: '10%' },
        { name: this.i18n.instant('kpi.expenses'), type: 'bar', data: months.map((m) => (m.month.transactionCount ? m.month.totalExpenses : null)), itemStyle: { color: SERIES_COLORS.expenses, borderRadius: [4, 4, 0, 0] } },
      ],
    };
  });

  protected readonly rates = computed<EChartsOption>(() => {
    const months = this.annual.value()?.months ?? [];
    this.prefs.translations();
    return {
      ...baseChart,
      tooltip: percentTooltip(this.prefs.locale()),
      xAxis: categoryAxis(this.labels()),
      yAxis: percentAxis(),
      series: [
        { name: this.i18n.instant('kpi.investedPct'), type: 'line', smooth: true, connectNulls: false, data: months.map((m) => m.month.investmentRate), itemStyle: { color: SERIES_COLORS.invested } },
        { name: this.i18n.instant('kpi.savedPct'), type: 'line', smooth: true, connectNulls: false, data: months.map((m) => m.month.savingsOnlyRate), itemStyle: { color: SERIES_COLORS.saved } },
      ],
    };
  });

  protected openMonth(month: number) {
    void this.router.navigate(['/monthly'], { queryParams: { period: `${this.year()}-${String(month).padStart(2, '0')}` } });
  }

  protected async confirm(id: string) {
    try {
      await firstValueFrom(this.api.confirmExpected(id));
      this.events.bump();
      this.toasts.show(this.i18n.instant('recurring.confirmed'));
    } catch (err) {
      this.toasts.error(err);
    }
  }

  protected async skip(id: string) {
    try {
      await firstValueFrom(this.api.skipExpected(id));
      this.events.bump();
    } catch (err) {
      this.toasts.error(err);
    }
  }
}
