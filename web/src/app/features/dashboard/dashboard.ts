import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { liveResource } from '../../core/resource';
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
import {
  SERIES_COLORS,
  baseChart,
  categoryAxis,
  moneyAxis,
  moneyTooltip,
  percentAxis,
  percentTooltip,
} from '../../shared/chart-options';
import { KpiComponent } from '../../shared/kpi';
import { ProgressComponent } from '../../shared/progress';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { NgIcon } from '@ng-icons/core';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';
import { EmptyStateComponent } from '../../shared/empty-state';
import { APP_ICONS, PAGE_ICONS } from '../../shared/icons';
import { PageHeaderComponent } from '../../shared/page-header';
import { StatusBadgeComponent } from '../../shared/status-badge';

@Component({
  selector: 'app-dashboard',
  imports: [
    NgIcon,
    HlmTableImports,
    HlmButtonImports,
    KpiComponent,
    ChartComponent,
    ProgressComponent,
    TranslatePipe,
    MoneyPipe,
    PercentPipe,
    MonthNamePipe,
    DayPipe,
    RouterLink,
    HlmTooltipImports,
    PageHeaderComponent,
    EmptyStateComponent,
    StatusBadgeComponent,
  ],
  providers: [APP_ICONS],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-page-header
      [icon]="icons.dashboard"
      [title]="'dashboard.title' | translate"
      [subtitle]="'dashboard.subtitle' | translate"
    >
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

    @if (overview.value(); as o) {
      <!-- Everyday money and investments are never added together; the overall row is the explicit summary. -->
      <div class="grid gap-6 xl:grid-cols-2">
        <section aria-labelledby="dash-everyday">
          <div class="mb-2">
            <h2 id="dash-everyday" class="flex items-center gap-2 text-sm font-semibold">
              <ng-icon name="lucideWallet" class="text-primary" aria-hidden="true" />
              {{ 'dashboard.everyday' | translate }}
            </h2>
            <p class="text-xs text-muted-foreground">{{ 'dashboard.everydayHint' | translate }}</p>
          </div>
          <div class="grid grid-cols-2 gap-3 sm:grid-cols-3">
            <app-kpi
              [label]="'kpi.income' | translate"
              [value]="o.everyday.income"
              icon="lucideArrowDownLeft"
              [color]="colors.income"
            />
            <app-kpi
              [label]="'kpi.expenses' | translate"
              [value]="o.everyday.expenses"
              icon="lucideArrowUpRight"
              [color]="colors.expenses"
            />
            <app-kpi
              class="col-span-2 sm:col-span-1"
              [label]="'kpi.netBalance' | translate"
              [value]="o.everyday.netBalance"
              icon="lucideScale"
              [color]="colors.net"
            />
          </div>
        </section>

        <section aria-labelledby="dash-investments">
          <div class="mb-2">
            <h2 id="dash-investments" class="flex items-center gap-2 text-sm font-semibold">
              <ng-icon name="lucideBriefcase" class="text-primary" aria-hidden="true" />
              {{ 'dashboard.investments' | translate }}
            </h2>
            <p class="text-xs text-muted-foreground">
              {{ 'dashboard.investmentsHint' | translate }}
            </p>
          </div>
          <div class="grid grid-cols-2 gap-3 sm:grid-cols-4 xl:grid-cols-2 2xl:grid-cols-4">
            <app-kpi
              [label]="'dashboard.purchases' | translate"
              [value]="o.investments.purchases"
              icon="lucideShoppingCart"
              [color]="colors.invested"
            />
            <app-kpi
              [label]="'dashboard.sales' | translate"
              [value]="o.investments.sales"
              icon="lucideHandCoins"
              [color]="colors.invested"
            />
            <app-kpi
              [label]="'dashboard.netInvested' | translate"
              [value]="o.investments.netInvested"
              icon="lucideBriefcase"
              [color]="colors.invested"
            />
            <app-kpi
              [label]="'dashboard.investmentRate' | translate"
              [value]="o.investments.investmentRate"
              icon="lucidePercent"
              kind="percent"
              [color]="colors.invested"
            />
          </div>
        </section>
      </div>

      <section class="mt-6" aria-labelledby="dash-overall">
        <h2 id="dash-overall" class="mb-2 flex items-center gap-2 text-sm font-semibold">
          <ng-icon name="lucideScale" class="text-primary" aria-hidden="true" />
          {{ 'dashboard.overall' | translate }}
        </h2>
        <div class="grid grid-cols-2 gap-3 sm:grid-cols-3">
          <app-kpi
            [label]="'kpi.saved' | translate"
            [value]="o.saved"
            icon="lucidePiggyBank"
            [color]="colors.saved"
          />
          <app-kpi
            [label]="'kpi.avgSavingsRate' | translate"
            [value]="o.averageMonthlySavingsRate"
            icon="lucideGauge"
            kind="percent"
            color="#f59e0b"
          />
          @if (o.weightedSavingsRate !== null) {
            <p
              class="col-span-2 self-center text-xs text-muted-foreground sm:col-span-1 sm:text-right"
            >
              {{ 'dashboard.weightedRate' | translate: { rate: (o.weightedSavingsRate | pct) } }}
            </p>
          }
        </div>
      </section>
    }

    @if ((pending.value() ?? []).length) {
      <section
        class="card mt-6 border-amber-200 bg-amber-50/60 dark:border-amber-500/30 dark:bg-amber-500/5"
      >
        <h2 class="card-title !text-amber-700 dark:!text-amber-300">
          <ng-icon name="lucideCalendarClock" class="!text-amber-600 dark:!text-amber-300" />
          {{ 'recurring.pendingTitle' | translate }}
        </h2>
        <ul class="divide-y divide-amber-100 dark:divide-amber-500/10">
          @for (e of pending.value(); track e.id) {
            <li class="flex flex-wrap items-center gap-3 py-2">
              <span class="flex-1 text-sm font-medium">{{ e.name }}</span>
              <span class="text-xs text-muted-foreground">{{ e.dueOn | day }}</span>
              <span class="num text-sm font-semibold">{{ e.amount | money: e.currency }}</span>
              <button hlmBtn variant="outline" size="sm" (click)="skip(e.id)">
                <ng-icon name="lucideSkipForward" />{{ 'recurring.skip' | translate }}
              </button>
              <button hlmBtn size="sm" (click)="confirm(e.id)">
                <ng-icon name="lucideCheck" />{{ 'recurring.confirm' | translate }}
              </button>
            </li>
          }
        </ul>
      </section>
    }

    <section class="mt-6 grid gap-4 lg:grid-cols-2">
      <div class="card">
        <h2 class="card-title">
          <ng-icon name="lucideChartColumn" />{{ 'charts.incomeVsExpenses' | translate }}
        </h2>
        <app-chart class="h-72" [option]="incomeVsExpenses()" />
      </div>
      <div class="card">
        <h2 class="card-title">
          <ng-icon name="lucideChartLine" />{{ 'charts.realRates' | translate }}
        </h2>
        <app-chart class="h-72" [option]="rates()" />
      </div>
    </section>

    <section class="mt-6 grid gap-4 2xl:grid-cols-[3fr_1fr]">
      <div class="card overflow-x-auto !p-0">
        <h2 class="card-title px-5 pt-5">
          <ng-icon name="lucideCalendarDays" />{{ 'dashboard.monthlyView' | translate }}
        </h2>
        <table hlmTable>
          <thead hlmTHead>
            <tr hlmTr class="border-b-0">
              <th hlmTh></th>
              <th hlmTh colspan="4" class="border-b text-center text-xs text-muted-foreground">
                {{ 'dashboard.everyday' | translate }}
              </th>
              <th hlmTh colspan="3" class="border-b text-center text-xs text-muted-foreground">
                {{ 'dashboard.investments' | translate }} / {{ 'kpi.saved' | translate }}
              </th>
              <th hlmTh></th>
            </tr>
            <tr hlmTr>
              <th hlmTh>{{ 'common.month' | translate }}</th>
              <th hlmTh class="text-right">{{ 'kpi.income' | translate }}</th>
              <th hlmTh class="text-right">{{ 'kpi.expenseBudget' | translate }}</th>
              <th hlmTh class="text-right">{{ 'kpi.expenses' | translate }}</th>
              <th hlmTh class="text-right">{{ 'kpi.netBalance' | translate }}</th>
              <th hlmTh class="text-right">{{ 'kpi.savingsRate' | translate }}</th>
              <th hlmTh class="text-right">{{ 'kpi.invested' | translate }}</th>
              <th hlmTh class="text-right">{{ 'kpi.saved' | translate }}</th>
              <th hlmTh>{{ 'common.status' | translate }}</th>
            </tr>
          </thead>
          <tbody hlmTBody>
            @for (row of annual.value()?.months ?? []; track row.month.period.month) {
              <tr
                hlmTr
                class="cursor-pointer hover:bg-muted/50"
                [class.text-muted-foreground]="!row.month.transactionCount"
                (click)="openMonth(row.month.period.month)"
              >
                <td hlmTd class="font-medium">{{ row.month.period.month | monthName }}</td>
                <td hlmTd class="num text-right">{{ row.month.income | money }}</td>
                <td hlmTd class="num text-right text-muted-foreground">
                  {{ row.month.expenseBudget | money }}
                </td>
                <td hlmTd class="num text-right">{{ row.month.totalExpenses | money }}</td>
                <td
                  hlmTd
                  class="num text-right font-medium"
                  [class.tone-pos]="row.month.transactionCount && row.month.netBalance > 0"
                  [class.tone-neg]="row.month.netBalance < 0"
                >
                  {{ row.month.netBalance | money }}
                </td>
                <td hlmTd class="num text-right">{{ row.month.savingsRate | pct }}</td>
                <td hlmTd class="num text-right">{{ row.month.invested | money }}</td>
                <td hlmTd class="num text-right">{{ row.month.saved | money }}</td>
                <td hlmTd>
                  @if (row.month.transactionCount) {
                    <app-status-badge
                      [tone]="row.month.status === 'Positive' ? 'success' : 'danger'"
                      [icon]="
                        row.month.status === 'Positive' ? 'lucideTrendingUp' : 'lucideTrendingDown'
                      "
                    >
                      {{ 'status.' + row.month.status | translate }}
                    </app-status-badge>
                  }
                </td>
              </tr>
            }
          </tbody>
        </table>
      </div>

      <div class="card">
        <div class="mb-4 flex items-center justify-between">
          <h2 class="card-title !mb-0">
            <ng-icon [name]="icons.goals" />{{ 'nav.goals' | translate }}
          </h2>
          <a
            routerLink="/goals"
            class="inline-flex items-center gap-1 text-xs font-medium text-primary hover:underline"
            >{{ 'common.viewAll' | translate
            }}<ng-icon name="lucideChevronRight" aria-hidden="true"
          /></a>
        </div>
        @for (g of goals.value() ?? []; track g.id) {
          <div class="mb-4">
            <div class="mb-1 flex justify-between text-sm">
              <span class="font-medium">{{ g.name }}</span>
              <span class="num text-muted-foreground">{{ g.progress | pct: 0 }}</span>
            </div>
            <app-progress [value]="g.progress" />
            <p class="num mt-1 text-xs text-muted-foreground">
              {{ g.currentAmount | money }} / {{ g.targetAmount | money }}
            </p>
          </div>
        } @empty {
          <app-empty-state [icon]="icons.goals" [text]="'goals.empty' | translate">
            <a hlmBtn variant="outline" size="sm" routerLink="/goals">
              <ng-icon name="lucidePlus" />{{ 'goals.new' | translate }}
            </a>
          </app-empty-state>
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
  protected readonly icons = PAGE_ICONS;
  protected readonly year = signal(new Date().getFullYear());

  private readonly key = computed(() => ({ year: this.year(), v: this.events.version() }));
  protected readonly overview = liveResource({
    params: this.key,
    stream: ({ params }) => this.api.overview(params.year),
  });
  protected readonly annual = liveResource({
    params: this.key,
    stream: ({ params }) => this.api.annual(params.year),
  });
  protected readonly pending = liveResource({
    params: () => this.events.version(),
    stream: () => this.api.expected(),
  });
  protected readonly goals = liveResource({
    params: () => this.events.version(),
    stream: () => this.api.goals(),
  });

  private readonly labels = computed(() => {
    const locale = this.prefs.locale();
    return Array.from({ length: 12 }, (_, i) =>
      new Intl.DateTimeFormat(locale, { month: 'short' }).format(new Date(2000, i, 1)),
    );
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
        {
          name: this.i18n.instant('kpi.income'),
          type: 'bar',
          data: months.map((m) => (m.month.transactionCount ? m.month.income : null)),
          itemStyle: { color: SERIES_COLORS.income, borderRadius: [4, 4, 0, 0] },
          barGap: '10%',
        },
        {
          name: this.i18n.instant('kpi.expenses'),
          type: 'bar',
          data: months.map((m) => (m.month.transactionCount ? m.month.totalExpenses : null)),
          itemStyle: { color: SERIES_COLORS.expenses, borderRadius: [4, 4, 0, 0] },
        },
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
        {
          name: this.i18n.instant('kpi.investedPct'),
          type: 'line',
          smooth: true,
          connectNulls: false,
          data: months.map((m) => m.month.investmentRate),
          itemStyle: { color: SERIES_COLORS.invested },
        },
        {
          name: this.i18n.instant('kpi.savedPct'),
          type: 'line',
          smooth: true,
          connectNulls: false,
          data: months.map((m) => m.month.savingsOnlyRate),
          itemStyle: { color: SERIES_COLORS.saved },
        },
      ],
    };
  });

  protected openMonth(month: number) {
    void this.router.navigate(['/monthly'], {
      queryParams: { period: `${this.year()}-${String(month).padStart(2, '0')}` },
    });
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
