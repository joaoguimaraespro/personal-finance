import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { HttpClient } from '@angular/common/http';
import { DataEvents } from '../../core/data-events';
import { currentPeriod, today } from '../../core/format';
import { Toasts, problemMessage } from '../../core/toast';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { DateFieldComponent } from '../../shared/date-field';
import { MonthPickerComponent } from '../../shared/month-picker';

type Kind =
  'Month' | 'Year' | 'Period' | 'All' | 'Expenses' | 'Income' | 'Investments' | 'Portfolio';
type Dataset = 'Transactions' | 'Monthly' | 'Positions' | 'Dividends' | 'Trades' | 'NetWorth';

/** Everything can leave the app at any time: Excel, CSV or a complete JSON archive. */
@Component({
  selector: 'app-export',
  imports: [
    MonthPickerComponent,
    DateFieldComponent,
    HlmInputImports,
    HlmButtonImports,
    TranslatePipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mb-6">
      <h1 class="text-2xl font-semibold tracking-tight">{{ 'export.title' | translate }}</h1>
      <p class="text-sm text-muted-foreground">{{ 'export.subtitle' | translate }}</p>
    </div>

    <div class="grid gap-4 lg:grid-cols-2">
      <section class="card space-y-4">
        <h2 class="card-title">{{ 'export.excel' | translate }}</h2>
        <div class="flex flex-wrap gap-2">
          @for (k of kinds; track k) {
            <button class="chip" [class.chip-active]="kind() === k" (click)="kind.set(k)">
              {{ 'export.kinds.' + k | translate }}
            </button>
          }
        </div>
        @if (kind() === 'Month') {
          <app-month-picker [(period)]="period" />
        } @else if (kind() === 'Year') {
          <input
            hlmInput
            class="num !w-32"
            type="number"
            [value]="year()"
            (input)="year.set(+$any($event.target).value)"
          />
        } @else if (kind() === 'Period') {
          <div class="flex gap-2">
            <app-date-field
              [value]="from()"
              [placeholder]="'common.from' | translate"
              (valueChange)="from.set($event)"
              clearable
            />
            <app-date-field
              [value]="to()"
              [placeholder]="'common.to' | translate"
              (valueChange)="to.set($event)"
              clearable
            />
          </div>
        }
        <a hlmBtn [href]="xlsxUrl()" download>{{ 'export.downloadXlsx' | translate }}</a>
      </section>

      <section class="card space-y-4">
        <h2 class="card-title">CSV</h2>
        <div class="flex flex-wrap gap-2">
          @for (d of datasets; track d) {
            <button class="chip" [class.chip-active]="dataset() === d" (click)="dataset.set(d)">
              {{ 'export.datasets.' + d | translate }}
            </button>
          }
        </div>
        <label class="flex items-center gap-2 text-sm text-muted-foreground">
          <input
            type="checkbox"
            [checked]="excelPt()"
            (change)="excelPt.set($any($event.target).checked)"
          />
          {{ 'export.excelPt' | translate }}
        </label>
        <a hlmBtn variant="outline" [href]="csvUrl()" download>{{
          'export.downloadCsv' | translate
        }}</a>
      </section>

      <section class="card space-y-3">
        <h2 class="card-title">{{ 'export.archive' | translate }}</h2>
        <p class="text-sm text-muted-foreground">{{ 'export.archiveHelp' | translate }}</p>
        <a hlmBtn variant="outline" href="/api/exports/json" download>{{
          'export.downloadJson' | translate
        }}</a>
      </section>

      <section class="card space-y-3">
        <h2 class="card-title">{{ 'export.restore' | translate }}</h2>
        <p class="text-sm text-muted-foreground">{{ 'export.restoreHelp' | translate }}</p>
        <input
          type="file"
          accept="application/json,.json"
          class="block text-sm"
          (change)="restore($any($event.target).files?.[0]); $any($event.target).value = ''"
        />
      </section>
    </div>
  `,
})
export class ExportComponent {
  private readonly http = inject(HttpClient);
  private readonly events = inject(DataEvents);
  private readonly toasts = inject(Toasts);
  private readonly i18n = inject(TranslateService);

  protected readonly kinds: Kind[] = [
    'Month',
    'Year',
    'Period',
    'All',
    'Expenses',
    'Income',
    'Investments',
    'Portfolio',
  ];
  protected readonly datasets: Dataset[] = [
    'Transactions',
    'Monthly',
    'Positions',
    'Dividends',
    'Trades',
    'NetWorth',
  ];
  protected readonly kind = signal<Kind>('Year');
  protected readonly period = signal(currentPeriod());
  protected readonly year = signal(new Date().getFullYear());
  protected readonly from = signal(`${new Date().getFullYear()}-01-01`);
  protected readonly to = signal(today());
  protected readonly dataset = signal<Dataset>('Transactions');
  protected readonly excelPt = signal(false);

  protected readonly xlsxUrl = computed(() => {
    const p = new URLSearchParams({ kind: this.kind() });
    if (this.kind() === 'Month') p.set('period', this.period());
    if (this.kind() === 'Year') p.set('year', String(this.year()));
    if (this.kind() === 'Period') {
      p.set('from', this.from());
      p.set('to', this.to());
    }
    return `/api/exports/xlsx?${p}`;
  });

  protected readonly csvUrl = computed(() => {
    const p = new URLSearchParams({ dataset: this.dataset() });
    if (this.excelPt()) p.set('dialect', 'excel-pt');
    return `/api/exports/csv?${p}`;
  });

  protected async restore(file: File | undefined) {
    if (!file) return;
    const form = new FormData();
    form.append('file', file);
    try {
      const r = await firstValueFrom(
        this.http.post<{ transactions: number; accounts: number; skipped: number }>(
          '/api/imports/json',
          form,
        ),
      );
      this.events.bump();
      this.toasts.show(this.i18n.instant('export.restored', r));
    } catch (err) {
      this.toasts.show(problemMessage(err), 'error');
    }
  }
}
