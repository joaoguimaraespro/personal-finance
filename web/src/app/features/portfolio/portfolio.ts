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
import { DataEvents } from '../../core/data-events';
import { DayPipe, MoneyPipe, PercentPipe, PricePipe } from '../../core/format';
import {
  AssetClass,
  Broker,
  DayChangeBasis,
  ManualHolding,
  PeriodReturn,
  PortfolioSummary,
  PositionLine,
} from '../../core/models';
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
import { NgIcon, provideIcons } from '@ng-icons/core';
import { APP_ICONS } from '../../shared/icons';
import { BrokerLogoComponent } from '../../shared/broker-logo';
import { CoinIconComponent } from '../../shared/coin-icon';
import { CryptoDialogComponent } from './crypto-dialog';
import { WalletStripComponent } from './wallet-strip';
import {
  Label,
  RETURN_PERIODS,
  dayChangeHint,
  dayChangeLabel,
  returnHint,
  returnLabel,
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

const CLASS_COLORS: Record<AssetClass, string> = {
  Stock: '#8b5cf6',
  Etf: '#10b981',
  Bond: '#0891b2',
  Fund: '#84cc16',
  Crypto: '#f59e0b',
  Cash: '#a1a1aa',
  Other: '#a855f7',
};

/**
 * Read-only view of broker data: nothing on this page can change a broker account. Coins held elsewhere (an
 * exchange account, a cold wallet) are entered by hand here and priced from public quotes.
 */
@Component({
  selector: 'app-portfolio',
  imports: [
    BrokerLogoComponent,
    CoinIconComponent,
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
                      @if (p.assetClass === 'Crypto') {
                        <app-coin-icon [symbol]="p.symbol" [size]="18" />
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
                  <th hlmTh class="text-right">{{ 'portfolio.gross' | translate }}</th>
                  <th hlmTh class="text-right">{{ 'portfolio.withholding' | translate }}</th>
                  <th hlmTh class="text-right">{{ 'portfolio.net' | translate }}</th>
                </tr>
              </thead>
              <tbody hlmTBody>
                @for (d of (dividends.value()?.items ?? []).slice(0, 10); track $index) {
                  <tr hlmTr>
                    <td hlmTd class="text-muted-foreground">{{ d.paidOn | day: 'short' }}</td>
                    <td hlmTd class="font-medium">
                      @if (d.broker === 'Manual') {
                        <app-coin-icon class="mr-1 align-[-3px]" [symbol]="d.symbol" [size]="16" />
                      }
                      {{ d.symbol }}
                      @if (d.broker === 'Manual') {
                        <span
                          class="badge ml-1 bg-emerald-500/10 !px-1.5 !py-0 text-[10px] text-emerald-700 dark:text-emerald-400"
                          >{{ 'crypto.rewardBadge' | translate }}</span
                        >
                      }
                    </td>
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
  private readonly api = inject(Api);
  protected readonly events = inject(DataEvents);
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
  protected readonly hasCrypto = computed(() => (this.manual.value()?.holdings.length ?? 0) > 0);
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
