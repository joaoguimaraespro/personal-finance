import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { liveResource } from '../../core/resource';
import { RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import type { EChartsOption } from 'echarts';
import { firstValueFrom } from 'rxjs';
import { Api, PortfolioScope } from '../../core/api';
import { DataEvents } from '../../core/data-events';
import { DayPipe, MoneyPipe, PercentPipe } from '../../core/format';
import { AssetClass, Broker, PositionLine } from '../../core/models';
import { Prefs } from '../../core/prefs';
import { Toasts } from '../../core/toast';
import { ChartComponent } from '../../shared/chart';
import {
  SERIES_COLORS,
  baseChart,
  categoryAxis,
  moneyAxis,
  moneyTooltip,
} from '../../shared/chart-options';
import { KpiComponent } from '../../shared/kpi';
import { ModalComponent } from '../../shared/modal';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { SelectComponent, SelectOption } from '../../shared/select';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideArrowDown,
  lucideArrowUp,
  lucideArrowUpDown,
  lucideChevronDown,
  lucideChevronRight,
  lucideTrendingDown,
  lucideTrendingUp,
} from '@ng-icons/lucide';

type SortKey = 'value' | 'gain' | 'today' | 'weight';

const CLASS_COLORS: Record<AssetClass, string> = {
  Stock: '#8b5cf6',
  Etf: '#10b981',
  Bond: '#0891b2',
  Fund: '#84cc16',
  Crypto: '#f59e0b',
  Cash: '#a1a1aa',
  Other: '#a855f7',
};

type Range = '1Y' | '3Y' | 'ALL';

/** Read-only view of broker data: nothing on this page can change a broker account. */
@Component({
  selector: 'app-portfolio',
  imports: [
    NgIcon,
    SelectComponent,
    HlmTableImports,
    HlmInputImports,
    HlmButtonImports,
    KpiComponent,
    ChartComponent,
    ModalComponent,
    TranslatePipe,
    MoneyPipe,
    PercentPipe,
    DayPipe,
    RouterLink,
  ],
  providers: [
    provideIcons({
      lucideArrowDown,
      lucideArrowUp,
      lucideArrowUpDown,
      lucideChevronDown,
      lucideChevronRight,
      lucideTrendingDown,
      lucideTrendingUp,
    }),
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mb-6 flex flex-wrap items-center justify-between gap-3">
      <div>
        <h1 class="text-2xl font-semibold tracking-tight">{{ 'nav.portfolio' | translate }}</h1>
        <p class="text-sm text-muted-foreground">
          {{ 'portfolio.readOnly' | translate }}
          @if (summary.value()?.lastSyncUtc; as synced) {
            · {{ 'portfolio.lastSync' | translate }} {{ synced | day }}
          }
        </p>
      </div>
      @if (scopeOptions().length > 1) {
        <app-select
          class="w-full sm:w-56"
          [options]="scopeOptions()"
          [value]="scopeKey()"
          (valueChange)="scopeKey.set($event)"
          [ariaLabel]="'portfolio.scope' | translate"
        />
      }
    </div>

    @if (summary.value(); as s) {
      @if (s.positions === 0 && !s.accounts.length) {
        <section class="card py-12 text-center">
          <p class="text-muted-foreground">{{ 'portfolio.empty' | translate }}</p>
          <a routerLink="/connections" hlmBtn class="mt-4">{{ 'portfolio.connect' | translate }}</a>
        </section>
      } @else {
        <section class="card mb-4 grid gap-5 sm:grid-cols-3">
          <div>
            <p class="text-xs text-muted-foreground">{{ 'portfolio.totalValue' | translate }}</p>
            <p class="num mt-1 text-3xl font-semibold tracking-tight">{{ s.totalValue | money }}</p>
          </div>
          <div>
            <p class="text-xs text-muted-foreground">{{ 'portfolio.today' | translate }}</p>
            @if (s.dayChange !== null) {
              <p
                class="num mt-1 flex items-center gap-1.5 text-xl font-semibold"
                [class]="tone(s.dayChange)"
              >
                <ng-icon
                  [name]="s.dayChange >= 0 ? 'lucideTrendingUp' : 'lucideTrendingDown'"
                  aria-hidden="true"
                />
                {{ s.dayChange | money: 'EUR' : true }}
                <span class="text-sm font-medium">({{ signedPct(s.dayChangePercent) }})</span>
              </p>
            } @else {
              <p class="mt-1 text-sm text-muted-foreground">
                {{ 'portfolio.todayPending' | translate }}
              </p>
            }
          </div>
          <div>
            <p class="text-xs text-muted-foreground">{{ 'portfolio.totalReturn' | translate }}</p>
            <p
              class="num mt-1 flex items-center gap-1.5 text-xl font-semibold"
              [class]="tone(s.totalReturn)"
            >
              <ng-icon
                [name]="s.totalReturn >= 0 ? 'lucideTrendingUp' : 'lucideTrendingDown'"
                aria-hidden="true"
              />
              {{ s.totalReturn | money: 'EUR' : true }}
              <span class="text-sm font-medium">({{ signedPct(s.totalReturnPercent) }})</span>
            </p>
          </div>
        </section>

        <section class="grid grid-cols-2 gap-3 md:grid-cols-3">
          <app-kpi
            [label]="'portfolio.contributions' | translate"
            [value]="s.netContributions"
            color="#71717a"
          />
          <app-kpi
            [label]="'portfolio.totalReturnPct' | translate"
            [value]="s.totalReturnPercent"
            kind="percent"
            color="#f59e0b"
          />
          <app-kpi
            [label]="'portfolio.unrealized' | translate"
            [value]="s.unrealizedPnl"
            [color]="colors.invested"
          />
          <app-kpi
            [label]="'portfolio.realized' | translate"
            [value]="s.realizedPnl"
            [color]="colors.invested"
          />
          <app-kpi
            [label]="'portfolio.dividends' | translate"
            [value]="s.dividends"
            [color]="colors.income"
          />
          <app-kpi
            [label]="'portfolio.fees' | translate"
            [value]="s.fees"
            [color]="colors.expenses"
          />
        </section>

        <section class="mt-6 grid gap-4 lg:grid-cols-[2fr_1fr]">
          <div class="card">
            <div class="mb-4 flex flex-wrap items-center justify-between gap-3">
              <h2 class="card-title !mb-0">{{ 'portfolio.performance' | translate }}</h2>
              <div class="flex flex-wrap items-center gap-x-4 gap-y-2">
                @if (performance.value(); as p) {
                  <span
                    class="text-xs text-muted-foreground"
                    [title]="'portfolio.twrHint' | translate"
                    >TWR <b class="num text-foreground">{{ p.timeWeightedReturn | pct }}</b></span
                  >
                  <span
                    class="text-xs text-muted-foreground"
                    [title]="'portfolio.xirrHint' | translate"
                    >XIRR <b class="num text-foreground">{{ p.moneyWeightedReturn | pct }}</b></span
                  >
                }
                <div class="segmented">
                  @for (r of ranges; track r) {
                    <button [class.active]="range() === r" (click)="range.set(r)">{{ r }}</button>
                  }
                </div>
              </div>
            </div>
            <app-chart class="h-72" [option]="valueChart()" />
          </div>
          <div class="card">
            <div class="mb-4 flex items-center justify-between">
              <h2 class="card-title !mb-0">{{ 'portfolio.allocation' | translate }}</h2>
              <button class="text-xs text-primary" (click)="openTargets()">
                {{ 'portfolio.editTargets' | translate }}
              </button>
            </div>
            <app-chart class="h-44" [option]="allocationChart()" />
            <div class="table-wrap">
              <table hlmTable class="mt-2">
                <thead hlmTHead>
                  <tr hlmTr>
                    <th hlmTh></th>
                    <th hlmTh class="text-right">{{ 'portfolio.actual' | translate }}</th>
                    <th hlmTh class="text-right">{{ 'portfolio.target' | translate }}</th>
                    <th hlmTh class="text-right">Δ</th>
                  </tr>
                </thead>
                <tbody hlmTBody>
                  @for (a of allocation.value() ?? []; track a.assetClass) {
                    <tr hlmTr>
                      <td hlmTd>
                        <span
                          class="mr-2 inline-block h-2 w-2 rounded-full"
                          [style.background]="classColor(a.assetClass)"
                        ></span
                        >{{ 'assetClass.' + a.assetClass | translate }}
                      </td>
                      <td hlmTd class="num text-right">{{ a.actual | pct }}</td>
                      <td hlmTd class="num text-right text-muted-foreground">
                        {{ a.target | pct }}
                      </td>
                      <td
                        hlmTd
                        class="num text-right"
                        [class.text-rose-600]="(a.difference ?? 0) < -0.02"
                        [class.text-emerald-600]="(a.difference ?? 0) > 0.02"
                      >
                        {{
                          a.difference === null
                            ? ''
                            : (a.difference > 0 ? '+' : '') +
                              (a.difference * 100).toFixed(1) +
                              ' pp'
                        }}
                      </td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
            <p class="mt-2 text-[11px] text-muted-foreground">
              {{ 'portfolio.noRebalance' | translate }}
            </p>
          </div>
        </section>

        <section class="card mt-6 overflow-x-auto !p-0">
          <h2 class="card-title px-5 pt-5">{{ 'portfolio.positions' | translate }}</h2>
          <table hlmTable class="whitespace-nowrap">
            <thead hlmTHead>
              <tr hlmTr>
                <th hlmTh>{{ 'portfolio.security' | translate }}</th>
                @for (col of sortColumns; track col.key) {
                  <th
                    hlmTh
                    class="text-right"
                    [class.hidden]="col.key === 'weight' || col.key === 'value'"
                    [class.sm:table-cell]="col.key === 'weight' || col.key === 'value'"
                    [attr.aria-sort]="
                      sortKey() === col.key ? (sortDesc() ? 'descending' : 'ascending') : null
                    "
                  >
                    <button
                      type="button"
                      class="inline-flex items-center gap-1 hover:text-foreground"
                      [class.text-foreground]="sortKey() === col.key"
                      (click)="sortBy(col.key)"
                    >
                      {{ col.label | translate }}
                      <ng-icon
                        [name]="
                          sortKey() !== col.key
                            ? 'lucideArrowUpDown'
                            : sortDesc()
                              ? 'lucideArrowDown'
                              : 'lucideArrowUp'
                        "
                        class="text-xs opacity-60"
                        aria-hidden="true"
                      />
                    </button>
                  </th>
                }
              </tr>
            </thead>
            <tbody hlmTBody>
              @for (p of sortedPositions(); track p.securityId) {
                <tr
                  hlmTr
                  class="cursor-pointer hover:bg-muted/50"
                  [attr.aria-expanded]="expanded() === p.securityId"
                  (click)="toggle(p.securityId)"
                >
                  <td hlmTd>
                    <div class="flex items-center gap-1.5 font-medium">
                      <ng-icon
                        [name]="
                          expanded() === p.securityId ? 'lucideChevronDown' : 'lucideChevronRight'
                        "
                        class="text-xs text-muted-foreground"
                        aria-hidden="true"
                      />
                      {{ p.symbol }}
                      <span class="badge bg-muted !px-1.5 !py-0 text-[10px]">{{
                        'assetClass.' + p.assetClass | translate
                      }}</span>
                    </div>
                    <div class="max-w-36 truncate pl-5 text-xs text-muted-foreground sm:max-w-56">
                      {{ p.name }}
                    </div>
                    <div class="num pl-5 text-xs font-medium sm:hidden">
                      {{ p.marketValueBase | money }}
                    </div>
                  </td>
                  <td hlmTd class="hidden text-right sm:table-cell">
                    <div class="num font-medium">{{ p.marketValueBase | money }}</div>
                    <div class="num text-xs text-muted-foreground">
                      {{ p.quantity }} × {{ p.lastPrice | money: p.currency }}
                    </div>
                  </td>
                  <td hlmTd class="text-right" [class]="tone(p.unrealizedPnlBase)">
                    <div class="num font-medium">
                      {{ p.unrealizedPnlBase | money: 'EUR' : true }}
                    </div>
                    <div class="num text-xs">{{ signedPct(p.unrealizedPnlPercent) }}</div>
                  </td>
                  <td hlmTd class="text-right" [class]="tone(p.dayChangeBase)">
                    @if (p.dayChangeBase !== null) {
                      <div class="num font-medium">{{ p.dayChangeBase | money: 'EUR' : true }}</div>
                      <div class="num text-xs">{{ signedPct(p.dayChangePercent) }}</div>
                    } @else {
                      <span class="text-muted-foreground">—</span>
                    }
                  </td>
                  <td hlmTd class="hidden text-right sm:table-cell">
                    <div class="num">{{ p.portfolioWeight | pct }}</div>
                    <div
                      class="ml-auto mt-1 h-1 w-16 overflow-hidden rounded-full bg-muted"
                      aria-hidden="true"
                    >
                      <div
                        class="h-full rounded-full bg-primary"
                        [style.width.%]="p.portfolioWeight * 100"
                      ></div>
                    </div>
                  </td>
                </tr>
                @if (expanded() === p.securityId) {
                  <tr hlmTr class="bg-muted/40 text-xs">
                    <td hlmTd colspan="5" class="whitespace-normal">
                      <div class="grid gap-2 pl-5 sm:grid-cols-3">
                        <div>
                          <span class="text-muted-foreground">ISIN</span> {{ p.isin ?? '—' }}
                        </div>
                        <div>
                          <span class="text-muted-foreground">{{
                            'portfolio.avgPrice' | translate
                          }}</span>
                          <span class="num"> {{ p.averagePrice | money: p.currency }}</span>
                        </div>
                        <div>
                          <span class="text-muted-foreground">{{
                            'portfolio.invested' | translate
                          }}</span>
                          <span class="num"> {{ p.costBase | money }}</span>
                        </div>
                        @for (h of p.holdings; track h.accountId) {
                          <div class="sm:col-span-3">
                            <span class="badge bg-primary/10 text-primary">{{
                              h.accountName
                            }}</span>
                            <span class="text-muted-foreground">
                              {{ 'source.' + h.broker | translate }} ·</span
                            >
                            <span class="num">
                              {{ h.quantity }} @ {{ h.averagePrice | money: p.currency }}</span
                            >
                          </div>
                        }
                      </div>
                    </td>
                  </tr>
                }
              } @empty {
                <tr hlmTr>
                  <td hlmTd colspan="5" class="py-8 text-center text-muted-foreground">
                    {{ 'portfolio.noPositions' | translate }}
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </section>

        <section class="mt-6 grid gap-4 lg:grid-cols-2">
          <div class="card">
            <h2 class="card-title">{{ 'portfolio.dividendsByMonth' | translate }}</h2>
            <app-chart class="h-64" [option]="dividendChart()" />
          </div>
          <div class="card overflow-x-auto !p-0">
            <h2 class="card-title px-5 pt-5">{{ 'portfolio.recentDividends' | translate }}</h2>
            <table hlmTable>
              <thead hlmTHead>
                <tr hlmTr>
                  <th hlmTh>{{ 'tx.date' | translate }}</th>
                  <th hlmTh></th>
                  <th hlmTh class="text-right">{{ 'portfolio.gross' | translate }}</th>
                  <th hlmTh class="text-right">{{ 'portfolio.withholding' | translate }}</th>
                  <th hlmTh class="text-right">{{ 'portfolio.net' | translate }}</th>
                </tr>
              </thead>
              <tbody hlmTBody>
                @for (d of (dividends.value()?.items ?? []).slice(0, 10); track $index) {
                  <tr hlmTr>
                    <td hlmTd class="text-muted-foreground">{{ d.paidOn | day: 'short' }}</td>
                    <td hlmTd class="font-medium">{{ d.symbol }}</td>
                    <td hlmTd class="num text-right">{{ d.gross | money: d.currency }}</td>
                    <td
                      hlmTd
                      class="num text-right text-muted-foreground"
                      [title]="d.withholdingDerived ? ('portfolio.derived' | translate) : ''"
                    >
                      {{ d.withholdingTax | money: d.currency
                      }}{{ d.withholdingDerived ? '*' : '' }}
                    </td>
                    <td hlmTd class="num text-right font-medium">{{ d.netBase | money }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        </section>
      }
    }

    <app-modal
      [open]="targetsOpen()"
      [title]="'portfolio.targetsTitle' | translate"
      (closed)="targetsOpen.set(false)"
    >
      <form class="space-y-3" (submit)="$event.preventDefault(); saveTargets()">
        @for (c of classes; track c) {
          <div class="grid grid-cols-[1fr_7rem] items-center gap-3">
            <label class="text-sm" [for]="'t-' + c">{{ 'assetClass.' + c | translate }}</label>
            <input
              [id]="'t-' + c"
              hlmInput
              class="num text-right"
              inputmode="decimal"
              [value]="targetInputs()[c] ?? ''"
              (input)="setTarget(c, $any($event.target).value)"
              placeholder="0"
            />
          </div>
        }
        <p class="text-xs text-muted-foreground">{{ 'portfolio.targetsHint' | translate }}</p>
        <div class="flex justify-end gap-2">
          <button type="button" hlmBtn variant="outline" (click)="targetsOpen.set(false)">
            {{ 'common.cancel' | translate }}
          </button>
          <button hlmBtn>{{ 'common.save' | translate }}</button>
        </div>
      </form>
    </app-modal>
  `,
})
export class PortfolioComponent {
  private readonly api = inject(Api);
  private readonly events = inject(DataEvents);
  private readonly prefs = inject(Prefs);
  private readonly toasts = inject(Toasts);
  private readonly i18n = inject(TranslateService);
  protected readonly colors = SERIES_COLORS;
  protected readonly ranges: Range[] = ['1Y', '3Y', 'ALL'];
  protected readonly classes: AssetClass[] = [
    'Etf',
    'Stock',
    'Bond',
    'Fund',
    'Crypto',
    'Cash',
    'Other',
  ];

  protected readonly scopeKey = signal('');
  protected readonly range = signal<Range>('ALL');
  protected readonly expanded = signal<string | null>(null);
  private readonly pct = new PercentPipe();
  protected readonly targetsOpen = signal(false);
  protected readonly targetInputs = signal<Partial<Record<AssetClass, string>>>({});

  private readonly scope = computed<PortfolioScope>(() => {
    const [kind, value] = this.scopeKey().split(':');
    return kind === 'broker'
      ? { broker: value as Broker }
      : kind === 'account'
        ? { accountId: value }
        : {};
  });
  private readonly key = computed(() => ({ scope: this.scope(), v: this.events.version() }));
  private readonly from = computed(() => {
    const years = this.range() === '1Y' ? 1 : this.range() === '3Y' ? 3 : 0;
    if (!years) return undefined;
    const d = new Date();
    d.setFullYear(d.getFullYear() - years);
    return d.toISOString().slice(0, 10);
  });

  protected readonly summary = liveResource({
    params: this.key,
    stream: ({ params }) => this.api.portfolioSummary(params.scope),
  });
  protected readonly positions = liveResource({
    params: this.key,
    stream: ({ params }) => this.api.positions(params.scope),
  });
  protected readonly allocation = liveResource({
    params: this.key,
    stream: ({ params }) => this.api.allocation(params.scope),
  });
  protected readonly dividends = liveResource({
    params: this.key,
    stream: ({ params }) => this.api.dividends(params.scope),
  });
  protected readonly performance = liveResource({
    params: () => ({ ...this.key(), from: this.from() }),
    stream: ({ params }) => this.api.performance(params.scope, params.from),
  });

  /** Every broker account regardless of the selected scope, so the scope picker doesn't shrink. */
  private readonly allAccounts = liveResource({
    params: () => this.events.version(),
    stream: () => this.api.portfolioSummary(),
  });
  protected readonly brokers = computed(() => [
    ...new Set((this.allAccounts.value()?.accounts ?? []).map((a) => a.broker)),
  ]);
  protected readonly scopeOptions = computed<SelectOption[]>(() => {
    this.prefs.translations();
    // Offer only scopes that differ: a broker when there is more than one, and an account only when its
    // broker has several (otherwise "Trading 212" would appear twice and show the same numbers).
    const accounts = this.allAccounts.value()?.accounts ?? [];
    const brokers = this.brokers();
    return [
      { value: '', label: this.i18n.instant('portfolio.consolidated') },
      ...(brokers.length > 1
        ? brokers.map((b) => ({ value: `broker:${b}`, label: this.i18n.instant(`source.${b}`) }))
        : []),
      ...accounts
        .filter((a) => accounts.filter((x) => x.broker === a.broker).length > 1)
        .map((a) => ({
          value: `account:${a.accountId}`,
          label: `${this.i18n.instant(`source.${a.broker}`)} · ${a.name}`,
        })),
    ];
  });
  protected classColor = (c: AssetClass) => CLASS_COLORS[c];

  protected readonly valueChart = computed<EChartsOption>(() => {
    const series = this.performance.value()?.series ?? [];
    this.prefs.translations();
    return {
      ...baseChart,
      tooltip: moneyTooltip(this.prefs.locale()),
      xAxis: {
        type: 'time',
        minInterval: 86_400_000,
        axisLabel: { color: '#a1a1aa', fontSize: 11 },
        splitLine: { show: false },
      },
      yAxis: moneyAxis(this.prefs.locale()),
      series: [
        {
          name: this.i18n.instant('portfolio.totalValue'),
          type: 'line',
          showSymbol: false,
          areaStyle: { opacity: 0.1 },
          data: series.map((p) => [p.date, p.value]),
          itemStyle: { color: SERIES_COLORS.net },
        },
        {
          name: this.i18n.instant('portfolio.contributions'),
          type: 'line',
          showSymbol: false,
          step: 'end',
          data: series.map((p) => [p.date, p.netContributions]),
          itemStyle: { color: '#a1a1aa' },
          lineStyle: { type: 'dashed' },
        },
      ],
    };
  });

  protected readonly allocationChart = computed<EChartsOption>(() => {
    const lines = this.allocation.value() ?? [];
    this.prefs.translations();
    return {
      tooltip: {
        trigger: 'item',
        valueFormatter: (v: unknown) =>
          new Intl.NumberFormat(this.prefs.locale(), { style: 'currency', currency: 'EUR' }).format(
            v as number,
          ),
      },
      series: [
        {
          type: 'pie',
          radius: ['55%', '85%'],
          label: { show: false },
          itemStyle: { borderWidth: 2, borderColor: 'transparent' },
          data: lines
            .filter((l) => l.value > 0)
            .map((l) => ({
              name: this.i18n.instant('assetClass.' + l.assetClass),
              value: l.value,
              itemStyle: { color: CLASS_COLORS[l.assetClass] },
            })),
        },
      ],
    };
  });

  protected readonly dividendChart = computed<EChartsOption>(() => {
    const months = (this.dividends.value()?.byMonth ?? []).slice(-24);
    return {
      ...baseChart,
      legend: undefined,
      tooltip: moneyTooltip(this.prefs.locale()),
      xAxis: categoryAxis(months.map((m) => m.period)),
      yAxis: moneyAxis(this.prefs.locale()),
      series: [
        {
          type: 'bar',
          data: months.map((m) => m.amount),
          itemStyle: { color: SERIES_COLORS.income, borderRadius: [4, 4, 0, 0] },
        },
      ],
    };
  });

  protected readonly sortColumns: { key: SortKey; label: string }[] = [
    { key: 'value', label: 'portfolio.marketValue' },
    { key: 'gain', label: 'portfolio.gain' },
    { key: 'today', label: 'portfolio.today' },
    { key: 'weight', label: 'portfolio.weight' },
  ];
  protected readonly sortKey = signal<SortKey>('value');
  protected readonly sortDesc = signal(true);
  protected readonly sortedPositions = computed(() => {
    const key = this.sortKey();
    const dir = this.sortDesc() ? -1 : 1;
    const pick = (p: PositionLine): number =>
      key === 'gain'
        ? (p.unrealizedPnlPercent ?? 0)
        : key === 'today'
          ? (p.dayChangePercent ?? 0)
          : key === 'weight'
            ? p.portfolioWeight
            : p.marketValueBase;
    return [...(this.positions.value() ?? [])].sort((a, b) => (pick(a) - pick(b)) * dir);
  });

  protected sortBy(key: SortKey) {
    if (this.sortKey() === key) {
      this.sortDesc.update((d) => !d);
    } else {
      this.sortKey.set(key);
      this.sortDesc.set(true);
    }
  }

  protected tone(value: number | null | undefined): string {
    if (value === null || value === undefined || value === 0) return '';
    return value > 0
      ? 'text-emerald-700 dark:text-emerald-400'
      : 'text-rose-600 dark:text-rose-400';
  }

  protected signedPct(value: number | null | undefined): string {
    if (value === null || value === undefined) return '—';
    const text = this.pct.transform(Math.abs(value));
    return value > 0 ? `+${text}` : value < 0 ? `−${text}` : text;
  }

  protected toggle(id: string) {
    this.expanded.set(this.expanded() === id ? null : id);
  }

  protected async openTargets() {
    const current = await firstValueFrom(this.api.targets());
    this.targetInputs.set(
      Object.fromEntries(current.map((t) => [t.assetClass, String(+(t.percent * 100).toFixed(2))])),
    );
    this.targetsOpen.set(true);
  }

  protected setTarget(c: AssetClass, value: string) {
    this.targetInputs.update((t) => ({ ...t, [c]: value }));
  }

  protected async saveTargets() {
    const items = Object.entries(this.targetInputs())
      .map(([assetClass, v]) => ({
        assetClass: assetClass as AssetClass,
        percent: Number((v ?? '').replace(',', '.')) / 100,
      }))
      .filter((i) => i.percent > 0);
    try {
      await firstValueFrom(this.api.saveTargets(items));
      this.targetsOpen.set(false);
      this.events.bump();
    } catch (err) {
      this.toasts.error(err);
    }
  }
}
