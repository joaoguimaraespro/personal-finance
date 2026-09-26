import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import type { EChartsOption } from 'echarts';
import { firstValueFrom } from 'rxjs';
import { Api, PortfolioScope } from '../../core/api';
import { DataEvents } from '../../core/data-events';
import { DayPipe, MoneyPipe, PercentPipe } from '../../core/format';
import { AssetClass, Broker } from '../../core/models';
import { Prefs } from '../../core/prefs';
import { Toasts } from '../../core/toast';
import { ChartComponent } from '../../shared/chart';
import { SERIES_COLORS, baseChart, categoryAxis, moneyAxis, moneyTooltip } from '../../shared/chart-options';
import { KpiComponent } from '../../shared/kpi';
import { ModalComponent } from '../../shared/modal';

const CLASS_COLORS: Record<AssetClass, string> = {
  Stock: '#6366f1',
  Etf: '#2f6fed',
  Bond: '#0891b2',
  Fund: '#14b8a6',
  Crypto: '#f59e0b',
  Cash: '#94a3b8',
  Other: '#a855f7',
};

type Range = '1Y' | '3Y' | 'ALL';

/** Read-only view of broker data: nothing on this page can change a broker account. */
@Component({
  selector: 'app-portfolio',
  imports: [KpiComponent, ChartComponent, ModalComponent, TranslatePipe, MoneyPipe, PercentPipe, DayPipe, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mb-6 flex flex-wrap items-center justify-between gap-3">
      <div>
        <h1 class="text-2xl font-semibold tracking-tight">{{ 'nav.portfolio' | translate }}</h1>
        <p class="text-sm text-slate-500">
          {{ 'portfolio.readOnly' | translate }}
          @if (summary.value()?.lastSyncUtc; as synced) { · {{ 'portfolio.lastSync' | translate }} {{ synced | day }} }
        </p>
      </div>
      <select class="input !w-auto" [value]="scopeKey()" (change)="scopeKey.set($any($event.target).value)" [attr.aria-label]="'portfolio.scope' | translate">
        <option value="">{{ 'portfolio.consolidated' | translate }}</option>
        @for (b of brokers(); track b) { <option [value]="'broker:' + b">{{ 'source.' + b | translate }}</option> }
        @for (a of summary.value()?.accounts ?? []; track a.accountId) { <option [value]="'account:' + a.accountId">{{ a.name }}</option> }
      </select>
    </div>

    @if (summary.value(); as s) {
      @if (s.positions === 0 && !s.accounts.length) {
        <section class="card py-12 text-center">
          <p class="text-slate-500">{{ 'portfolio.empty' | translate }}</p>
          <a routerLink="/connections" class="btn btn-primary mt-4">{{ 'portfolio.connect' | translate }}</a>
        </section>
      } @else {
        <section class="grid grid-cols-2 gap-3 md:grid-cols-4">
          <app-kpi [label]="'portfolio.totalValue' | translate" [value]="s.totalValue" [color]="colors.net" />
          <app-kpi [label]="'portfolio.contributions' | translate" [value]="s.netContributions" color="#64748b" />
          <app-kpi [label]="'portfolio.totalReturn' | translate" [value]="s.totalReturn" [color]="s.totalReturn >= 0 ? colors.income : colors.expenses" />
          <app-kpi [label]="'portfolio.totalReturnPct' | translate" [value]="s.totalReturnPercent" kind="percent" color="#f59e0b" />
          <app-kpi [label]="'portfolio.unrealized' | translate" [value]="s.unrealizedPnl" [color]="colors.invested" />
          <app-kpi [label]="'portfolio.realized' | translate" [value]="s.realizedPnl" [color]="colors.invested" />
          <app-kpi [label]="'portfolio.dividends' | translate" [value]="s.dividends" [color]="colors.income" />
          <app-kpi [label]="'portfolio.fees' | translate" [value]="s.fees" [color]="colors.expenses" />
        </section>

        <section class="mt-6 grid gap-4 lg:grid-cols-[2fr_1fr]">
          <div class="card">
            <div class="mb-4 flex flex-wrap items-center justify-between gap-3">
              <h2 class="card-title !mb-0">{{ 'portfolio.performance' | translate }}</h2>
              <div class="flex items-center gap-4">
                @if (performance.value(); as p) {
                  <span class="text-xs text-slate-500" [title]="'portfolio.twrHint' | translate">TWR <b class="num text-slate-900 dark:text-white">{{ p.timeWeightedReturn | pct }}</b></span>
                  <span class="text-xs text-slate-500" [title]="'portfolio.xirrHint' | translate">XIRR <b class="num text-slate-900 dark:text-white">{{ p.moneyWeightedReturn | pct }}</b></span>
                }
                <div class="segmented">
                  @for (r of ranges; track r) { <button [class.active]="range() === r" (click)="range.set(r)">{{ r }}</button> }
                </div>
              </div>
            </div>
            <app-chart class="h-72" [option]="valueChart()" />
          </div>
          <div class="card">
            <div class="mb-4 flex items-center justify-between">
              <h2 class="card-title !mb-0">{{ 'portfolio.allocation' | translate }}</h2>
              <button class="text-xs text-brand-600" (click)="openTargets()">{{ 'portfolio.editTargets' | translate }}</button>
            </div>
            <app-chart class="h-44" [option]="allocationChart()" />
            <table class="table mt-2">
              <thead><tr><th></th><th class="text-right">{{ 'portfolio.actual' | translate }}</th><th class="text-right">{{ 'portfolio.target' | translate }}</th><th class="text-right">Δ</th></tr></thead>
              <tbody>
                @for (a of allocation.value() ?? []; track a.assetClass) {
                  <tr>
                    <td><span class="mr-2 inline-block h-2 w-2 rounded-full" [style.background]="classColor(a.assetClass)"></span>{{ 'assetClass.' + a.assetClass | translate }}</td>
                    <td class="num text-right">{{ a.actual | pct }}</td>
                    <td class="num text-right text-slate-500">{{ a.target | pct }}</td>
                    <td class="num text-right" [class.text-rose-600]="(a.difference ?? 0) < -0.02" [class.text-emerald-600]="(a.difference ?? 0) > 0.02">{{ a.difference === null ? '' : ((a.difference > 0 ? '+' : '') + (a.difference * 100).toFixed(1) + ' pp') }}</td>
                  </tr>
                }
              </tbody>
            </table>
            <p class="mt-2 text-[11px] text-slate-400">{{ 'portfolio.noRebalance' | translate }}</p>
          </div>
        </section>

        <section class="card mt-6 overflow-x-auto !p-0">
          <h2 class="card-title px-5 pt-5">{{ 'portfolio.positions' | translate }}</h2>
          <table class="table whitespace-nowrap">
            <thead>
              <tr>
                <th>{{ 'portfolio.security' | translate }}</th>
                <th>{{ 'portfolio.broker' | translate }}</th>
                <th class="text-right">{{ 'portfolio.quantity' | translate }}</th>
                <th class="text-right">{{ 'portfolio.avgPrice' | translate }}</th>
                <th class="text-right">{{ 'portfolio.price' | translate }}</th>
                <th class="text-right">{{ 'portfolio.marketValue' | translate }}</th>
                <th class="text-right">P&amp;L</th>
                <th class="text-right">P&amp;L %</th>
                <th class="text-right">{{ 'portfolio.weight' | translate }}</th>
              </tr>
            </thead>
            <tbody>
              @for (p of positions.value() ?? []; track p.securityId) {
                <tr class="cursor-pointer hover:bg-slate-50 dark:hover:bg-slate-800/40" (click)="toggle(p.securityId)">
                  <td>
                    <div class="font-medium">{{ p.symbol }} <span class="badge ml-1 bg-slate-100 !px-1.5 !py-0 text-[10px] dark:bg-slate-800">{{ 'assetClass.' + p.assetClass | translate }}</span></div>
                    <div class="max-w-64 truncate text-xs text-slate-400">{{ p.name }} · {{ p.isin }}</div>
                  </td>
                  <td class="text-xs">
                    @for (h of p.holdings; track h.accountId) { <span class="badge mr-1 bg-brand-50 text-brand-700 dark:bg-brand-500/15 dark:text-brand-100">{{ h.accountName }}</span> }
                  </td>
                  <td class="num text-right">{{ p.quantity }}</td>
                  <td class="num text-right text-slate-500">{{ p.averagePrice | money: p.currency }}</td>
                  <td class="num text-right">{{ p.lastPrice | money: p.currency }}</td>
                  <td class="num text-right font-medium">{{ p.marketValueBase | money }}</td>
                  <td class="num text-right" [class]="p.unrealizedPnlBase >= 0 ? 'text-emerald-600' : 'text-rose-600'">{{ p.unrealizedPnlBase | money: 'EUR' : true }}</td>
                  <td class="num text-right" [class]="(p.unrealizedPnlPercent ?? 0) >= 0 ? 'text-emerald-600' : 'text-rose-600'">{{ p.unrealizedPnlPercent | pct }}</td>
                  <td class="num text-right">{{ p.portfolioWeight | pct }}</td>
                </tr>
                @if (expanded() === p.securityId) {
                  @for (h of p.holdings; track h.accountId) {
                    <tr class="bg-slate-50/70 text-xs dark:bg-slate-800/30">
                      <td class="pl-8 text-slate-500">↳ {{ h.accountName }}</td>
                      <td>{{ 'source.' + h.broker | translate }}</td>
                      <td class="num text-right">{{ h.quantity }}</td>
                      <td class="num text-right text-slate-500">{{ h.averagePrice | money: p.currency }}</td>
                      <td colspan="5"></td>
                    </tr>
                  }
                }
              } @empty {
                <tr><td colspan="9" class="py-8 text-center text-slate-400">{{ 'portfolio.noPositions' | translate }}</td></tr>
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
            <table class="table">
              <thead><tr><th>{{ 'tx.date' | translate }}</th><th></th><th class="text-right">{{ 'portfolio.gross' | translate }}</th><th class="text-right">{{ 'portfolio.withholding' | translate }}</th><th class="text-right">{{ 'portfolio.net' | translate }}</th></tr></thead>
              <tbody>
                @for (d of (dividends.value()?.items ?? []).slice(0, 10); track $index) {
                  <tr>
                    <td class="text-slate-500">{{ d.paidOn | day: 'short' }}</td>
                    <td class="font-medium">{{ d.symbol }}</td>
                    <td class="num text-right">{{ d.gross | money: d.currency }}</td>
                    <td class="num text-right text-slate-500" [title]="d.withholdingDerived ? ('portfolio.derived' | translate) : ''">{{ d.withholdingTax | money: d.currency }}{{ d.withholdingDerived ? '*' : '' }}</td>
                    <td class="num text-right font-medium">{{ d.netBase | money }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        </section>
      }
    }

    <app-modal [open]="targetsOpen()" [title]="'portfolio.targetsTitle' | translate" (closed)="targetsOpen.set(false)">
      <form class="space-y-3" (submit)="$event.preventDefault(); saveTargets()">
        @for (c of classes; track c) {
          <div class="grid grid-cols-[1fr_7rem] items-center gap-3">
            <label class="text-sm" [for]="'t-' + c">{{ 'assetClass.' + c | translate }}</label>
            <input [id]="'t-' + c" class="input num text-right" inputmode="decimal" [value]="targetInputs()[c] ?? ''" (input)="setTarget(c, $any($event.target).value)" placeholder="0" />
          </div>
        }
        <p class="text-xs text-slate-500">{{ 'portfolio.targetsHint' | translate }}</p>
        <div class="flex justify-end gap-2">
          <button type="button" class="btn" (click)="targetsOpen.set(false)">{{ 'common.cancel' | translate }}</button>
          <button class="btn btn-primary">{{ 'common.save' | translate }}</button>
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
  protected readonly classes: AssetClass[] = ['Etf', 'Stock', 'Bond', 'Fund', 'Crypto', 'Cash', 'Other'];

  protected readonly scopeKey = signal('');
  protected readonly range = signal<Range>('ALL');
  protected readonly expanded = signal<string | null>(null);
  protected readonly targetsOpen = signal(false);
  protected readonly targetInputs = signal<Partial<Record<AssetClass, string>>>({});

  private readonly scope = computed<PortfolioScope>(() => {
    const [kind, value] = this.scopeKey().split(':');
    return kind === 'broker' ? { broker: value as Broker } : kind === 'account' ? { accountId: value } : {};
  });
  private readonly key = computed(() => ({ scope: this.scope(), v: this.events.version() }));
  private readonly from = computed(() => {
    const years = this.range() === '1Y' ? 1 : this.range() === '3Y' ? 3 : 0;
    if (!years) return undefined;
    const d = new Date();
    d.setFullYear(d.getFullYear() - years);
    return d.toISOString().slice(0, 10);
  });

  protected readonly summary = rxResource({ params: this.key, stream: ({ params }) => this.api.portfolioSummary(params.scope) });
  protected readonly positions = rxResource({ params: this.key, stream: ({ params }) => this.api.positions(params.scope) });
  protected readonly allocation = rxResource({ params: this.key, stream: ({ params }) => this.api.allocation(params.scope) });
  protected readonly dividends = rxResource({ params: this.key, stream: ({ params }) => this.api.dividends(params.scope) });
  protected readonly performance = rxResource({
    params: () => ({ ...this.key(), from: this.from() }),
    stream: ({ params }) => this.api.performance(params.scope, params.from),
  });

  protected readonly brokers = computed(() => [...new Set((this.summary.value()?.accounts ?? []).map((a) => a.broker))]);
  protected classColor = (c: AssetClass) => CLASS_COLORS[c];

  protected readonly valueChart = computed<EChartsOption>(() => {
    const series = this.performance.value()?.series ?? [];
    this.prefs.translations();
    return {
      ...baseChart,
      tooltip: moneyTooltip(this.prefs.locale()),
      xAxis: { type: 'time', minInterval: 86_400_000, axisLabel: { color: '#94a3b8', fontSize: 11 }, splitLine: { show: false } },
      yAxis: moneyAxis(this.prefs.locale()),
      series: [
        { name: this.i18n.instant('portfolio.totalValue'), type: 'line', showSymbol: false, areaStyle: { opacity: 0.1 }, data: series.map((p) => [p.date, p.value]), itemStyle: { color: SERIES_COLORS.net } },
        { name: this.i18n.instant('portfolio.contributions'), type: 'line', showSymbol: false, step: 'end', data: series.map((p) => [p.date, p.netContributions]), itemStyle: { color: '#94a3b8' }, lineStyle: { type: 'dashed' } },
      ],
    };
  });

  protected readonly allocationChart = computed<EChartsOption>(() => {
    const lines = this.allocation.value() ?? [];
    this.prefs.translations();
    return {
      tooltip: { trigger: 'item', valueFormatter: (v: unknown) => new Intl.NumberFormat(this.prefs.locale(), { style: 'currency', currency: 'EUR' }).format(v as number) },
      series: [
        { type: 'pie', radius: ['55%', '85%'], label: { show: false }, itemStyle: { borderWidth: 2, borderColor: 'transparent' },
          data: lines.filter((l) => l.value > 0).map((l) => ({ name: this.i18n.instant('assetClass.' + l.assetClass), value: l.value, itemStyle: { color: CLASS_COLORS[l.assetClass] } })) },
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
      series: [{ type: 'bar', data: months.map((m) => m.amount), itemStyle: { color: SERIES_COLORS.income, borderRadius: [4, 4, 0, 0] } }],
    };
  });

  protected toggle(id: string) {
    this.expanded.set(this.expanded() === id ? null : id);
  }

  protected async openTargets() {
    const current = await firstValueFrom(this.api.targets());
    this.targetInputs.set(Object.fromEntries(current.map((t) => [t.assetClass, String(+(t.percent * 100).toFixed(2))])));
    this.targetsOpen.set(true);
  }

  protected setTarget(c: AssetClass, value: string) {
    this.targetInputs.update((t) => ({ ...t, [c]: value }));
  }

  protected async saveTargets() {
    const items = Object.entries(this.targetInputs())
      .map(([assetClass, v]) => ({ assetClass: assetClass as AssetClass, percent: Number((v ?? '').replace(',', '.')) / 100 }))
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
