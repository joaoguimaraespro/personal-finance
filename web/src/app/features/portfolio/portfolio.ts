import { displayFx, formatMoney } from '../../core/display-currency';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { liveResource } from '../../core/resource';
import { RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import type { EChartsOption } from 'echarts';
import { firstValueFrom } from 'rxjs';
import { Api, PortfolioScope } from '../../core/api';
import { DataEvents, QuickAdd } from '../../core/data-events';
import { brokerMovementPrefill } from '../../core/notifications';
import { DayPipe, MoneyPipe, PercentPipe, PricePipe } from '../../core/format';
import {
  AssetClass,
  BrokerMovement,
  Broker,
  DayChangeBasis,
  ManualHolding,
  PeriodReturn,
  PortfolioSummary,
  PositionLine,
  DividendSummary,
} from '../../core/models';
import { Prefs } from '../../core/prefs';
import { Toasts } from '../../core/toast';
import { ChartComponent } from '../../shared/chart';
import {
  SERIES_COLORS,
  ASSET_CLASS_COLORS,
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
import { NgIcon, provideIcons } from '@ng-icons/core';
import { APP_ICONS } from '../../shared/icons';
import { BrokerLogoComponent } from '../../shared/broker-logo';
import { CoinIconComponent } from '../../shared/coin-icon';
import { SecurityIconComponent } from '../../shared/security-icon';
import { CryptoDialogComponent } from './crypto-dialog';
import { WalletStripComponent } from './wallet-strip';
import {
  Label,
  RETURN_PERIODS,
  dayChangeHint,
  dayChangeLabel,
  returnHint,
  returnLabel,
  incomeMonths,
} from './periods';
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

const CLASS_COLORS: Record<AssetClass, string> = ASSET_CLASS_COLORS;

/**
 * Read-only view of broker data: nothing on this page can change a broker account. Coins held elsewhere (an
 * exchange account, a cold wallet) are entered by hand here and priced from public quotes.
 */
/** A row of the recent-income table: one dividend, or one coin's exchange rewards of a month. */
type RecentIncome = DividendSummary['items'][number] & { month: boolean; count: number };

@Component({
  selector: 'app-portfolio',
  imports: [
    BrokerLogoComponent,
    CoinIconComponent,
    SecurityIconComponent,
    CryptoDialogComponent,
    WalletStripComponent,
    PricePipe,
    NgIcon,
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
    APP_ICONS,
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
      <div class="flex flex-wrap items-center gap-2">
        <!-- One control for the period of every return on the page and of the value chart. -->
        <div class="segmented" role="group" [attr.aria-label]="'portfolio.period' | translate">
          @for (p of periods; track p) {
            <button
              type="button"
              [class.active]="period() === p"
              [attr.aria-pressed]="period() === p"
              [attr.data-period]="p"
              (click)="prefs.portfolioPeriod.set(p)"
            >
              {{ 'periods.' + p | translate }}
            </button>
          }
        </div>
        <button hlmBtn variant="outline" (click)="openCrypto(null)">
          <ng-icon name="lucidePlus" aria-hidden="true" />{{ 'crypto.addButton' | translate }}
        </button>
      </div>
    </div>

    @if (allAccounts.value(); as all) {
      @if (all.accounts.length > 1) {
        <app-wallet-strip
          class="mb-4"
          [summary]="all"
          [selected]="walletId()"
          (selectedChange)="walletId.set($event)"
        />
      }
    }

    @if (summary.value(); as s) {
      @if (s.positions === 0 && !s.accounts.length) {
        <section class="card py-12 text-center">
          <p class="text-muted-foreground">{{ 'portfolio.empty' | translate }}</p>
          <div class="mt-4 flex flex-wrap justify-center gap-2">
            <a routerLink="/connections" hlmBtn>{{ 'portfolio.connect' | translate }}</a>
            <button hlmBtn variant="outline" (click)="openCrypto(null)">
              <ng-icon name="lucideCoins" aria-hidden="true" />{{ 'crypto.addButton' | translate }}
            </button>
          </div>
        </section>
      } @else {
        <section class="card mb-4 grid gap-5 sm:grid-cols-3">
          <div>
            <p class="text-xs text-muted-foreground">
              {{ 'portfolio.totalValue' | translate }}
              @if (selectedWallet(); as w) {
                · <span class="font-medium text-foreground">{{ w.name }}</span>
              }
            </p>
            <p class="num mt-1 text-3xl font-semibold tracking-tight">{{ s.totalValue | money }}</p>
          </div>
          <div>
            <p class="text-xs text-muted-foreground">
              <span
                [class.hint]="dayHint(s.dayChangeBasis)"
                [attr.title]="
                  dayHint(s.dayChangeBasis) ? (dayHint(s.dayChangeBasis)! | translate) : null
                "
                >{{ dayLabel(s.dayChangeBasis) | translate }}</span
              >
            </p>
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
              <!-- No change before a second day of data: a dash, with the reason on hover. -->
              <p
                class="num mt-1 cursor-help text-2xl font-semibold text-muted-foreground"
                [attr.title]="'portfolio.todayPending' | translate"
                [attr.aria-label]="'portfolio.todayPending' | translate"
              >
                —
              </p>
            }
          </div>
          <div>
            @let r = heroReturn();
            <p class="text-xs text-muted-foreground" data-testid="hero-return-label">
              <span class="hint" [title]="returnHint(r) | translate">{{
                label(r).key | translate: label(r).params
              }}</span>
            </p>
            @if (r.gain !== null && r.gain !== undefined) {
              <p
                class="num mt-1 flex items-center gap-1.5 text-xl font-semibold"
                [class]="tone(r.gain)"
              >
                <ng-icon
                  [name]="r.gain >= 0 ? 'lucideTrendingUp' : 'lucideTrendingDown'"
                  aria-hidden="true"
                />
                {{ r.gain | money: 'EUR' : true }}
                <span class="text-sm font-medium">({{ signedPct(r.percent) }})</span>
              </p>
            } @else {
              <p class="mt-1 text-sm text-muted-foreground">
                {{ 'portfolio.returnPending' | translate }}
              </p>
            }
          </div>
        </section>

        <!-- Gains differ from the brokers' own apps on purpose: say why once, where the figures are. -->
        <p
          class="text-muted-foreground -mt-2 mb-4 flex items-start gap-1.5 text-xs"
          data-testid="cost-note"
        >
          <ng-icon name="lucideInfo" class="mt-px shrink-0" aria-hidden="true" />{{
            'portfolio.costNote' | translate
          }}
        </p>
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
            [label]="
              (hasCrypto() ? 'crypto.dividendsAndRewards' : 'portfolio.dividends') | translate
            "
            [value]="s.dividends"
            [color]="colors.income"
          />
          <app-kpi
            [label]="'portfolio.fees' | translate"
            [value]="s.fees"
            [color]="colors.expenses"
          />
        </section>

        <!-- Money at the brokers not invested yet: the total, and where it sits. -->
        @if (s.cash) {
          <section class="card mt-4" data-testid="cash-card">
            <div class="flex flex-wrap items-baseline justify-between gap-x-4 gap-y-1">
              <h2 class="card-title !mb-0">
                <ng-icon name="lucideWallet" aria-hidden="true" />{{
                  'portfolio.cash.title' | translate
                }}
              </h2>
              <p class="num text-xl font-semibold">
                {{ s.cash | money }}
                @if (s.totalValue > 0) {
                  <span class="text-muted-foreground text-xs font-normal">{{
                    'portfolio.cash.share' | translate: { share: (s.cash / s.totalValue | pct: 0) }
                  }}</span>
                }
              </p>
            </div>
            <p class="text-muted-foreground mt-1 text-xs">
              {{ 'portfolio.cash.hint' | translate }}
            </p>
            @if (cashAccounts().length > 1) {
              <ul class="mt-3 grid gap-2 sm:grid-cols-2 lg:grid-cols-3">
                @for (a of cashAccounts(); track a.accountId) {
                  <li class="border-border flex items-center gap-2 rounded-md border px-3 py-2">
                    <app-broker-logo [broker]="a.broker" [name]="a.name" [size]="20" />
                    <span class="min-w-0 flex-1 truncate text-sm">{{ a.name }}</span>
                    <span class="num text-sm font-medium">{{ a.cash | money }}</span>
                  </li>
                }
              </ul>
            }
          </section>
        }

        <section class="mt-6 grid gap-4 lg:grid-cols-[2fr_1fr]">
          <!-- The chart takes whatever height the allocation card next to it needs. -->
          <div class="card flex flex-col">
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
                  <span class="badge bg-muted text-muted-foreground">{{
                    'periods.' + period() | translate
                  }}</span>
                }
              </div>
            </div>
            @if (hasHistory()) {
              <app-chart class="min-h-72 flex-1" [option]="valueChart()" />
              @if (performance.value()?.reconstructedBefore; as before) {
                <p class="mt-2 text-xs text-muted-foreground">
                  {{ 'portfolio.reconstructedNote' | translate: { date: dayMonth(before) } }}
                  @if (performance.value()?.estimatedDays) {
                    {{ 'portfolio.reconstructedEstimated' | translate }}
                  }
                </p>
              }
            } @else {
              <div
                class="flex h-72 flex-col items-center justify-center gap-2 px-6 text-center text-sm text-muted-foreground"
              >
                <p class="font-medium text-foreground">
                  {{ 'portfolio.historyEmptyTitle' | translate }}
                </p>
                <p class="max-w-md">{{ 'portfolio.historyEmpty' | translate }}</p>
              </div>
            }
          </div>
          <div class="card">
            <div class="mb-4 flex items-center justify-between">
              <h2 class="card-title !mb-0">
                <ng-icon name="lucideChartPie" />{{ 'portfolio.allocation' | translate }}
              </h2>
              <button hlmBtn variant="ghost" size="sm" class="text-primary" (click)="openTargets()">
                <ng-icon name="lucideTarget" aria-hidden="true" />{{
                  'portfolio.editTargets' | translate
                }}
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
                      {{ col.label | translate: { currency: displayCurrency() } }}
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
                      @if (p.assetClass === 'Crypto') {
                        <app-coin-icon [symbol]="p.symbol" [size]="18" />
                      } @else {
                        <app-security-icon
                          [securityId]="p.securityId"
                          [symbol]="p.symbol"
                          [assetClass]="p.assetClass"
                          [size]="18"
                        />
                      }
                      {{ p.symbol }}
                      @if (p.assetClass !== 'Crypto') {
                        <span class="badge bg-muted !px-1.5 !py-0 text-[10px]">{{
                          'assetClass.' + p.assetClass | translate
                        }}</span>
                      }
                      @if (isManual(p)) {
                        <span
                          class="badge bg-amber-500/10 !px-1.5 !py-0 text-[10px] text-amber-700 dark:text-amber-400"
                          [title]="'crypto.manualHint' | translate"
                          >{{ 'crypto.manualBadge' | translate }}</span
                        >
                      }
                    </div>
                    <div class="max-w-36 truncate pl-5 text-xs text-muted-foreground sm:max-w-56">
                      {{ p.name }}
                      @if (p.assetClass === 'Crypto') {
                        · {{ 'assetClass.Crypto' | translate }}
                      }
                    </div>
                    <div class="num pl-5 text-xs font-medium sm:hidden">
                      {{ p.marketValueBase | money }}
                    </div>
                  </td>
                  <td hlmTd class="hidden text-right sm:table-cell">
                    <div class="num font-medium">{{ p.marketValueBase | money }}</div>
                    <div class="num text-xs text-muted-foreground">
                      {{ p.quantity }} × {{ p.lastPrice | price: p.currency }}
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
                      <div class="num text-xs">
                        @if (p.dayChangeBasis === 'Rolling24Hours') {
                          <span
                            class="text-muted-foreground"
                            data-testid="change-24h"
                            [title]="'portfolio.change24hHint' | translate"
                            >{{ 'portfolio.change24h' | translate }} ·</span
                          >
                        }
                        {{ signedPct(p.dayChangePercent) }}
                      </div>
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
                          @if (p.isin || !isManual(p)) {
                            <span class="text-muted-foreground">ISIN</span> {{ p.isin ?? '—' }}
                          } @else {
                            <span class="text-muted-foreground">{{
                              'portfolio.price' | translate
                            }}</span>
                            <span class="num"> {{ p.lastPrice | price: p.currency }}</span>
                          }
                        </div>
                        <div>
                          <span class="text-muted-foreground">{{
                            'portfolio.avgPrice' | translate
                          }}</span>
                          <span class="num"> {{ p.averagePrice | price: p.currency }}</span>
                        </div>
                        <div>
                          <span class="text-muted-foreground">{{
                            'portfolio.invested' | translate
                          }}</span>
                          <span class="num"> {{ p.costBase | money }}</span>
                        </div>
                        @for (h of p.holdings; track h.accountId) {
                          <div class="flex flex-wrap items-center gap-1 sm:col-span-3">
                            <app-broker-logo
                              [broker]="h.broker"
                              [name]="h.accountName"
                              [size]="16"
                            />
                            <span class="badge bg-primary/10 text-primary">{{
                              h.accountName
                            }}</span>
                            <span class="text-muted-foreground">
                              {{ brokerLabel(h.broker) }} ·</span
                            >
                            <span class="num">
                              {{ h.quantity }} @ {{ h.averagePrice | price: p.currency }}</span
                            >
                            @if (manualFor(h.accountId, p.securityId); as m) {
                              @if (m.rewardQuantity) {
                                <span class="text-muted-foreground">
                                  ·
                                  {{
                                    'crypto.ofWhichRewards' | translate: { qty: m.rewardQuantity }
                                  }}</span
                                >
                              }
                              <button
                                type="button"
                                hlmBtn
                                variant="ghost"
                                size="sm"
                                class="ml-auto h-7"
                                (click)="$event.stopPropagation(); openCrypto(m)"
                              >
                                <ng-icon name="lucidePencil" aria-hidden="true" />{{
                                  'crypto.editButton' | translate
                                }}
                              </button>
                            }
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
          <p class="px-5 py-3 text-[11px] text-muted-foreground">
            {{ 'portfolio.positionsNote' | translate }}
          </p>
        </section>

        <section class="mt-6 grid gap-4 lg:grid-cols-2">
          <div class="card flex flex-col">
            <h2 class="card-title">
              {{
                (hasCrypto() ? 'crypto.incomeByMonth' : 'portfolio.dividendsByMonth') | translate
              }}
            </h2>
            <app-chart class="min-h-64 flex-1" [option]="dividendChart()" />
          </div>
          <div class="card overflow-x-auto !p-0">
            <h2 class="card-title px-5 pt-5">
              {{ (hasCrypto() ? 'crypto.recentIncome' : 'portfolio.recentDividends') | translate }}
            </h2>
            <table hlmTable>
              <thead hlmTHead>
                <tr hlmTr>
                  <th hlmTh>{{ 'tx.date' | translate }}</th>
                  <th hlmTh></th>
                  <th hlmTh class="hidden text-right sm:table-cell">
                    {{ 'portfolio.gross' | translate }}
                  </th>
                  <th hlmTh class="hidden text-right sm:table-cell">
                    {{ 'portfolio.withholding' | translate }}
                  </th>
                  <th hlmTh class="text-right">
                    {{ 'portfolio.net' | translate: { currency: displayCurrency() } }}
                  </th>
                </tr>
              </thead>
              <tbody hlmTBody>
                @for (d of recentIncome(); track $index) {
                  <tr hlmTr>
                    <td hlmTd class="text-muted-foreground">
                      {{ d.month ? monthLabel(d.paidOn) : (d.paidOn | day: 'short') }}
                    </td>
                    <td hlmTd class="font-medium">
                      @if (d.broker === 'Manual') {
                        <app-coin-icon class="mr-1 align-[-3px]" [symbol]="d.symbol" [size]="16" />
                      }
                      {{ d.symbol }}
                      @if (d.broker === 'Manual' || d.broker === 'Binance') {
                        <span
                          class="badge ml-1 bg-emerald-500/10 !px-1.5 !py-0 text-[10px] text-emerald-700 dark:text-emerald-400"
                          [title]="
                            d.month
                              ? ('crypto.rewardsMonthHint' | translate: { count: d.count })
                              : ''
                          "
                          >{{
                            (d.broker === 'Binance' ? 'crypto.earnBadge' : 'crypto.rewardBadge')
                              | translate
                          }}</span
                        >
                      }
                    </td>
                    <td hlmTd class="num hidden text-right sm:table-cell">
                      {{ d.gross | money: d.currency }}
                    </td>
                    <td
                      hlmTd
                      class="num hidden text-right text-muted-foreground sm:table-cell"
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

        <!-- Dividends by calendar month, every position included: the thin months show where to allocate. -->
        @if (calendar.value(); as cal) {
          @if (cal.rows.length) {
            <section class="card mt-6 overflow-x-auto !p-0" data-testid="dividend-calendar">
              <div class="flex flex-wrap items-baseline justify-between gap-2 px-5 pt-5">
                <h2 class="card-title !mb-0">
                  <ng-icon name="lucideCalendarDays" />{{ 'portfolio.calendar' | translate }}
                </h2>
                <label class="text-muted-foreground hidden items-center sm:flex gap-2 text-xs">
                  <input
                    type="checkbox"
                    [checked]="payersOnly()"
                    (change)="payersOnly.set($any($event.target).checked)"
                  />
                  {{ 'portfolio.payersOnly' | translate }}
                </label>
              </div>
              <p class="text-muted-foreground px-5 pt-1 text-xs">
                {{
                  'portfolio.calendarSummary'
                    | translate
                      : { average: (cal.monthlyAverage | money), total: (cal.total | money) }
                }}
                @if (thinMonths().length) {
                  ·
                  <span class="text-amber-700 dark:text-amber-300">{{
                    'portfolio.thinMonths' | translate: { months: thinMonths().join(', ') }
                  }}</span>
                }
              </p>
              <!-- Phones: one line per month, with who pays in it, instead of a 14-column grid. -->
              <ul
                class="divide-border mt-3 divide-y border-t sm:hidden"
                data-testid="calendar-list"
              >
                @for (m of calendarByMonth(); track m.index) {
                  <li class="px-5 py-2.5">
                    <div class="flex items-baseline justify-between gap-3 text-sm">
                      <span class="font-medium">{{ m.name }}</span>
                      <span
                        class="num font-medium"
                        [class]="m.thin ? 'text-amber-700 dark:text-amber-300' : ''"
                        >{{ m.total | money }}</span
                      >
                    </div>
                    @if (m.payers.length) {
                      <p class="text-muted-foreground mt-0.5 text-xs">
                        @for (p of m.payers; track p.symbol; let last = $last) {
                          {{ p.symbol }} <span class="num">{{ p.value | money }}</span
                          >{{ last ? '' : ' · ' }}
                        }
                      </p>
                    }
                  </li>
                }
              </ul>
              <table hlmTable class="mt-3 hidden text-xs sm:table">
                <thead hlmTHead>
                  <tr hlmTr>
                    <th hlmTh class="min-w-32"></th>
                    @for (m of monthNames(); track $index) {
                      <th hlmTh class="text-right font-medium">{{ m }}</th>
                    }
                    <th hlmTh class="text-right">{{ 'portfolio.calendarTotal' | translate }}</th>
                    <th hlmTh class="text-right">{{ 'portfolio.yield' | translate }}</th>
                  </tr>
                </thead>
                <tbody hlmTBody>
                  @for (r of calendarRows(); track r.securityId) {
                    <tr hlmTr [class.text-muted-foreground]="!r.total">
                      <td hlmTd class="font-medium">
                        <span [title]="r.name">{{ r.symbol }}</span>
                        @if (!r.total) {
                          <span class="badge bg-muted ml-1 !px-1.5 !py-0 text-[10px] font-normal">{{
                            'portfolio.noDividends' | translate
                          }}</span>
                        }
                      </td>
                      @for (v of r.months; track $index) {
                        <td
                          hlmTd
                          class="num text-right"
                          [style.background-color]="v ? heat(v, cal) : null"
                        >
                          {{ v ? (v | money) : '·' }}
                        </td>
                      }
                      <td hlmTd class="num text-right font-medium">
                        {{ r.total ? (r.total | money) : '—' }}
                      </td>
                      <td hlmTd class="num text-right">
                        {{ r.yield ? (r.yield | pct: 1) : '—' }}
                      </td>
                    </tr>
                  }
                  <tr hlmTr class="bg-muted/50 font-medium">
                    <td hlmTd>{{ 'portfolio.calendarTotal' | translate }}</td>
                    @for (v of cal.months; track $index) {
                      <td
                        hlmTd
                        [class]="
                          v < cal.monthlyAverage / 2
                            ? 'num text-right text-amber-700 dark:text-amber-300'
                            : 'num text-right'
                        "
                      >
                        {{ v | money }}
                      </td>
                    }
                    <td hlmTd class="num text-right">{{ cal.total | money }}</td>
                    <td hlmTd></td>
                  </tr>
                </tbody>
              </table>
              <p class="text-muted-foreground px-5 py-3 text-[11px]">
                {{ 'portfolio.calendarHint' | translate }}
              </p>
            </section>
          }
        }
      }
    }

    <!-- Synced deposits and withdrawals: which account the money came from, as the broker named each one. -->
    @if (movements().length) {
      <section class="card mt-6 !p-0" id="broker-movements" data-testid="broker-movements">
        <h2 class="card-title px-5 pt-5 !mb-1">
          <ng-icon name="lucideBanknote" />{{ 'brokerMovements.title' | translate }}
        </h2>
        <p class="text-muted-foreground px-5 text-xs">{{ 'brokerMovements.hint' | translate }}</p>
        <ul class="divide-border mt-3 divide-y">
          @for (m of movements(); track m.id) {
            <li class="flex flex-wrap items-center gap-x-3 gap-y-2 px-5 py-3">
              <div class="min-w-0 flex-1">
                <p class="text-sm font-medium">
                  {{
                    (m.amount > 0 ? 'brokerMovements.depositTo' : 'brokerMovements.withdrawalFrom')
                      | translate: { broker: m.broker }
                  }}
                </p>
                <p class="text-muted-foreground text-xs">
                  {{ m.on | day: 'short' }}
                  @if (m.kind) {
                    · <span class="font-mono text-[10px]">{{ m.kind }}</span>
                  }
                </p>
              </div>
              <span
                class="num text-sm font-medium"
                [class]="m.amount > 0 ? '' : 'text-rose-600 dark:text-rose-400'"
                >{{ m.amount | money: m.currency : true }}</span
              >
              @if (m.state === 'Pending') {
                <span
                  class="badge bg-amber-500/10 !px-1.5 !py-0 text-[10px] text-amber-700 dark:text-amber-400"
                  >{{ 'brokerMovements.state.Pending' | translate }}</span
                >
                <div class="flex w-full gap-2 sm:w-auto">
                  <button hlmBtn size="sm" (click)="recordMovement(m)">
                    <ng-icon name="lucidePlus" />{{ 'allocation.record' | translate }}
                  </button>
                  <button
                    hlmBtn
                    size="sm"
                    variant="outline"
                    [disabled]="ignoring() === m.id"
                    (click)="ignoreMovement(m)"
                  >
                    {{ 'recurring.skip' | translate }}
                  </button>
                </div>
              } @else {
                <span class="badge bg-muted text-muted-foreground !px-1.5 !py-0 text-[10px]">{{
                  'brokerMovements.state.' + m.state | translate
                }}</span>
              }
            </li>
          }
        </ul>
      </section>
    }

    <app-crypto-dialog
      [open]="cryptoOpen()"
      [holding]="editing()"
      [locations]="manual.value()?.locations ?? []"
      [defaultWallet]="selectedWallet()?.broker === 'Manual' ? selectedWallet()!.name : null"
      (closed)="cryptoOpen.set(false)"
      (changed)="events.bump()"
    />

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
  /** Column headers name the currency figures are shown in. */
  protected readonly displayCurrency = () => displayFx().currency;
  private readonly api = inject(Api);
  protected readonly events = inject(DataEvents);
  private readonly quickAdd = inject(QuickAdd);
  protected readonly prefs = inject(Prefs);
  private readonly toasts = inject(Toasts);
  private readonly i18n = inject(TranslateService);
  protected readonly colors = SERIES_COLORS;
  protected readonly periods = RETURN_PERIODS;
  protected readonly classes: AssetClass[] = [
    'Etf',
    'Stock',
    'Bond',
    'Fund',
    'Crypto',
    'Cash',
    'Other',
  ];

  /** The wallet (account) the page is scoped to; '' for all of them. */
  protected readonly walletId = signal('');
  /** Period of the returns and the value chart; remembered in this browser like the theme. */
  protected readonly period = this.prefs.portfolioPeriod;
  protected readonly expanded = signal<string | null>(null);
  private readonly pct = new PercentPipe();
  protected readonly targetsOpen = signal(false);
  protected readonly cryptoOpen = signal(false);
  private readonly editingId = signal<string | null>(null);
  protected readonly targetInputs = signal<Partial<Record<AssetClass, string>>>({});

  private readonly scope = computed<PortfolioScope>(() =>
    this.walletId() ? { accountId: this.walletId() } : {},
  );
  private readonly key = computed(() => ({ scope: this.scope(), v: this.events.version() }));

  protected readonly summary = liveResource({
    params: () => ({ ...this.key(), period: this.period() }),
    stream: ({ params }) => this.api.portfolioSummary(params.scope, params.period),
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
  protected readonly calendar = liveResource({
    params: this.key,
    stream: ({ params }) => this.api.dividendCalendar(params.scope),
  });
  private readonly brokerMovements = liveResource({
    params: () => ({ v: this.events.version() }),
    stream: () => this.api.brokerMovements(),
  });
  /** The selected wallet's movements, or every broker's. */
  protected readonly movements = computed(() =>
    (this.brokerMovements.value() ?? []).filter(
      (m) => !this.walletId() || m.accountId === this.walletId(),
    ),
  );
  protected readonly ignoring = signal<string | null>(null);

  protected recordMovement(m: BrokerMovement) {
    this.quickAdd.addWith(
      brokerMovementPrefill(
        m,
        this.i18n.instant(
          m.amount > 0 ? 'brokerMovements.depositTo' : 'brokerMovements.withdrawalFrom',
          { broker: m.broker },
        ),
      ),
    );
  }

  protected async ignoreMovement(m: BrokerMovement) {
    this.ignoring.set(m.id);
    try {
      await firstValueFrom(this.api.ignoreBrokerMovement(m.id));
      this.toasts.show(this.i18n.instant('notifications.skipped'));
      this.events.bump();
    } catch (err) {
      this.toasts.error(err);
    } finally {
      this.ignoring.set(null);
    }
  }

  /** The calendar lists the positions that pay; the others (to weigh where to add) one tick away. */
  protected readonly payersOnly = signal(true);
  protected readonly calendarRows = computed(() =>
    (this.calendar.value()?.rows ?? []).filter((r) => !this.payersOnly() || r.total > 0),
  );
  /** "jan", "fev", … in the display language. */
  protected readonly monthNames = computed(() => {
    const format = new Intl.DateTimeFormat(this.prefs.locale(), { month: 'short' });
    return Array.from({ length: 12 }, (_, i) =>
      format.format(new Date(2000, i, 1)).replace('.', ''),
    );
  });
  /** Months under half the monthly average: where a payer would even out the income. */
  /** The calendar by month for phones: the total (thin when under half the average) and who pays, the most first. */
  protected readonly calendarByMonth = computed(() => {
    const cal = this.calendar.value();
    if (!cal) return [];
    const names = this.monthNames();
    return cal.months.map((total, index) => ({
      index,
      name: names[index],
      total,
      thin: total < cal.monthlyAverage / 2,
      payers: cal.rows
        .filter((r) => r.months[index])
        .map((r) => ({ symbol: r.symbol, value: r.months[index] }))
        .sort((a, b) => b.value - a.value),
    }));
  });
  protected readonly thinMonths = computed(() => {
    const cal = this.calendar.value();
    if (!cal || !cal.total) return [];
    return cal.months
      .map((v, i) => (v < cal.monthlyAverage / 2 ? this.monthNames()[i] : null))
      .filter((m): m is string => !!m);
  });
  /** A cell's shade: stronger the more it pays, relative to the largest monthly payment. */
  protected heat(value: number, cal: { rows: { months: number[] }[] }): string {
    const max = Math.max(...cal.rows.flatMap((r) => r.months), 1);
    return `color-mix(in oklch, var(--primary) ${Math.round(8 + (value / max) * 32)}%, transparent)`;
  }
  protected readonly performance = liveResource({
    params: () => ({ ...this.key(), period: this.period() }),
    stream: ({ params }) => this.api.performance(params.scope, params.period),
  });

  /** Coins entered by hand (edit buttons, location suggestions). */
  protected readonly manual = liveResource({
    params: () => this.events.version(),
    stream: () => this.api.manualHoldings(),
  });
  private readonly manualByKey = computed(
    () =>
      new Map(
        (this.manual.value()?.holdings ?? []).map((h) => [`${h.accountId}:${h.securityId}`, h]),
      ),
  );
  /** Coins in view (entered by hand, or an exchange such as Binance): their income is rewards, not only dividends. */
  protected readonly hasCrypto = computed(
    () =>
      (this.manual.value()?.holdings.length ?? 0) > 0 ||
      (this.positions.value() ?? []).some((p) => p.assetClass === 'Crypto'),
  );

  /**
   * The latest income, newest first. Exchange rewards (Binance Earn pays every day, often fractions of a cent) are
   * summed per coin and month, so the list shows what was earned instead of rows of 0.00.
   */
  protected readonly recentIncome = computed(() => {
    const rows: RecentIncome[] = [];
    const months = new Map<string, RecentIncome>();
    for (const d of this.dividends.value()?.items ?? []) {
      if (d.broker !== 'Binance') {
        rows.push({ ...d, month: false, count: 1 });
        continue;
      }
      const key = `${d.symbol}:${d.paidOn.slice(0, 7)}`;
      const row = months.get(key);
      if (row) {
        row.gross += d.gross;
        row.withholdingTax += d.withholdingTax;
        row.net += d.net;
        row.netBase += d.netBase;
        row.count++;
      } else {
        const first: RecentIncome = { ...d, paidOn: d.paidOn.slice(0, 7), month: true, count: 1 };
        months.set(key, first);
        rows.push(first);
      }
    }
    return rows.slice(0, 10);
  });
  /** The holding being edited, re-read after every change so new rewards show up in the dialog. */
  protected readonly editing = computed(() => {
    const id = this.editingId();
    return id ? (this.manual.value()?.holdings.find((h) => h.id === id) ?? null) : null;
  });

  /** Every wallet regardless of the selected one, with its own totals: the wallet cards come from here. */
  protected readonly allAccounts = liveResource({
    params: () => ({ v: this.events.version(), period: this.period() }),
    stream: ({ params }) => this.api.portfolioSummary({}, params.period),
  });
  /** "out 2026": a month of summed rewards. */
  protected monthLabel(period: string): string {
    const [y, m] = period.split('-').map(Number);
    return new Intl.DateTimeFormat(this.prefs.locale(), { month: 'short', year: 'numeric' }).format(
      new Date(y, m - 1, 1),
    );
  }

  /** Brokers holding cash, the most first (a wallet's own figure when one is selected). */
  protected readonly cashAccounts = computed(() =>
    (this.summary.value()?.accounts ?? [])
      .filter((a) => a.cash !== 0)
      .sort((a, b) => b.cash - a.cash),
  );
  protected readonly selectedWallet = computed(() => {
    const id = this.walletId();
    return id ? (this.allAccounts.value()?.accounts.find((a) => a.accountId === id) ?? null) : null;
  });
  protected classColor = (c: AssetClass) => CLASS_COLORS[c];

  constructor() {
    // A wallet that is gone (its last coin removed, its connection purged) falls back to all wallets.
    effect(() => {
      const accounts = this.allAccounts.value()?.accounts;
      if (accounts && this.walletId() && !accounts.some((a) => a.accountId === this.walletId())) {
        this.walletId.set('');
      }
    });
    // Coins have no sync: opening the page fetches prices older than 15 minutes, then reloads if any moved.
    this.api.refreshCoinPrices().subscribe({
      next: (r) => {
        if (r.updated) this.events.bump();
      },
      error: () => undefined, // Keep the last known prices.
    });
  }

  /** The return of the hero: over the chosen period (total return since the first deposit for "All"). */
  protected readonly heroReturn = computed<PeriodReturn>(() => {
    const s: PortfolioSummary | undefined = this.summary.value();
    return (
      s?.periodReturn ?? {
        period: 'ALL',
        gain: s?.totalReturn ?? null,
        percent: s?.totalReturnPercent ?? null,
        from: s?.since ?? null,
        partial: false,
        timeWeighted: false,
      }
    );
  });

  protected label(r: PeriodReturn): Label {
    this.prefs.translations();
    return returnLabel(r, this.prefs.locale(), (p) => this.i18n.instant(`periods.${p}`));
  }

  protected returnHint = returnHint;
  protected dayLabel = (b: DayChangeBasis | undefined) => dayChangeLabel(b);
  protected dayHint = (b: DayChangeBasis | undefined) => dayChangeHint(b);

  protected brokerLabel(b: Broker): string {
    this.prefs.translations();
    return this.i18n.instant(b === 'Manual' ? 'crypto.manualSource' : `source.${b}`);
  }

  protected isManual(p: PositionLine): boolean {
    return p.holdings.some((h) => h.broker === 'Manual');
  }

  protected manualFor(accountId: string, securityId: string): ManualHolding | null {
    return this.manualByKey().get(`${accountId}:${securityId}`) ?? null;
  }

  protected openCrypto(holding: ManualHolding | null) {
    this.editingId.set(holding?.id ?? null);
    this.cryptoOpen.set(true);
  }

  /** A line needs two points; while loading, keep the chart frame instead of flashing the empty state. */
  protected readonly hasHistory = computed(() => {
    const p = this.performance.value();
    return !p || p.series.length >= 2;
  });

  protected dayMonth(isoDate: string): string {
    const [y, m, d] = isoDate.slice(0, 10).split('-').map(Number);
    return new Intl.DateTimeFormat(this.prefs.locale(), {
      day: '2-digit',
      month: '2-digit',
    }).format(new Date(y, m - 1, d));
  }

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
          itemStyle: { color: SERIES_COLORS.value },
        },
        {
          name: this.i18n.instant('portfolio.contributions'),
          type: 'line',
          showSymbol: false,
          step: 'end',
          data: series.map((p) => [p.date, p.netContributions]),
          itemStyle: { color: SERIES_COLORS.muted },
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
        valueFormatter: (v: unknown) => formatMoney(this.prefs.locale(), v as number),
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
    // The period chosen at the top, every month shown (the empty ones too).
    const months = incomeMonths(this.dividends.value()?.byMonth ?? [], this.period());
    const locale = this.prefs.locale();
    const label = new Intl.DateTimeFormat(locale, { month: 'short', year: '2-digit' });
    return {
      ...baseChart,
      legend: undefined,
      tooltip: moneyTooltip(locale),
      xAxis: categoryAxis(
        months.map((m) => {
          const [y, mo] = m.period.split('-').map(Number);
          return label.format(new Date(y, mo - 1, 1));
        }),
      ),
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
