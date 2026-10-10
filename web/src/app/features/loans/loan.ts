import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NgIcon } from '@ng-icons/core';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmTableImports } from '@spartan-ng/helm/table';
import type { EChartsOption } from 'echarts';
import { firstValueFrom } from 'rxjs';
import { Api } from '../../core/api';
import { Confirm } from '../../core/confirm';
import { DataEvents } from '../../core/data-events';
import { DayPipe, MoneyPipe, PercentPipe } from '../../core/format';
import { Instalment, LoanRateType, PrepaymentMode, PrepaymentSimulation } from '../../core/models';
import { Prefs } from '../../core/prefs';
import { liveResource } from '../../core/resource';
import { Toasts } from '../../core/toast';
import { ChartComponent } from '../../shared/chart';
import { baseChart, moneyAxis, moneyTooltip, SERIES_COLORS } from '../../shared/chart-options';
import { DateFieldComponent } from '../../shared/date-field';
import { APP_ICONS } from '../../shared/icons';
import { ModalComponent } from '../../shared/modal';
import { PageHeaderComponent } from '../../shared/page-header';
import { parseAmount } from '../../shared/parse-amount';
import { ProgressComponent } from '../../shared/progress';
import { SelectComponent, SelectOption } from '../../shared/select';

interface YearGroup {
  year: number;
  rows: Instalment[];
  payment: number;
  interest: number;
  principal: number;
  balance: number;
}

/**
 * A loan account's credit: where it stands (debt, instalment, end date, interest left), its amortisation plan, rate
 * revisions, early repayments and a simulator. The plan is computed by the server (French method).
 */
@Component({
  selector: 'app-loan',
  imports: [
    RouterLink,
    NgIcon,
    TranslatePipe,
    HlmButtonImports,
    HlmInputImports,
    HlmTableImports,
    MoneyPipe,
    DayPipe,
    PercentPipe,
    ChartComponent,
    DateFieldComponent,
    ModalComponent,
    PageHeaderComponent,
    ProgressComponent,
    SelectComponent,
  ],
  providers: [APP_ICONS],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-page-header
      icon="lucideHandCoins"
      [title]="accountName() || ('loans.title' | translate)"
      [subtitle]="'loans.subtitle' | translate"
    >
      <div class="flex flex-wrap gap-2">
        <a hlmBtn variant="ghost" routerLink="/accounts">{{ 'nav.accounts' | translate }}</a>
        @if (detail.value()) {
          <button hlmBtn variant="outline" (click)="openTerms()">
            <ng-icon name="lucidePencil" />{{ 'loans.editTerms' | translate }}
          </button>
        }
      </div>
    </app-page-header>

    @if (detail.error() && !detail.value()) {
      <!-- No terms yet: they are what turns a debt into a plan. -->
      <section class="card mx-auto max-w-xl text-center">
        <ng-icon name="lucideHandCoins" class="text-primary mb-2 text-3xl" aria-hidden="true" />
        <h2 class="font-semibold">{{ 'loans.setupTitle' | translate }}</h2>
        <p class="text-muted-foreground mt-1 text-sm">{{ 'loans.setupText' | translate }}</p>
        <button hlmBtn class="mt-4" (click)="openTerms()">
          <ng-icon name="lucidePlus" />{{ 'loans.setup' | translate }}
        </button>
      </section>
    }

    @if (detail.value(); as d) {
      @let s = d.summary;
      <section class="grid grid-cols-2 gap-3 lg:grid-cols-4">
        <div class="card !p-4">
          <p class="text-muted-foreground text-xs">{{ 'loans.outstanding' | translate }}</p>
          <p class="num mt-1 text-2xl font-semibold">{{ s.outstanding | money }}</p>
          <app-progress class="mt-3 block" [value]="s.paidOffShare" />
          <p class="text-muted-foreground num mt-1.5 text-[11px]">
            {{
              'loans.paidOff'
                | translate: { share: (s.paidOffShare | pct: 0), principal: (s.principal | money) }
            }}
          </p>
        </div>
        <div class="card !p-4">
          <p class="text-muted-foreground text-xs">{{ 'loans.nextPayment' | translate }}</p>
          <p class="num mt-1 text-2xl font-semibold">
            {{ s.next ? (s.next.payment | money) : '—' }}
          </p>
          @if (s.next; as n) {
            <p class="text-muted-foreground num mt-2 text-[11px]">
              {{ n.date | day }} · {{ 'loans.interest' | translate }} {{ n.interest | money }} ·
              {{ 'loans.capital' | translate }} {{ n.principal | money }}
            </p>
          }
        </div>
        <div class="card !p-4">
          <p class="text-muted-foreground text-xs">{{ 'loans.ends' | translate }}</p>
          <p class="mt-1 text-2xl font-semibold">{{ s.endDate ? (s.endDate | day) : '—' }}</p>
          <p class="text-muted-foreground num mt-2 text-[11px]">
            {{
              'loans.left'
                | translate: { months: s.instalmentsLeft, time: duration(s.instalmentsLeft) }
            }}
          </p>
        </div>
        <div class="card !p-4">
          <p class="text-muted-foreground text-xs">{{ 'loans.rate' | translate }}</p>
          <p class="num mt-1 text-2xl font-semibold">{{ s.currentRatePercent / 100 | pct: 2 }}</p>
          <p class="text-muted-foreground num mt-2 text-[11px]">
            @if (s.rateType === 'Variable') {
              {{ s.indexName || ('loans.index' | translate) }} +
              {{ (s.spreadPercent ?? 0) / 100 | pct: 2 }}
              @if (s.nextRevision) {
                · {{ 'loans.nextRevision' | translate }} {{ s.nextRevision | day }}
              }
            } @else {
              {{ 'loans.fixed' | translate }}
            }
          </p>
        </div>
      </section>

      <section class="mt-4 grid gap-4 lg:grid-cols-[2fr_1fr]">
        <div class="card flex flex-col">
          <h2 class="card-title">
            <ng-icon name="lucideTrendingDown" />{{ 'loans.debtOverTime' | translate }}
          </h2>
          <app-chart class="min-h-56 flex-1" [option]="chart()" />
          <p class="text-muted-foreground num mt-2 text-xs">
            {{
              'loans.interestSummary'
                | translate: { paid: (s.interestPaid | money), left: (s.interestLeft | money) }
            }}
          </p>
        </div>

        <!-- Simulator: nothing is saved until "Record it". -->
        <div class="card">
          <h2 class="card-title">
            <ng-icon name="lucidePercent" />{{ 'loans.simulator' | translate }}
          </h2>
          <div class="space-y-3">
            <div class="grid grid-cols-2 gap-2">
              <div>
                <label class="label" for="sim-amount">{{ 'loans.amount' | translate }}</label>
                <input
                  id="sim-amount"
                  hlmInput
                  class="num w-full"
                  inputmode="decimal"
                  [value]="simAmount()"
                  (input)="simAmount.set($any($event.target).value)"
                />
              </div>
              <div>
                <label class="label" for="sim-date">{{ 'tx.date' | translate }}</label>
                <app-date-field
                  inputId="sim-date"
                  [value]="simDate()"
                  (valueChange)="simDate.set($event)"
                />
              </div>
            </div>
            <div class="segmented flex w-full">
              @for (m of modes; track m) {
                <button
                  type="button"
                  class="flex-1"
                  [class.active]="simMode() === m"
                  (click)="simMode.set(m)"
                >
                  {{ 'loans.mode.' + m | translate }}
                </button>
              }
            </div>
            <button
              hlmBtn
              variant="outline"
              class="w-full"
              [disabled]="!simAmountValue()"
              (click)="simulate()"
            >
              {{ 'loans.simulate' | translate }}
            </button>
            @if (simulation(); as r) {
              <dl class="bg-muted/50 space-y-1.5 rounded-lg p-3 text-xs">
                <div class="flex justify-between">
                  <dt class="text-muted-foreground">{{ 'loans.interestSaved' | translate }}</dt>
                  <dd class="num tone-pos font-semibold">{{ r.interestSaved | money }}</dd>
                </div>
                @if (simMode() === 'ReduceTerm') {
                  <div class="flex justify-between">
                    <dt class="text-muted-foreground">{{ 'loans.endsEarlier' | translate }}</dt>
                    <dd class="num font-medium">
                      {{ duration(r.monthsSaved) }} ·
                      {{ r.endDateAfter ? (r.endDateAfter | day) : '—' }}
                    </dd>
                  </div>
                } @else {
                  <div class="flex justify-between">
                    <dt class="text-muted-foreground">{{ 'loans.newPayment' | translate }}</dt>
                    <dd class="num font-medium">
                      {{ r.paymentAfter | money }}
                      <span class="text-muted-foreground"
                        >({{ r.paymentAfter - r.paymentBefore | money: 'EUR' : true }})</span
                      >
                    </dd>
                  </div>
                }
                <button hlmBtn size="sm" class="mt-2 w-full" (click)="recordPrepayment()">
                  {{ 'loans.recordIt' | translate }}
                </button>
              </dl>
            }
          </div>
        </div>
      </section>

      <!-- Booking instalments: optional, the debt follows the plan either way. -->
      <section class="card mt-4">
        <div class="grid gap-3 sm:grid-cols-[minmax(0,18rem)_1fr] sm:items-center">
          <div>
            <label class="label" for="ln-payfrom">{{ 'loans.paidFrom' | translate }}</label>
            <app-select
              inputId="ln-payfrom"
              [options]="payFromOptions()"
              [value]="s.paymentAccountId ?? ''"
              (valueChange)="setPayFrom($event)"
            />
          </div>
          <p class="text-muted-foreground text-xs">{{ 'loans.paidFromHint' | translate }}</p>
        </div>
        @if (pendingRows().length) {
          <h3
            class="text-muted-foreground mt-4 mb-2 text-xs font-semibold tracking-wider uppercase"
          >
            {{ 'loans.pending' | translate }}
          </h3>
          <ul class="divide-y text-sm">
            @for (i of pendingRows(); track i.number; let first = $first) {
              <li class="flex flex-wrap items-center justify-between gap-2 py-2">
                <span>
                  {{ i.date | day }}
                  <span class="text-muted-foreground num ml-2 text-xs"
                    >{{ 'loans.interest' | translate }} {{ i.interest | money }} ·
                    {{ 'loans.capital' | translate }} {{ i.principal | money }}</span
                  >
                </span>
                <span class="flex items-center gap-2">
                  <b class="num">{{ i.payment | money }}</b>
                  <button hlmBtn size="sm" [disabled]="!first" (click)="book(i.number)">
                    {{ 'loans.book' | translate }}
                  </button>
                  <button
                    hlmBtn
                    size="sm"
                    variant="ghost"
                    [disabled]="!first"
                    (click)="skip(i.number)"
                  >
                    {{ 'loans.skip' | translate }}
                  </button>
                </span>
              </li>
            }
          </ul>
        }
      </section>

      <section class="mt-4 grid gap-4 md:grid-cols-2">
        <div class="card">
          <div class="mb-3 flex items-center justify-between gap-2">
            <h2 class="card-title !mb-0">
              <ng-icon name="lucidePercent" />{{ 'loans.rates' | translate }}
            </h2>
            <button hlmBtn variant="ghost" size="sm" (click)="rateOpen.set(true)">
              <ng-icon name="lucidePlus" />{{
                (s.rateType === 'Variable' ? 'loans.addRevision' : 'loans.addRate') | translate
              }}
            </button>
          </div>
          <ul class="divide-y text-sm">
            @for (r of d.rates; track r.id) {
              <li class="flex items-center justify-between gap-2 py-2">
                <span>{{ 'loans.from' | translate }} {{ r.effectiveFrom | day }}</span>
                <span class="num flex items-center gap-2">
                  @if (r.indexRatePercent !== null) {
                    <span class="text-muted-foreground text-xs"
                      >{{ r.indexRatePercent / 100 | pct: 3 }} +</span
                    >
                  }
                  <b>{{ r.annualRatePercent / 100 | pct: 3 }}</b>
                  @if (d.rates.length > 1) {
                    <button
                      hlmBtn
                      variant="ghost"
                      size="icon-sm"
                      class="text-muted-foreground hover:text-destructive"
                      [attr.aria-label]="'common.delete' | translate"
                      (click)="removeRate(r.id)"
                    >
                      <ng-icon name="lucideTrash2" />
                    </button>
                  }
                </span>
              </li>
            }
          </ul>
        </div>
        <div class="card">
          <h2 class="card-title">
            <ng-icon name="lucideHandCoins" />{{ 'loans.prepayments' | translate }}
          </h2>
          <ul class="divide-y text-sm">
            @for (p of d.prepayments; track p.id) {
              <li class="flex items-center justify-between gap-2 py-2">
                <span
                  >{{ p.on | day }} ·
                  <span class="text-muted-foreground">{{
                    'loans.mode.' + p.mode | translate
                  }}</span></span
                >
                <span class="num flex items-center gap-2">
                  <b>{{ p.amount | money }}</b>
                  <button
                    hlmBtn
                    variant="ghost"
                    size="icon-sm"
                    class="text-muted-foreground hover:text-destructive"
                    [attr.aria-label]="'common.delete' | translate"
                    (click)="removePrepayment(p.id)"
                  >
                    <ng-icon name="lucideTrash2" />
                  </button>
                </span>
              </li>
            } @empty {
              <li class="text-muted-foreground py-2 text-xs">
                {{ 'loans.noPrepayments' | translate }}
              </li>
            }
          </ul>
        </div>
      </section>

      <section class="card mt-4 overflow-x-auto !p-0">
        <h2 class="card-title px-5 pt-5">
          <ng-icon name="lucideCalendarClock" />{{ 'loans.plan' | translate }}
        </h2>
        <table hlmTable>
          <thead hlmTHead>
            <tr hlmTr>
              <th hlmTh>{{ 'loans.year' | translate }}</th>
              <th hlmTh class="text-right">{{ 'loans.payments' | translate }}</th>
              <th hlmTh class="text-right">{{ 'loans.interest' | translate }}</th>
              <th hlmTh class="text-right">{{ 'loans.capital' | translate }}</th>
              <th hlmTh class="text-right">{{ 'loans.balanceEnd' | translate }}</th>
            </tr>
          </thead>
          <tbody hlmTBody>
            @for (y of years(); track y.year) {
              <tr
                hlmTr
                class="cursor-pointer"
                (click)="toggleYear(y.year)"
                [attr.aria-expanded]="openYears().has(y.year)"
              >
                <td hlmTd class="font-medium">
                  <ng-icon
                    name="lucideChevronDown"
                    class="mr-1 align-[-2px] transition-transform"
                    [class.-rotate-90]="!openYears().has(y.year)"
                    aria-hidden="true"
                  />{{ y.year }}
                  @if (y.year === thisYear) {
                    <span class="badge bg-primary/10 text-primary ml-1">{{
                      'loans.thisYear' | translate
                    }}</span>
                  }
                </td>
                <td hlmTd class="num text-right">{{ y.payment | money }}</td>
                <td hlmTd class="num text-right">{{ y.interest | money }}</td>
                <td hlmTd class="num text-right">{{ y.principal | money }}</td>
                <td hlmTd class="num text-right font-medium">{{ y.balance | money }}</td>
              </tr>
              @if (openYears().has(y.year)) {
                @for (i of y.rows; track i.number) {
                  <tr
                    hlmTr
                    class="text-muted-foreground bg-muted/30 text-xs"
                    [class.font-medium]="i.date === s.next?.date"
                  >
                    <td hlmTd class="pl-9">
                      {{ i.date | day }}
                      @if (i.prepaid) {
                        <span class="badge bg-primary/10 text-primary ml-1"
                          >+{{ i.prepaid | money }}</span
                        >
                      }
                    </td>
                    <td hlmTd class="num text-right">{{ i.payment | money }}</td>
                    <td hlmTd class="num text-right">{{ i.interest | money }}</td>
                    <td hlmTd class="num text-right">{{ i.principal | money }}</td>
                    <td hlmTd class="num text-right">{{ i.balance | money }}</td>
                  </tr>
                }
              }
            }
          </tbody>
        </table>
      </section>
    }

    <app-modal
      [open]="termsOpen()"
      [title]="'loans.terms' | translate"
      width="36rem"
      (closed)="termsOpen.set(false)"
    >
      <form class="grid grid-cols-2 gap-3" (submit)="$event.preventDefault(); saveTerms()">
        <div>
          <label class="label" for="ln-principal">{{ 'loans.principal' | translate }}</label>
          <input
            id="ln-principal"
            hlmInput
            class="num w-full"
            inputmode="decimal"
            [value]="fPrincipal()"
            (input)="fPrincipal.set($any($event.target).value)"
          />
        </div>
        <div>
          <label class="label" for="ln-term">{{ 'loans.termMonths' | translate }}</label>
          <input
            id="ln-term"
            hlmInput
            class="num w-full"
            inputmode="numeric"
            [value]="fTerm()"
            (input)="fTerm.set($any($event.target).value)"
          />
          <p class="text-muted-foreground mt-1 text-[11px]">{{ duration(+fTerm() || 0) }}</p>
        </div>
        <div class="col-span-2">
          <label class="label" for="ln-first">{{ 'loans.firstPayment' | translate }}</label>
          <app-date-field
            inputId="ln-first"
            [value]="fFirst()"
            (valueChange)="fFirst.set($event)"
          />
        </div>
        <div class="col-span-2 segmented flex w-full">
          @for (t of rateTypes; track t) {
            <button
              type="button"
              class="flex-1"
              [class.active]="fType() === t"
              (click)="fType.set(t)"
            >
              {{ 'loans.rateType.' + t | translate }}
            </button>
          }
        </div>
        @if (fType() === 'Variable') {
          <div>
            <label class="label" for="ln-index">{{ 'loans.indexName' | translate }}</label>
            <input
              id="ln-index"
              hlmInput
              class="w-full"
              placeholder="Euribor 12M"
              [value]="fIndexName()"
              (input)="fIndexName.set($any($event.target).value)"
            />
          </div>
          <div>
            <label class="label" for="ln-revision">{{ 'loans.revisionMonths' | translate }}</label>
            <app-select
              inputId="ln-revision"
              [options]="revisionOptions()"
              [value]="fRevision()"
              (valueChange)="fRevision.set($event)"
            />
          </div>
          <div>
            <label class="label" for="ln-spread">{{ 'loans.spread' | translate }}</label>
            <input
              id="ln-spread"
              hlmInput
              class="num w-full"
              inputmode="decimal"
              [value]="fSpread()"
              (input)="fSpread.set($any($event.target).value)"
            />
          </div>
          <div>
            <label class="label" for="ln-index-value">{{ 'loans.indexStart' | translate }}</label>
            <input
              id="ln-index-value"
              hlmInput
              class="num w-full"
              inputmode="decimal"
              [value]="fIndexValue()"
              (input)="fIndexValue.set($any($event.target).value)"
            />
          </div>
          <p class="text-muted-foreground col-span-2 text-xs">
            {{ 'loans.tanIs' | translate: { rate: (variableTan() / 100 | pct: 3) } }}
          </p>
        } @else {
          <div class="col-span-2">
            <label class="label" for="ln-rate">{{ 'loans.tanStart' | translate }}</label>
            <input
              id="ln-rate"
              hlmInput
              class="num w-full"
              inputmode="decimal"
              [value]="fRate()"
              (input)="fRate.set($any($event.target).value)"
            />
          </div>
        }
        <p class="text-muted-foreground col-span-2 flex gap-1.5 text-xs">
          <ng-icon name="lucideInfo" class="mt-px shrink-0" aria-hidden="true" />{{
            'loans.termsHint' | translate
          }}
        </p>
        <div class="col-span-2 flex justify-end gap-2">
          <button type="button" hlmBtn variant="outline" (click)="termsOpen.set(false)">
            {{ 'common.cancel' | translate }}
          </button>
          <button hlmBtn><ng-icon name="lucideSave" />{{ 'common.save' | translate }}</button>
        </div>
      </form>
    </app-modal>

    <app-modal
      [open]="rateOpen()"
      [title]="'loans.addRevision' | translate"
      (closed)="rateOpen.set(false)"
    >
      <form class="space-y-3" (submit)="$event.preventDefault(); addRate()">
        <div>
          <label class="label" for="rt-from">{{ 'loans.from' | translate }}</label>
          <app-date-field inputId="rt-from" [value]="rFrom()" (valueChange)="rFrom.set($event)" />
        </div>
        <div>
          <label class="label" for="rt-value">{{
            (isVariable() ? 'loans.indexValue' : 'loans.tan') | translate
          }}</label>
          <input
            id="rt-value"
            hlmInput
            class="num w-full"
            inputmode="decimal"
            [value]="rValue()"
            (input)="rValue.set($any($event.target).value)"
          />
          @if (isVariable()) {
            <p class="text-muted-foreground mt-1 text-xs">{{ 'loans.spreadAdded' | translate }}</p>
          }
        </div>
        <div class="flex justify-end gap-2">
          <button type="button" hlmBtn variant="outline" (click)="rateOpen.set(false)">
            {{ 'common.cancel' | translate }}
          </button>
          <button hlmBtn><ng-icon name="lucideSave" />{{ 'common.save' | translate }}</button>
        </div>
      </form>
    </app-modal>
  `,
})
export class LoanComponent {
  readonly accountId = input.required<string>();
  private readonly api = inject(Api);
  private readonly events = inject(DataEvents);
  private readonly toasts = inject(Toasts);
  private readonly confirm = inject(Confirm);
  private readonly i18n = inject(TranslateService);
  private readonly prefs = inject(Prefs);

  protected readonly modes: PrepaymentMode[] = ['ReduceTerm', 'ReducePayment'];
  protected readonly rateTypes: LoanRateType[] = ['Fixed', 'Variable'];
  protected readonly thisYear = new Date().getFullYear();

  protected readonly detail = liveResource({
    params: () => ({ id: this.accountId(), v: this.events.version() }),
    stream: ({ params }) => this.api.loan(params.id),
  });
  private readonly accounts = liveResource({ stream: () => this.api.accounts() });
  protected readonly accountName = computed(
    () =>
      this.detail.value()?.summary.accountName ??
      (this.accounts.value() ?? []).find((a) => a.id === this.accountId())?.name ??
      '',
  );
  protected readonly isVariable = computed(
    () => this.detail.value()?.summary.rateType === 'Variable',
  );

  /** The plan by year (this year open), so 30 years stay readable. */
  protected readonly years = computed<YearGroup[]>(() => {
    const groups = new Map<number, YearGroup>();
    for (const i of this.detail.value()?.plan ?? []) {
      const year = Number(i.date.slice(0, 4));
      const g = groups.get(year) ?? {
        year,
        rows: [],
        payment: 0,
        interest: 0,
        principal: 0,
        balance: 0,
      };
      g.rows.push(i);
      g.payment += i.payment + i.prepaid;
      g.interest += i.interest;
      g.principal += i.principal + i.prepaid;
      g.balance = i.balance;
      groups.set(year, g);
    }
    return [...groups.values()];
  });
  protected readonly openYears = signal<ReadonlySet<number>>(new Set([new Date().getFullYear()]));

  protected readonly chart = computed<EChartsOption>(() => {
    const plan = this.detail.value()?.plan ?? [];
    this.prefs.translations();
    return {
      ...baseChart,
      legend: { show: false },
      tooltip: moneyTooltip(this.prefs.locale()),
      xAxis: {
        type: 'time',
        axisLabel: { color: '#a1a1aa', fontSize: 11 },
        splitLine: { show: false },
      },
      yAxis: moneyAxis(this.prefs.locale()),
      series: [
        {
          name: this.i18n.instant('loans.outstanding'),
          type: 'line',
          showSymbol: false,
          areaStyle: { opacity: 0.12 },
          data: plan.map((i) => [i.date, i.balance]),
          itemStyle: { color: SERIES_COLORS.expenses },
          markLine: {
            symbol: 'none',
            label: { show: false },
            lineStyle: { color: SERIES_COLORS.muted, type: 'dashed' },
            data: [{ xAxis: new Date().toISOString().slice(0, 10) }],
          },
        },
      ],
    };
  });

  /** Bank, savings or cash accounts the instalments can come out of. */
  protected readonly payFromOptions = computed<SelectOption[]>(() => {
    this.prefs.translations();
    return [
      { value: '', label: this.i18n.instant('loans.notTracked') },
      ...(this.accounts.value() ?? [])
        .filter((a) => ['Bank', 'Savings', 'Cash'].includes(a.kind) && !a.archived)
        .map((a) => ({ value: a.id, label: a.name })),
    ];
  });

  /** The instalments due and not booked yet (the oldest ones the server counts as pending). */
  protected readonly pendingRows = computed(() => {
    const d = this.detail.value();
    const count = d?.summary.pendingInstalments ?? 0;
    if (!d || !count) return [];
    const today = new Date().toISOString().slice(0, 10);
    const due = d.plan.filter((i) => i.date <= today && i.payment > 0);
    return due.slice(Math.max(due.length - count, 0));
  });

  protected async setPayFrom(id: string) {
    await this.run(this.api.setLoanPaymentAccount(this.accountId(), id || null));
  }

  protected async book(number: number) {
    await this.run(this.api.bookInstalment(this.accountId(), number));
  }

  protected async skip(number: number) {
    await this.run(this.api.skipInstalment(this.accountId(), number));
  }

  // Simulator
  protected readonly simAmount = signal('');
  protected readonly simDate = signal(new Date().toISOString().slice(0, 10));
  protected readonly simMode = signal<PrepaymentMode>('ReduceTerm');
  protected readonly simAmountValue = computed(() => parseAmount(this.simAmount()) ?? 0);
  protected readonly simulation = signal<PrepaymentSimulation | null>(null);

  // Terms form
  protected readonly termsOpen = signal(false);
  protected readonly fPrincipal = signal('');
  protected readonly fTerm = signal('');
  protected readonly fFirst = signal('');
  protected readonly fType = signal<LoanRateType>('Fixed');
  protected readonly fRate = signal('');
  protected readonly fIndexName = signal('');
  protected readonly fRevision = signal('12');
  protected readonly fSpread = signal('');
  protected readonly fIndexValue = signal('');
  protected readonly variableTan = computed(
    () => (parseAmount(this.fIndexValue()) ?? 0) + (parseAmount(this.fSpread()) ?? 0),
  );
  protected readonly revisionOptions = computed<SelectOption[]>(() => {
    this.prefs.translations();
    return [1, 3, 6, 12].map((m) => ({
      value: String(m),
      label: this.i18n.instant('loans.everyMonths', { months: m }),
    }));
  });

  // Rate revision form
  protected readonly rateOpen = signal(false);
  protected readonly rFrom = signal(new Date().toISOString().slice(0, 10));
  protected readonly rValue = signal('');

  /** "28 years 7 months" from a number of months. */
  protected duration(months: number): string {
    const y = Math.floor(months / 12);
    const m = months % 12;
    const parts: string[] = [];
    if (y) parts.push(this.i18n.instant('loans.years', { count: y }));
    if (m || !y) parts.push(this.i18n.instant('loans.months', { count: m }));
    return parts.join(' ');
  }

  protected toggleYear(year: number) {
    this.openYears.update((s) => {
      const next = new Set(s);
      if (next.has(year)) next.delete(year);
      else next.add(year);
      return next;
    });
  }

  protected openTerms() {
    const s = this.detail.value()?.summary;
    const firstRate = this.detail.value()?.rates[0];
    this.fPrincipal.set(s ? String(s.principal).replace('.', ',') : '');
    this.fTerm.set(s ? String(s.termMonths) : '360');
    this.fFirst.set(s?.firstPaymentOn ?? new Date().toISOString().slice(0, 10));
    this.fType.set(s?.rateType ?? 'Fixed');
    this.fRate.set(firstRate ? String(firstRate.annualRatePercent).replace('.', ',') : '');
    this.fIndexName.set(s?.indexName ?? '');
    this.fRevision.set(String(s?.revisionMonths ?? 12));
    this.fSpread.set(s?.spreadPercent != null ? String(s.spreadPercent).replace('.', ',') : '');
    this.fIndexValue.set(
      firstRate?.indexRatePercent != null
        ? String(firstRate.indexRatePercent).replace('.', ',')
        : '',
    );
    this.termsOpen.set(true);
  }

  protected async saveTerms() {
    const variable = this.fType() === 'Variable';
    const body = {
      principal: parseAmount(this.fPrincipal()),
      firstPaymentOn: this.fFirst(),
      termMonths: Number(this.fTerm()),
      rateType: this.fType(),
      revisionMonths: variable ? Number(this.fRevision()) : null,
      indexName: variable ? this.fIndexName() || null : null,
      spreadPercent: variable ? parseAmount(this.fSpread()) : null,
      initialRatePercent: variable ? null : parseAmount(this.fRate()),
      initialIndexPercent: variable ? parseAmount(this.fIndexValue()) : null,
    };
    await this.run(this.api.saveLoan(this.accountId(), body), () => this.termsOpen.set(false));
  }

  protected async addRate() {
    const value = parseAmount(this.rValue());
    if (value === null) return;
    const body = this.isVariable()
      ? { effectiveFrom: this.rFrom(), indexRatePercent: value }
      : { effectiveFrom: this.rFrom(), annualRatePercent: value };
    await this.run(this.api.addLoanRate(this.accountId(), body), () => this.rateOpen.set(false));
  }

  protected async removeRate(id: string) {
    if (!(await this.confirm.ask(this.i18n.instant('loans.confirmRemoveRate')))) return;
    await this.run(this.api.deleteLoanRate(this.accountId(), id));
  }

  protected async removePrepayment(id: string) {
    if (!(await this.confirm.ask(this.i18n.instant('loans.confirmRemovePrepayment')))) return;
    await this.run(this.api.deletePrepayment(this.accountId(), id));
  }

  protected async simulate() {
    try {
      this.simulation.set(
        await firstValueFrom(
          this.api.simulatePrepayment(this.accountId(), {
            on: this.simDate(),
            amount: this.simAmountValue(),
            mode: this.simMode(),
          }),
        ),
      );
    } catch (err) {
      this.toasts.error(err);
    }
  }

  protected async recordPrepayment() {
    await this.run(
      this.api.addPrepayment(this.accountId(), {
        on: this.simDate(),
        amount: this.simAmountValue(),
        mode: this.simMode(),
      }),
      () => this.simulation.set(null),
    );
  }

  private async run(request: ReturnType<Api['saveLoan']>, after?: () => void) {
    try {
      await firstValueFrom(request);
      after?.();
      this.events.bump();
      this.toasts.show(this.i18n.instant('common.saved'));
    } catch (err) {
      this.toasts.error(err);
    }
  }
}
