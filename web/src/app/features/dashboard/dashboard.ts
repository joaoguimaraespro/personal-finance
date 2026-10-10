import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { liveResource } from '../../core/resource';
import { Router, RouterLink } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import type { EChartsOption } from 'echarts';
import { firstValueFrom } from 'rxjs';
import { Api } from '../../core/api';
import { DataEvents, QuickAdd } from '../../core/data-events';
import { DayPipe, MoneyPipe, MonthNamePipe, PercentPipe } from '../../core/format';
import type { DayChangeBasis, MonthlySummary, NetWorthHistory } from '../../core/models';
import { dayChangeHint, dayChangeLabel, monthYear } from '../portfolio/periods';
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
import { ProgressComponent } from '../../shared/progress';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { NgIcon } from '@ng-icons/core';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';
import { EmptyStateComponent } from '../../shared/empty-state';
import { APP_ICONS, PAGE_ICONS } from '../../shared/icons';
import { PageHeaderComponent } from '../../shared/page-header';

/**
 * Home overview: where things stand today (net worth, latest month's cash flow, portfolio), the selected
 * year's summary, what needs attention (pending recurring, goals) and the month-by-month detail.
 * Every figure comes from the API; the view only formats and arranges it.
 */
@Component({
  selector: 'app-dashboard',
  imports: [
    NgIcon,
    HlmTableImports,
    HlmButtonImports,
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
  ],
  providers: [APP_ICONS],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    :host {
      display: block;
    }
    .hero {
      display: flex;
      flex-direction: column;
      min-height: 12rem;
    }
    .onboard {
      border-color: color-mix(in oklab, var(--primary) 25%, transparent);
      background-image: linear-gradient(
        135deg,
        color-mix(in oklab, var(--primary) 8%, transparent),
        transparent 70%
      );
    }
    .flow-row {
      display: grid;
      grid-template-columns: minmax(4.5rem, auto) 1fr auto;
      align-items: center;
      gap: 0.75rem;
    }
    .hero-label {
      display: flex;
      align-items: center;
      gap: 0.625rem;
      font-size: 0.8125rem;
      font-weight: 500;
      color: var(--muted-foreground);
    }
    .hero-icon {
      display: flex;
      flex-shrink: 0;
      align-items: center;
      justify-content: center;
      width: 1.75rem;
      height: 1.75rem;
      border-radius: 0.5rem;
      font-size: 0.875rem;
    }
    .hero-value {
      margin-top: 0.875rem;
      font-size: 1.875rem;
      line-height: 2.25rem;
      font-weight: 600;
      letter-spacing: -0.025em;
      font-variant-numeric: tabular-nums;
      white-space: nowrap;
    }
    .delta {
      display: inline-flex;
      align-items: center;
      gap: 0.25rem;
      border-radius: 9999px;
      padding: 0.125rem 0.5rem;
      font-size: 0.75rem;
      font-weight: 500;
      font-variant-numeric: tabular-nums;
      white-space: nowrap;
    }
    .skeleton {
      border-radius: 0.375rem;
      background: var(--muted);
      animation: dash-pulse 2s cubic-bezier(0.4, 0, 0.6, 1) infinite;
    }
    @keyframes dash-pulse {
      50% {
        opacity: 0.5;
      }
    }
  `,
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

    <!-- New user: a short checklist instead of a wall of zeros. -->
    @if (showOnboarding()) {
      <section class="card onboard mb-6" aria-labelledby="dash-start">
        <div class="mb-4 flex items-start gap-3">
          <span class="hero-icon bg-primary/10 text-primary dark:bg-primary/20" aria-hidden="true">
            <ng-icon name="lucideSparkles" />
          </span>
          <div>
            <h2 id="dash-start" class="text-base font-semibold">
              {{ 'dashboard.getStarted' | translate }}
            </h2>
            <p class="text-sm text-muted-foreground">
              {{ 'dashboard.getStartedHint' | translate }}
            </p>
          </div>
        </div>
        <ol class="grid gap-3 sm:grid-cols-3">
          @for (s of onboarding(); track s.key; let i = $index) {
            <li class="flex flex-col gap-3 rounded-lg border bg-card p-4">
              <div class="flex items-start gap-3">
                @if (s.done) {
                  <span
                    class="flex size-6 shrink-0 items-center justify-center rounded-full bg-primary text-xs text-primary-foreground"
                    role="img"
                    [attr.aria-label]="'dashboard.done' | translate"
                  >
                    <ng-icon name="lucideCheck" />
                  </span>
                } @else {
                  <span
                    class="num flex size-6 shrink-0 items-center justify-center rounded-full border text-xs font-semibold text-muted-foreground"
                    aria-hidden="true"
                    >{{ i + 1 }}</span
                  >
                }
                <div class="min-w-0">
                  <p class="text-sm font-medium" [class.text-muted-foreground]="s.done">
                    {{ 'dashboard.' + s.key | translate }}
                  </p>
                  <p class="text-xs text-muted-foreground">
                    {{ 'dashboard.' + s.key + 'Hint' | translate }}
                  </p>
                </div>
              </div>
              @if (!s.done) {
                <div class="mt-auto flex flex-wrap gap-2">
                  @switch (s.key) {
                    @case ('stepAccount') {
                      <a hlmBtn size="sm" routerLink="/accounts">
                        <ng-icon name="lucidePlus" />{{ 'accounts.new' | translate }}
                      </a>
                    }
                    @case ('stepTransaction') {
                      <button
                        hlmBtn
                        size="sm"
                        [variant]="onboarding()[0].done ? 'default' : 'outline'"
                        (click)="quick.add()"
                      >
                        <ng-icon name="lucidePlus" />{{ 'tx.add' | translate }}
                      </button>
                      <a hlmBtn size="sm" variant="ghost" routerLink="/import">
                        <ng-icon name="lucideUpload" />{{ 'nav.import' | translate }}
                      </a>
                    }
                    @default {
                      <a hlmBtn size="sm" variant="outline" routerLink="/connections">
                        <ng-icon name="lucideLink" />{{ 'portfolio.connect' | translate }}
                      </a>
                    }
                  }
                </div>
              }
            </li>
          }
        </ol>
      </section>
    }

    <!-- Hero: where things stand today. -->
    <section class="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
      <!-- Net worth -->
      <div class="card hero md:col-span-2 xl:col-span-1">
        <div class="flex items-center justify-between gap-2">
          <h2 class="hero-label">
            <span
              class="hero-icon bg-primary/10 text-primary dark:bg-primary/20"
              aria-hidden="true"
            >
              <ng-icon [name]="icons.netWorth" />
            </span>
            {{ 'nav.netWorth' | translate }}
          </h2>
          <a
            hlmBtn
            variant="ghost"
            size="icon-sm"
            class="-my-1 text-muted-foreground"
            routerLink="/net-worth"
            [attr.aria-label]="'nav.netWorth' | translate"
          >
            <ng-icon name="lucideChevronRight" />
          </a>
        </div>
        @if (netWorth.value(); as nw) {
          @if (nw.series.length || nw.current.netWorth) {
            <p class="hero-value">{{ nw.current.netWorth | money }}</p>
            @if (nw.changeSinceStart !== null) {
              <p class="mt-2 flex flex-wrap items-center gap-x-2 gap-y-1 text-xs">
                <span [class]="toneChip(nw.changeSinceStart)">
                  <ng-icon [name]="trendIcon(nw.changeSinceStart)" aria-hidden="true" />
                  {{ nw.changeSinceStart | money: 'EUR' : true }}
                  @if (nw.changeSinceStartPercent !== null) {
                    · {{ signedPct(nw.changeSinceStartPercent) }}
                  }
                </span>
                @if (nw.startDate) {
                  <span class="text-muted-foreground"
                    >{{ 'netWorth.since' | translate }} {{ nw.startDate | day }}</span
                  >
                }
              </p>
            }
            <!-- What it is made of: fills the card from day one, before there is a history to chart. -->
            <dl class="mt-3 space-y-1.5 text-xs">
              @for (part of netWorthParts(nw); track part.key) {
                <div class="flex items-center justify-between gap-3">
                  <dt class="text-muted-foreground flex items-center gap-2">
                    <span class="size-2 rounded-full" [style.background-color]="part.color"></span
                    >{{ 'netWorth.' + part.key | translate }}
                  </dt>
                  <dd class="num" [class.tone-neg]="part.key === 'liabilities'">
                    {{ part.key === 'liabilities' ? '−' : '' }}{{ part.value | money }}
                  </dd>
                </div>
              }
            </dl>
            @if (nw.series.length > 1) {
              <app-chart class="-mx-1 mt-auto h-16 pt-3" [option]="sparkline()" />
            }
          } @else {
            <p class="mt-3 text-sm text-muted-foreground">
              {{ 'dashboard.netWorthEmpty' | translate }}
            </p>
            <a
              hlmBtn
              variant="outline"
              size="sm"
              class="mt-auto self-start"
              routerLink="/net-worth"
            >
              <ng-icon name="lucidePlus" />{{ 'netWorth.addAsset' | translate }}
            </a>
          }
        } @else {
          <div class="skeleton mt-4 h-8 w-40"></div>
          <div class="skeleton mt-3 h-4 w-28"></div>
        }
      </div>

      <!-- Latest month's cash flow -->
      <div class="card hero">
        <div class="flex items-center justify-between gap-2">
          <h2 class="hero-label">
            <span
              class="hero-icon bg-emerald-500/10 text-emerald-700 dark:text-emerald-400"
              aria-hidden="true"
            >
              <ng-icon name="lucideScale" />
            </span>
            {{ 'dashboard.cashFlow' | translate }}
          </h2>
          @if (cashMonth(); as m) {
            <button
              type="button"
              class="rounded-md px-1.5 py-0.5 text-xs font-medium text-muted-foreground hover:bg-muted hover:text-foreground"
              (click)="openMonth(m.period.month)"
            >
              {{ m.period.month | monthName }} {{ m.period.year }}
            </button>
          }
        </div>
        @if (cashMonth(); as m) {
          @if (m.transactionCount) {
            <p class="hero-value" [class.tone-neg]="m.netBalance < 0">
              {{ m.netBalance | money: 'EUR' : true }}
            </p>
            <p class="mt-2 text-xs text-muted-foreground">
              @if (m.savingsRate !== null) {
                {{ 'dashboard.setAside' | translate: { rate: (m.savingsRate | pct) } }}
              } @else {
                {{ 'dashboard.cashFlowHint' | translate }}
              }
            </p>
            <dl class="mt-auto grid gap-2.5 pt-4 text-xs">
              <div class="flow-row">
                <dt class="text-muted-foreground">{{ 'kpi.income' | translate }}</dt>
                <div class="h-1.5 overflow-hidden rounded-full bg-muted" aria-hidden="true">
                  <div
                    class="h-full rounded-full"
                    [style.width.%]="barWidth(m.income, m)"
                    [style.background]="colors.income"
                  ></div>
                </div>
                <dd class="num text-right font-medium">{{ m.income | money }}</dd>
              </div>
              <div class="flow-row">
                <dt class="text-muted-foreground">{{ 'kpi.expenses' | translate }}</dt>
                <div class="h-1.5 overflow-hidden rounded-full bg-muted" aria-hidden="true">
                  <div
                    class="h-full rounded-full"
                    [style.width.%]="barWidth(m.totalExpenses, m)"
                    [style.background]="colors.expenses"
                  ></div>
                </div>
                <dd class="num text-right font-medium">{{ m.totalExpenses | money }}</dd>
              </div>
            </dl>
          } @else {
            <p class="mt-3 text-sm text-muted-foreground">
              {{ 'dashboard.noActivity' | translate }}
            </p>
            <button
              hlmBtn
              variant="outline"
              size="sm"
              class="mt-auto self-start"
              (click)="quick.add()"
            >
              <ng-icon name="lucidePlus" />{{ 'tx.add' | translate }}
            </button>
          }
        } @else {
          <div class="skeleton mt-4 h-8 w-36"></div>
          <div class="skeleton mt-3 h-4 w-48"></div>
        }
      </div>

      <!-- Portfolio -->
      <div class="card hero">
        <div class="flex items-center justify-between gap-2">
          <h2 class="hero-label">
            <span
              class="hero-icon bg-violet-500/10 text-violet-600 dark:text-violet-400"
              aria-hidden="true"
            >
              <ng-icon [name]="icons.portfolio" />
            </span>
            {{ 'nav.portfolio' | translate }}
          </h2>
          <a
            hlmBtn
            variant="ghost"
            size="icon-sm"
            class="-my-1 text-muted-foreground"
            routerLink="/portfolio"
            [attr.aria-label]="'nav.portfolio' | translate"
          >
            <ng-icon name="lucideChevronRight" />
          </a>
        </div>
        @if (portfolio.value(); as p) {
          @if (p.positions || p.accounts.length) {
            <p class="hero-value">{{ p.totalValue | money }}</p>
            <p class="mt-2 flex flex-wrap items-center gap-x-2 gap-y-1 text-xs">
              @if (p.dayChange !== null) {
                <span [class]="toneChip(p.dayChange)">
                  <ng-icon [name]="trendIcon(p.dayChange)" aria-hidden="true" />
                  {{ p.dayChange | money: 'EUR' : true }}
                  @if (p.dayChangePercent !== null) {
                    · {{ signedPct(p.dayChangePercent) }}
                  }
                </span>
                <span
                  class="text-muted-foreground"
                  [attr.title]="
                    dayHint(p.dayChangeBasis) ? (dayHint(p.dayChangeBasis)! | translate) : null
                  "
                  >{{ dayLabel(p.dayChangeBasis) | translate }}</span
                >
              } @else {
                <span
                  class="text-muted-foreground cursor-help"
                  [attr.title]="'portfolio.todayPending' | translate"
                  [attr.aria-label]="'portfolio.todayPending' | translate"
                  >—</span
                >
              }
            </p>
            <dl class="mt-auto grid gap-2 pt-4 text-xs">
              <div class="flex items-center justify-between gap-3">
                <dt class="text-muted-foreground" [title]="'portfolio.returnAllHint' | translate">
                  @if (p.since) {
                    {{ 'portfolio.returnSince' | translate: { date: monthYear(p.since) } }}
                  } @else {
                    {{ 'portfolio.totalReturn' | translate }}
                  }
                </dt>
                <dd
                  class="num font-medium"
                  [class.tone-pos]="p.totalReturn > 0"
                  [class.tone-neg]="p.totalReturn < 0"
                >
                  {{ p.totalReturn | money: 'EUR' : true }}
                  @if (p.totalReturnPercent !== null) {
                    <span class="text-muted-foreground"
                      >({{ signedPct(p.totalReturnPercent) }})</span
                    >
                  }
                </dd>
              </div>
              <div class="flex items-center justify-between gap-3">
                <dt class="text-muted-foreground">{{ 'portfolio.contributions' | translate }}</dt>
                <dd class="num font-medium">{{ p.netContributions | money }}</dd>
              </div>
            </dl>
          } @else {
            <p class="mt-3 text-sm text-muted-foreground">{{ 'portfolio.empty' | translate }}</p>
            <a
              hlmBtn
              variant="outline"
              size="sm"
              class="mt-auto self-start"
              routerLink="/connections"
            >
              <ng-icon name="lucideLink" />{{ 'portfolio.connect' | translate }}
            </a>
          }
        } @else if (portfolio.error()) {
          <p class="mt-3 text-sm text-muted-foreground">{{ 'common.error' | translate }}</p>
        } @else {
          <div class="skeleton mt-4 h-8 w-36"></div>
          <div class="skeleton mt-3 h-4 w-24"></div>
        }
      </div>
    </section>

    <!-- The selected year. Everyday money and investments are never added together; "overall" is the explicit summary. -->
    @if (overview.value(); as o) {
      <section class="card mt-6 !p-0" aria-labelledby="dash-year">
        <h2 id="dash-year" class="card-title !mb-0 px-5 pt-5">
          <ng-icon name="lucideCalendar" />{{
            'dashboard.yearAtGlance' | translate: { year: o.year }
          }}
        </h2>
        <div class="grid divide-y md:grid-cols-3 md:divide-x md:divide-y-0">
          <div class="flex flex-col p-5" role="group" aria-labelledby="dash-everyday">
            <h3 id="dash-everyday" class="flex items-center gap-2 text-sm font-semibold">
              <ng-icon name="lucideWallet" class="text-primary" aria-hidden="true" />
              {{ 'dashboard.everyday' | translate }}
            </h3>
            <p class="mt-3 text-xs text-muted-foreground">
              {{ 'kpi.netBalance' | translate }}
            </p>
            <p
              class="num text-2xl font-semibold tracking-tight"
              [class.tone-pos]="o.everyday.netBalance > 0"
              [class.tone-neg]="o.everyday.netBalance < 0"
            >
              {{ o.everyday.netBalance | money }}
            </p>
            <dl class="mt-3 divide-y text-sm">
              <div class="flex items-center justify-between gap-3 py-2">
                <dt class="flex items-center gap-2 text-muted-foreground">
                  <ng-icon
                    name="lucideArrowDownLeft"
                    [style.color]="colors.income"
                    aria-hidden="true"
                  />
                  {{ 'kpi.income' | translate }}
                </dt>
                <dd class="num font-medium">{{ o.everyday.income | money }}</dd>
              </div>
              <div class="flex items-center justify-between gap-3 py-2">
                <dt class="flex items-center gap-2 text-muted-foreground">
                  <ng-icon
                    name="lucideArrowUpRight"
                    [style.color]="colors.expenses"
                    aria-hidden="true"
                  />
                  {{ 'kpi.expenses' | translate }}
                </dt>
                <dd class="num font-medium">{{ o.everyday.expenses | money }}</dd>
              </div>
            </dl>
            <p class="mt-auto pt-3 text-xs text-muted-foreground">
              {{ 'dashboard.everydayHint' | translate }}
            </p>
          </div>

          <div class="flex flex-col p-5" role="group" aria-labelledby="dash-investments">
            <h3 id="dash-investments" class="flex items-center gap-2 text-sm font-semibold">
              <ng-icon name="lucideBriefcase" class="text-primary" aria-hidden="true" />
              {{ 'dashboard.investments' | translate }}
            </h3>
            <p class="mt-3 text-xs text-muted-foreground">
              {{ 'dashboard.netInvested' | translate }}
            </p>
            <p class="num text-2xl font-semibold tracking-tight">
              {{ o.investments.netInvested | money }}
            </p>
            <dl class="mt-3 divide-y text-sm">
              <div class="flex items-center justify-between gap-3 py-2">
                <dt class="flex items-center gap-2 text-muted-foreground">
                  <ng-icon
                    name="lucideShoppingCart"
                    [style.color]="colors.invested"
                    aria-hidden="true"
                  />
                  {{ 'dashboard.purchases' | translate }}
                </dt>
                <dd class="num font-medium">{{ o.investments.purchases | money }}</dd>
              </div>
              <div class="flex items-center justify-between gap-3 py-2">
                <dt class="flex items-center gap-2 text-muted-foreground">
                  <ng-icon
                    name="lucideHandCoins"
                    [style.color]="colors.invested"
                    aria-hidden="true"
                  />
                  {{ 'dashboard.sales' | translate }}
                </dt>
                <dd class="num font-medium">{{ o.investments.sales | money }}</dd>
              </div>
              <div class="flex items-center justify-between gap-3 py-2">
                <dt class="flex items-center gap-2 text-muted-foreground">
                  <ng-icon
                    name="lucidePercent"
                    [style.color]="colors.invested"
                    aria-hidden="true"
                  />
                  {{ 'dashboard.investmentRate' | translate }}
                </dt>
                <dd class="num font-medium">{{ o.investments.investmentRate | pct }}</dd>
              </div>
            </dl>
            <p class="mt-auto pt-3 text-xs text-muted-foreground">
              {{ 'dashboard.investmentsHint' | translate }}
            </p>
          </div>

          <div class="flex flex-col p-5" role="group" aria-labelledby="dash-overall">
            <h3 id="dash-overall" class="flex items-center gap-2 text-sm font-semibold">
              <ng-icon name="lucidePiggyBank" class="text-primary" aria-hidden="true" />
              {{ 'dashboard.overall' | translate }}
            </h3>
            <p class="mt-3 text-xs text-muted-foreground">{{ 'kpi.saved' | translate }}</p>
            <p class="num text-2xl font-semibold tracking-tight">{{ o.saved | money }}</p>
            <dl class="mt-3 divide-y text-sm">
              <div class="flex items-center justify-between gap-3 py-2">
                <dt class="flex items-center gap-2 text-muted-foreground">
                  <ng-icon name="lucideGauge" [style.color]="colors.saved" aria-hidden="true" />
                  {{ 'kpi.avgSavingsRate' | translate }}
                </dt>
                <dd class="num font-medium">{{ o.averageMonthlySavingsRate | pct }}</dd>
              </div>
            </dl>
            @if (o.averageMonthlySavingsRate !== null) {
              <app-progress class="mt-1 block" [value]="o.averageMonthlySavingsRate" />
            }
            @if (o.weightedSavingsRate !== null) {
              <p class="mt-auto pt-3 text-xs text-muted-foreground">
                {{ 'dashboard.weightedRate' | translate: { rate: (o.weightedSavingsRate | pct) } }}
              </p>
            }
          </div>
        </div>
      </section>
    }

    <!-- Needs attention: recurring to confirm, then goals. -->
    @let pendingList = pending.value() ?? [];
    <section class="mt-6 grid items-start gap-4" [class.xl:grid-cols-2]="pendingList.length > 0">
      @if (pendingList.length) {
        <div class="card">
          <div class="mb-3 flex items-center justify-between gap-2">
            <h2 class="card-title !mb-0">
              <ng-icon name="lucideCalendarClock" class="!text-amber-600 dark:!text-amber-400" />
              {{ 'recurring.pendingTitle' | translate }}
            </h2>
            <span
              class="num rounded-full bg-amber-500/10 px-2 py-0.5 text-xs font-medium whitespace-nowrap text-amber-700 dark:text-amber-300"
              >{{ 'dashboard.pendingCount' | translate: { count: pendingList.length } }}</span
            >
          </div>
          <ul class="divide-y">
            @for (e of pendingList; track e.id) {
              <li class="flex items-center gap-3 py-2.5">
                <span
                  class="hero-icon"
                  [class]="
                    e.type === 'Income'
                      ? 'bg-emerald-500/10 text-emerald-700 dark:text-emerald-400'
                      : 'bg-muted text-muted-foreground'
                  "
                  aria-hidden="true"
                >
                  <ng-icon [name]="e.type === 'Income' ? 'lucideArrowDownLeft' : 'lucideRepeat'" />
                </span>
                <div class="min-w-0 flex-1">
                  <p class="truncate text-sm font-medium">{{ e.name }}</p>
                  <p class="text-xs text-muted-foreground">{{ e.dueOn | day }}</p>
                </div>
                <span
                  class="num text-sm font-semibold whitespace-nowrap"
                  [class.tone-pos]="e.type === 'Income'"
                  >{{ e.amount | money: e.currency }}</span
                >
                <div class="flex shrink-0 items-center gap-1">
                  <button
                    hlmBtn
                    variant="ghost"
                    size="sm"
                    (click)="skip(e.id)"
                    [attr.aria-label]="('recurring.skip' | translate) + ' · ' + e.name"
                  >
                    <ng-icon name="lucideSkipForward" /><span class="hidden sm:inline">{{
                      'recurring.skip' | translate
                    }}</span>
                  </button>
                  <button
                    hlmBtn
                    variant="outline"
                    size="sm"
                    (click)="confirm(e.id)"
                    [attr.aria-label]="('recurring.confirm' | translate) + ' · ' + e.name"
                  >
                    <ng-icon name="lucideCheck" class="text-primary" /><span
                      class="hidden sm:inline"
                      >{{ 'recurring.confirm' | translate }}</span
                    >
                  </button>
                </div>
              </li>
            }
          </ul>
        </div>
      }

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
        <div
          class="grid gap-x-8 gap-y-5"
          [class.sm:grid-cols-2]="!pendingList.length"
          [class.xl:grid-cols-3]="!pendingList.length"
        >
          @for (g of goals.value() ?? []; track g.id) {
            <div>
              <div class="mb-2 flex items-center justify-between gap-3 text-sm">
                <span class="flex min-w-0 items-center gap-1.5 font-medium">
                  <span class="truncate">{{ g.name }}</span>
                  @if (g.achieved) {
                    <ng-icon
                      name="lucideCircleCheck"
                      class="shrink-0 text-primary"
                      role="img"
                      [attr.aria-label]="'goals.achieved' | translate"
                    />
                  }
                </span>
                <span class="num text-xs font-medium text-muted-foreground">{{
                  g.progress | pct: 0
                }}</span>
              </div>
              <app-progress [value]="g.progress" />
              <div class="num mt-1.5 flex justify-between gap-3 text-xs text-muted-foreground">
                <span>{{ g.currentAmount | money }} / {{ g.targetAmount | money }}</span>
                @if (g.targetDate && !g.achieved) {
                  <span class="whitespace-nowrap">{{ g.targetDate | day }}</span>
                }
              </div>
            </div>
          } @empty {
            <app-empty-state
              class="col-span-full"
              [icon]="icons.goals"
              [text]="'goals.empty' | translate"
            >
              <a hlmBtn variant="outline" size="sm" routerLink="/goals">
                <ng-icon name="lucidePlus" />{{ 'goals.new' | translate }}
              </a>
            </app-empty-state>
          }
        </div>
      </div>
    </section>

    <!-- Loans: what is owed and the next instalment of each. -->
    @if (loans.value()?.length) {
      @let ls = loans.value()!;
      <section class="card mt-6">
        <div class="mb-3 flex items-center justify-between gap-2">
          <h2 class="card-title !mb-0">
            <ng-icon name="lucideHandCoins" />{{ 'loans.dashboardTitle' | translate }}
          </h2>
          <span class="text-muted-foreground num text-xs"
            >{{ 'loans.owed' | translate }}
            <b class="text-foreground">{{ loansOwed() | money }}</b></span
          >
        </div>
        <ul class="grid gap-3 md:grid-cols-2">
          @for (l of ls; track l.accountId) {
            <li>
              <a
                [routerLink]="['/loans', l.accountId]"
                class="hover:bg-muted/50 block rounded-lg border p-3 transition-colors"
              >
                <div class="flex items-baseline justify-between gap-2">
                  <span class="truncate font-medium">{{ l.accountName }}</span>
                  <span class="num font-semibold">{{ l.outstanding | money }}</span>
                </div>
                <app-progress class="mt-2 block" [value]="l.paidOffShare" />
                <p class="text-muted-foreground num mt-1.5 text-xs">
                  @if (l.next; as n) {
                    {{ 'loans.nextPayment' | translate }} {{ n.payment | money }} ·
                    {{ n.date | day }} ·
                  }
                  {{ 'loans.endsOn' | translate: { date: (l.endDate | day) } }}
                </p>
              </a>
            </li>
          }
        </ul>
      </section>
    }

    @if (hasTransactions()) {
      <section class="mt-6 grid gap-4 xl:grid-cols-5">
        <div class="card xl:col-span-3">
          <h2 class="card-title">
            <ng-icon name="lucideChartColumn" />{{ 'charts.incomeVsExpenses' | translate }}
          </h2>
          <app-chart class="h-72" [option]="incomeVsExpenses()" />
        </div>
        <div class="card xl:col-span-2">
          <h2 class="card-title">
            <ng-icon name="lucideChartLine" />{{ 'charts.realRates' | translate }}
          </h2>
          <app-chart class="h-72" [option]="rates()" />
        </div>
      </section>
    }

    <section class="card mt-6 !p-0">
      <h2 class="card-title px-5 pt-5">
        <ng-icon name="lucideCalendarDays" />{{ 'dashboard.monthlyView' | translate }}
      </h2>
      @if (hasTransactions()) {
        <!-- Phones: one line per month with its balance; the full table from tablets up. -->
        <ul class="divide-y md:hidden">
          @for (row of shownMonths(); track row.month.period.month) {
            <li>
              <button
                type="button"
                class="hover:bg-muted/50 flex w-full items-center justify-between gap-3 px-5 py-3 text-left"
                (click)="openMonth(row.month.period.month)"
              >
                <span class="min-w-0">
                  <span class="block font-medium first-letter:uppercase">{{
                    row.month.period.month | monthName
                  }}</span>
                  <span class="text-muted-foreground num block truncate text-xs">
                    {{ 'kpi.income' | translate }} {{ row.month.income | money }} ·
                    {{ 'kpi.savingsRate' | translate }} {{ row.month.savingsRate | pct }}
                  </span>
                </span>
                <span
                  class="num shrink-0 font-semibold"
                  [class.tone-pos]="row.month.netBalance > 0"
                  [class.tone-neg]="row.month.netBalance < 0"
                  >{{ row.month.netBalance | money: 'EUR' : true }}</span
                >
              </button>
            </li>
          }
        </ul>
        <div class="table-wrap hidden md:block">
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
              </tr>
            </thead>
            <tbody hlmTBody>
              <!-- The balance's colour says positive or negative; months still ahead are left out. -->
              @for (row of shownMonths(); track row.month.period.month) {
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
                </tr>
              }
            </tbody>
          </table>
        </div>
      } @else {
        <app-empty-state icon="lucideCalendarDays" [text]="'common.noData' | translate" />
      }
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
  protected readonly quick = inject(QuickAdd);
  protected readonly colors = SERIES_COLORS;
  protected readonly icons = PAGE_ICONS;
  private readonly thisYear = new Date().getFullYear();
  protected readonly year = signal(this.thisYear);

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
  protected readonly loans = liveResource({
    params: () => this.events.version(),
    stream: () => this.api.loans(),
  });
  protected readonly loansOwed = computed(() =>
    (this.loans.value() ?? []).reduce((sum, l) => sum + l.outstanding, 0),
  );
  protected readonly goals = liveResource({
    params: () => this.events.version(),
    stream: () => this.api.goals(),
  });
  /** Months of the selected year up to now (a past year: all twelve); empty months ahead are left out. */
  protected readonly shownMonths = computed(() => {
    const now = new Date();
    return (this.annual.value()?.months ?? []).filter(
      (r) =>
        r.month.transactionCount > 0 ||
        r.month.period.year < now.getFullYear() ||
        (r.month.period.year === now.getFullYear() && r.month.period.month <= now.getMonth() + 1),
    );
  });

  protected netWorthParts(nw: NetWorthHistory) {
    const c = nw.current;
    return [
      { key: 'cash', value: c.cash, color: SERIES_COLORS.income },
      { key: 'investments', value: c.investments, color: SERIES_COLORS.invested },
      { key: 'manual', value: c.manualAssets, color: SERIES_COLORS.saved },
      { key: 'liabilities', value: c.liabilities, color: SERIES_COLORS.expenses },
    ].filter((p) => p.value !== 0);
  }

  protected readonly netWorth = liveResource({
    params: () => this.events.version(),
    stream: () => this.api.netWorth(),
  });
  protected readonly portfolio = liveResource({
    params: () => this.events.version(),
    stream: () => this.api.portfolioSummary(),
  });
  protected readonly accounts = liveResource({
    params: () => this.events.version(),
    stream: () => this.api.accounts(),
  });

  protected readonly hasTransactions = computed(() =>
    (this.annual.value()?.months ?? []).some((r) => r.month.transactionCount > 0),
  );

  /** Onboarding checklist; the i18n keys are dashboard.<key> and dashboard.<key>Hint. */
  protected readonly onboarding = computed(() => [
    { key: 'stepAccount', done: (this.accounts.value() ?? []).length > 0 },
    { key: 'stepTransaction', done: this.hasTransactions() },
    { key: 'stepBroker', done: (this.portfolio.value()?.accounts ?? []).length > 0 },
  ]);

  /** Shown until there is an account and, for the current year, some activity. */
  protected readonly showOnboarding = computed(() => {
    if (!this.accounts.hasValue() || !this.annual.hasValue()) return false;
    const [account, transaction] = this.onboarding();
    return !account.done || (this.year() === this.thisYear && !transaction.done);
  });

  /** The latest month with activity up to today (early in a month the current one is usually empty). */
  protected readonly cashMonth = computed<MonthlySummary | null>(() => {
    const months = this.annual.value()?.months ?? [];
    const now = new Date();
    const limit = this.year() === now.getFullYear() ? now.getMonth() + 1 : 12;
    const upTo = months.filter((r) => r.month.period.month <= limit).map((r) => r.month);
    return [...upTo].reverse().find((m) => m.transactionCount) ?? upTo.at(-1) ?? null;
  });

  private readonly labels = computed(() => {
    const locale = this.prefs.locale();
    return Array.from({ length: 12 }, (_, i) =>
      new Intl.DateTimeFormat(locale, { month: 'short' }).format(new Date(2000, i, 1)),
    );
  });

  protected readonly sparkline = computed<EChartsOption>(() => {
    const series = this.netWorth.value()?.series ?? [];
    this.prefs.translations();
    const color = SERIES_COLORS.income;
    return {
      grid: { left: 0, right: 0, top: 4, bottom: 0 },
      tooltip: { ...moneyTooltip(this.prefs.locale()), confine: true },
      xAxis: { type: 'time', show: false },
      yAxis: { type: 'value', show: false, scale: true },
      series: [
        {
          name: this.i18n.instant('nav.netWorth'),
          type: 'line',
          smooth: true,
          showSymbol: false,
          data: series.map((p) => [p.date, p.netWorth]),
          lineStyle: { width: 2, color },
          itemStyle: { color },
          areaStyle: {
            color: {
              type: 'linear',
              x: 0,
              y: 0,
              x2: 0,
              y2: 1,
              colorStops: [
                { offset: 0, color: 'rgba(5,150,105,0.22)' },
                { offset: 1, color: 'rgba(5,150,105,0)' },
              ],
            },
          },
        },
      ],
    };
  });

  protected readonly incomeVsExpenses = computed<EChartsOption>(() => {
    const months = this.annual.value()?.months ?? [];
    this.prefs.translations();
    const active = (m: MonthlySummary, v: number) => (m.transactionCount ? v : null);
    return {
      ...baseChart,
      tooltip: moneyTooltip(this.prefs.locale()),
      xAxis: categoryAxis(this.labels()),
      yAxis: moneyAxis(this.prefs.locale()),
      series: [
        {
          name: this.i18n.instant('kpi.income'),
          type: 'bar',
          data: months.map((r) => active(r.month, r.month.income)),
          itemStyle: { color: SERIES_COLORS.income, borderRadius: [4, 4, 0, 0] },
          barGap: '15%',
          barMaxWidth: 16,
        },
        {
          name: this.i18n.instant('kpi.expenses'),
          type: 'bar',
          data: months.map((r) => active(r.month, r.month.totalExpenses)),
          itemStyle: { color: SERIES_COLORS.expenses, borderRadius: [4, 4, 0, 0] },
          barMaxWidth: 16,
        },
        {
          name: this.i18n.instant('kpi.netBalance'),
          type: 'line',
          connectNulls: false,
          symbol: 'circle',
          symbolSize: 5,
          data: months.map((r) => active(r.month, r.month.netBalance)),
          lineStyle: { width: 1.5, type: 'dashed', color: SERIES_COLORS.net },
          itemStyle: { color: SERIES_COLORS.net },
          z: 3,
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
          symbol: 'circle',
          symbolSize: 5,
          data: months.map((m) => m.month.investmentRate),
          itemStyle: { color: SERIES_COLORS.invested },
          lineStyle: { width: 2 },
        },
        {
          name: this.i18n.instant('kpi.savedPct'),
          type: 'line',
          smooth: true,
          connectNulls: false,
          symbol: 'circle',
          symbolSize: 5,
          data: months.map((m) => m.month.savingsOnlyRate),
          itemStyle: { color: SERIES_COLORS.saved },
          lineStyle: { width: 2 },
        },
      ],
    };
  });

  /** Bar length relative to the larger of the month's income and expenses (display only). */
  protected barWidth(value: number, m: MonthlySummary): number {
    const max = Math.max(m.income, m.totalExpenses);
    return max > 0 ? Math.max(2, (value / max) * 100) : 0;
  }

  /** "Today", or "24h" when the portfolio is only coins; the hint says when coins count over 24 hours. */
  protected dayLabel = (b: DayChangeBasis | undefined) => dayChangeLabel(b);
  protected dayHint = (b: DayChangeBasis | undefined) => dayChangeHint(b);

  protected monthYear(isoDate: string): string {
    return monthYear(isoDate, this.prefs.locale());
  }

  protected signedPct(v: number): string {
    return new Intl.NumberFormat(this.prefs.locale(), {
      style: 'percent',
      minimumFractionDigits: 1,
      maximumFractionDigits: 1,
      signDisplay: 'exceptZero',
    }).format(v);
  }

  protected toneChip(v: number): string {
    return v > 0
      ? 'delta bg-emerald-500/10 text-emerald-700 dark:text-emerald-400'
      : v < 0
        ? 'delta bg-rose-500/10 text-rose-600 dark:text-rose-400'
        : 'delta bg-muted text-muted-foreground';
  }

  protected trendIcon(v: number): string {
    return v > 0 ? 'lucideTrendingUp' : v < 0 ? 'lucideTrendingDown' : 'lucideArrowRight';
  }

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
