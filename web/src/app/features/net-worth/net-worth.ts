import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import type { EChartsOption } from 'echarts';
import { firstValueFrom } from 'rxjs';
import { Api } from '../../core/api';
import { DataEvents } from '../../core/data-events';
import { DayPipe, MoneyPipe, PercentPipe, today } from '../../core/format';
import { ManualAsset, ManualAssetKind } from '../../core/models';
import { Prefs } from '../../core/prefs';
import { Toasts } from '../../core/toast';
import { ChartComponent } from '../../shared/chart';
import { SERIES_COLORS, baseChart, moneyAxis, moneyTooltip } from '../../shared/chart-options';
import { KpiComponent } from '../../shared/kpi';
import { ModalComponent } from '../../shared/modal';
import { parseAmount } from '../transactions/quick-add';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { UiSelect } from '../../shared/select';
import { DateFieldComponent } from '../../shared/date-field';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucidePlus } from '@ng-icons/lucide';

@Component({
  selector: 'app-net-worth',
  imports: [
    NgIcon,
    DateFieldComponent,
    UiSelect,
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
  ],
  providers: [provideIcons({ lucidePlus })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mb-6 flex flex-wrap items-center justify-between gap-3">
      <h1 class="text-2xl font-semibold tracking-tight">{{ 'nav.netWorth' | translate }}</h1>
      <button hlmBtn (click)="open()">
        <ng-icon name="lucidePlus" />{{ 'netWorth.addAsset' | translate }}
      </button>
    </div>

    @if (data.value(); as d) {
      <section class="card mb-6 flex flex-wrap items-end justify-between gap-6">
        <div>
          <p class="text-sm text-muted-foreground">{{ 'nav.netWorth' | translate }}</p>
          <p class="num text-4xl font-semibold tracking-tight">{{ d.current.netWorth | money }}</p>
          @if (d.changeSinceStartPercent !== null) {
            <p
              class="num mt-1 text-sm"
              [class]="(d.changeSinceStart ?? 0) >= 0 ? 'text-emerald-600' : 'text-rose-600'"
            >
              {{ (d.changeSinceStart ?? 0) >= 0 ? '+' : ''
              }}{{ d.changeSinceStartPercent | pct }} ({{
                d.changeSinceStart | money: 'EUR' : true
              }}) {{ 'netWorth.since' | translate }} {{ d.startDate | day }}
            </p>
          }
        </div>
        <div class="grid grid-cols-2 gap-3 md:grid-cols-4">
          <app-kpi
            [label]="'netWorth.cash' | translate"
            [value]="d.current.cash"
            [color]="colors.saved"
          />
          <app-kpi
            [label]="'netWorth.investments' | translate"
            [value]="d.current.investments"
            [color]="colors.invested"
          />
          <app-kpi
            [label]="'netWorth.manual' | translate"
            [value]="d.current.manualAssets"
            color="#14b8a6"
          />
          <app-kpi
            [label]="'netWorth.liabilities' | translate"
            [value]="d.current.liabilities"
            [color]="colors.expenses"
          />
        </div>
      </section>

      <section class="grid gap-4 lg:grid-cols-[2fr_1fr]">
        <div class="card">
          <h2 class="card-title">{{ 'netWorth.history' | translate }}</h2>
          <app-chart class="h-72" [option]="chart()" />
        </div>
        <div class="card !p-0">
          <h2 class="card-title px-5 pt-5">{{ 'netWorth.breakdown' | translate }}</h2>
          <table hlmTable>
            <tbody hlmTBody>
              @for (l of d.current.lines; track $index) {
                <tr hlmTr>
                  <td hlmTd>
                    {{ l.name }}
                    <span class="text-xs text-muted-foreground"
                      >· {{ 'netWorth.group.' + l.group | translate }}</span
                    >
                  </td>
                  <td hlmTd class="num text-right" [class.text-rose-600]="l.group === 'liability'">
                    {{ l.group === 'liability' ? '−' : '' }}{{ l.value | money }}
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      </section>
    }

    <section class="card mt-6 overflow-x-auto !p-0">
      <h2 class="card-title px-5 pt-5">{{ 'netWorth.manualTitle' | translate }}</h2>
      <table hlmTable>
        <thead hlmTHead>
          <tr hlmTr>
            <th hlmTh>{{ 'common.name' | translate }}</th>
            <th hlmTh>{{ 'accounts.kind' | translate }}</th>
            <th hlmTh class="text-right">{{ 'netWorth.value' | translate }}</th>
            <th hlmTh>{{ 'netWorth.valuedOn' | translate }}</th>
            <th hlmTh></th>
          </tr>
        </thead>
        <tbody hlmTBody>
          @for (a of assets.value() ?? []; track a.id) {
            <tr hlmTr>
              <td hlmTd class="font-medium">{{ a.name }}</td>
              <td hlmTd class="text-sm text-muted-foreground">
                {{ 'assetKind.' + a.kind | translate }}
              </td>
              <td hlmTd class="num text-right" [class.text-rose-600]="a.isLiability">
                {{ a.currentValue | money: a.currency }}
              </td>
              <td hlmTd class="text-sm text-muted-foreground">{{ a.valuedOn | day }}</td>
              <td hlmTd class="text-right whitespace-nowrap">
                <button hlmBtn variant="ghost" size="sm" (click)="revalue(a)">
                  {{ 'netWorth.update' | translate }}
                </button>
                <button hlmBtn variant="ghost" size="sm" (click)="archive(a)">
                  {{ 'common.archive' | translate }}
                </button>
              </td>
            </tr>
          } @empty {
            <tr hlmTr>
              <td hlmTd colspan="5" class="py-8 text-center text-muted-foreground">
                {{ 'netWorth.noManual' | translate }}
              </td>
            </tr>
          }
        </tbody>
      </table>
    </section>

    <app-modal
      [open]="formOpen()"
      [title]="(editing() ? 'netWorth.update' : 'netWorth.addAsset') | translate"
      (closed)="formOpen.set(false)"
    >
      <form class="grid grid-cols-2 gap-3" (submit)="$event.preventDefault(); save()">
        @if (!editing()) {
          <div class="col-span-2">
            <label class="label" for="m-name">{{ 'common.name' | translate }}</label>
            <input
              id="m-name"
              hlmInput
              required
              maxlength="80"
              (input)="name.set($any($event.target).value)"
            />
          </div>
          <div>
            <label class="label" for="m-kind">{{ 'accounts.kind' | translate }}</label>
            <select
              id="m-kind"
              uiSelect
              [value]="kind()"
              (change)="kind.set($any($event.target).value)"
            >
              @for (k of kinds; track k) {
                <option [value]="k">{{ 'assetKind.' + k | translate }}</option>
              }
            </select>
          </div>
          <div>
            <label class="label" for="m-cur">{{ 'tx.currency' | translate }}</label>
            <input
              id="m-cur"
              hlmInput
              class="uppercase"
              maxlength="3"
              [value]="currency()"
              (input)="currency.set($any($event.target).value.toUpperCase())"
            />
          </div>
        }
        <div>
          <label class="label" for="m-val">{{ 'netWorth.value' | translate }}</label>
          <input
            id="m-val"
            hlmInput
            class="num"
            inputmode="decimal"
            required
            (input)="value.set($any($event.target).value)"
          />
        </div>
        <div>
          <label class="label" for="m-on">{{ 'netWorth.valuedOn' | translate }}</label>
          <app-date-field inputId="m-on" [value]="on()" (valueChange)="on.set($event)" />
        </div>
        <p class="col-span-2 text-xs text-muted-foreground">
          {{ 'netWorth.liabilityHint' | translate }}
        </p>
        <div class="col-span-2 flex justify-end gap-2">
          <button type="button" hlmBtn variant="outline" (click)="formOpen.set(false)">
            {{ 'common.cancel' | translate }}
          </button>
          <button hlmBtn>{{ 'common.save' | translate }}</button>
        </div>
      </form>
    </app-modal>
  `,
})
export class NetWorthComponent {
  private readonly api = inject(Api);
  private readonly events = inject(DataEvents);
  private readonly prefs = inject(Prefs);
  private readonly toasts = inject(Toasts);
  private readonly i18n = inject(TranslateService);
  protected readonly colors = SERIES_COLORS;
  protected readonly kinds: ManualAssetKind[] = [
    'RealEstate',
    'Vehicle',
    'Crypto',
    'Pension',
    'Other',
    'Mortgage',
    'Loan',
    'OtherDebt',
  ];

  protected readonly data = rxResource({
    params: () => this.events.version(),
    stream: () => this.api.netWorth(),
  });
  protected readonly assets = rxResource({
    params: () => this.events.version(),
    stream: () => this.api.manualAssets(),
  });

  protected readonly formOpen = signal(false);
  protected readonly editing = signal<ManualAsset | null>(null);
  protected readonly name = signal('');
  protected readonly kind = signal<ManualAssetKind>('RealEstate');
  protected readonly currency = signal('EUR');
  protected readonly value = signal('');
  protected readonly on = signal(today());

  protected readonly chart = computed<EChartsOption>(() => {
    const series = this.data.value()?.series ?? [];
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
          name: this.i18n.instant('nav.netWorth'),
          type: 'line',
          showSymbol: series.length < 3,
          areaStyle: { opacity: 0.12 },
          data: series.map((p) => [p.date, p.netWorth]),
          itemStyle: { color: SERIES_COLORS.net },
        },
        {
          name: this.i18n.instant('netWorth.liabilities'),
          type: 'line',
          showSymbol: false,
          data: series.map((p) => [p.date, p.liabilities]),
          itemStyle: { color: SERIES_COLORS.expenses },
          lineStyle: { type: 'dashed' },
        },
      ],
    };
  });

  protected open() {
    this.editing.set(null);
    this.name.set('');
    this.value.set('');
    this.on.set(today());
    this.formOpen.set(true);
  }

  protected revalue(a: ManualAsset) {
    this.editing.set(a);
    this.value.set('');
    this.on.set(today());
    this.formOpen.set(true);
  }

  protected async save() {
    const value = parseAmount(this.value());
    if (value === null) return;
    try {
      const a = this.editing();
      if (a) await firstValueFrom(this.api.valueManualAsset(a.id, value, this.on()));
      else
        await firstValueFrom(
          this.api.createManualAsset({
            name: this.name(),
            kind: this.kind(),
            currency: this.currency(),
            value,
            valuedOn: this.on(),
          }),
        );
      this.formOpen.set(false);
      this.events.bump();
    } catch (err) {
      this.toasts.error(err);
    }
  }

  protected async archive(a: ManualAsset) {
    try {
      await firstValueFrom(this.api.archiveManualAsset(a.id));
      this.events.bump();
    } catch (err) {
      this.toasts.error(err);
    }
  }
}
