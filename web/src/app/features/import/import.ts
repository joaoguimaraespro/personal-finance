import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { liveResource } from '../../core/resource';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { Api } from '../../core/api';
import { DataEvents } from '../../core/data-events';
import { DayPipe, MoneyPipe, MonthNamePipe } from '../../core/format';
import { ImportPreview } from '../../core/models';
import { Prefs } from '../../core/prefs';
import { Toasts, problemMessage } from '../../core/toast';
import { categoryLabel } from '../../shared/category-label';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { SelectComponent, SelectOption } from '../../shared/select';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideCheck,
  lucideCircleCheck,
  lucideCircleX,
  lucideTriangleAlert,
  lucideX,
} from '@ng-icons/lucide';

type Step = 'upload' | 'map' | 'done';

/** Analyze → Map → Validate → Preview → Import. Nothing touches the ledger until "Import" is pressed. */
@Component({
  selector: 'app-import',
  imports: [
    NgIcon,
    SelectComponent,
    HlmTableImports,
    HlmInputImports,
    HlmButtonImports,
    TranslatePipe,
    MoneyPipe,
    MonthNamePipe,
    DayPipe,
  ],
  providers: [
    provideIcons({ lucideCheck, lucideCircleCheck, lucideCircleX, lucideTriangleAlert, lucideX }),
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mb-6">
      <h1 class="text-2xl font-semibold tracking-tight">{{ 'import.title' | translate }}</h1>
      <p class="text-sm text-muted-foreground">{{ 'import.subtitle' | translate }}</p>
    </div>

    <ol class="mb-6 flex flex-wrap gap-2 text-xs font-medium">
      @for (s of stepsList; track s; let i = $index) {
        <li
          class="rounded-full px-3 py-1"
          [class]="
            stepIndex() >= i
              ? 'bg-primary text-primary-foreground'
              : 'bg-muted text-muted-foreground'
          "
        >
          {{ i + 1 }}. {{ 'import.steps.' + s | translate }}
        </li>
      }
    </ol>

    @switch (step()) {
      @case ('upload') {
        <section class="card max-w-xl space-y-4">
          <p class="text-sm text-muted-foreground">{{ 'import.uploadHelp' | translate }}</p>
          <div>
            <label class="label" for="i-year">{{ 'import.year' | translate }}</label>
            <input
              id="i-year"
              hlmInput
              class="num !w-32"
              type="number"
              min="1970"
              max="2100"
              [value]="year()"
              (input)="year.set(+$any($event.target).value)"
            />
          </div>
          <input
            class="block w-full text-sm"
            type="file"
            accept=".xlsx,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
            (change)="file.set($any($event.target).files?.[0] ?? null)"
          />
          <p class="text-xs text-muted-foreground">{{ 'import.privacy' | translate }}</p>
          @if (error()) {
            <p class="text-sm text-rose-600">{{ error() }}</p>
          }
          <button hlmBtn [disabled]="!file() || busy()" (click)="analyze()">
            {{ 'import.analyze' | translate }}
          </button>
        </section>
      }
      @case ('map') {
        @if (preview(); as p) {
          <div class="grid gap-4 xl:grid-cols-[1fr_1fr]">
            <section class="card space-y-3">
              <h2 class="card-title">{{ 'import.mapping' | translate }}</h2>
              <div class="grid grid-cols-2 gap-3">
                <div>
                  <label class="label" for="m-year">{{ 'import.year' | translate }}</label>
                  <input
                    id="m-year"
                    hlmInput
                    class="num"
                    type="number"
                    [value]="p.preview.mapping.year"
                    (change)="remap({ year: +$any($event.target).value })"
                  />
                </div>
                <div>
                  <label class="label" for="m-main">{{ 'import.mainAccount' | translate }}</label>
                  <app-select
                    inputId="m-main"
                    [options]="mainAccountOptions()"
                    [value]="p.preview.mapping.mainAccountId ?? ''"
                    (valueChange)="remap({ mainAccountId: $event || null })"
                  />
                </div>
                <div>
                  <label class="label" for="m-inv">{{
                    'import.investmentAccount' | translate
                  }}</label>
                  <app-select
                    inputId="m-inv"
                    [options]="optionalAccountOptions()"
                    [value]="p.preview.mapping.investmentAccountId ?? ''"
                    (valueChange)="remap({ investmentAccountId: $event || null })"
                  />
                </div>
                <div>
                  <label class="label" for="m-sav">{{ 'import.savingsAccount' | translate }}</label>
                  <app-select
                    inputId="m-sav"
                    [options]="optionalAccountOptions()"
                    [value]="p.preview.mapping.savingsAccountId ?? ''"
                    (valueChange)="remap({ savingsAccountId: $event || null })"
                  />
                </div>
              </div>
              @if (!(accounts.value() ?? []).length) {
                <p
                  class="rounded-xl bg-amber-50 p-3 text-sm text-amber-800 dark:bg-amber-500/10 dark:text-amber-300"
                >
                  {{ 'import.needAccount' | translate }}
                </p>
              }
              <h3 class="pt-2 text-sm font-semibold">{{ 'import.categoryMapping' | translate }}</h3>
              <div class="space-y-2">
                @for (m of p.workbookCategories; track m.label) {
                  <div class="grid grid-cols-2 items-center gap-2">
                    <span class="truncate text-sm">{{ m.label }}</span>
                    <app-select
                      [options]="categoryOptions()"
                      [value]="m.categoryId"
                      [ariaLabel]="m.label"
                      (valueChange)="remapCategory(m.label, $event)"
                    />
                  </div>
                }
              </div>
            </section>

            <section class="space-y-4">
              <div class="card">
                <h2 class="card-title">{{ 'import.validation' | translate }}</h2>
                <ul class="space-y-1 text-sm">
                  <li>
                    {{
                      'import.transactionsFound'
                        | translate: { count: p.preview.transactions.length }
                    }}
                  </li>
                  <li>
                    @if (p.preview.reconciled) {
                      <span class="inline-flex items-center gap-1.5 text-emerald-600"
                        ><ng-icon name="lucideCircleCheck" />{{
                          'import.reconciled' | translate
                        }}</span
                      >
                    } @else {
                      <span class="inline-flex items-center gap-1.5 text-amber-600"
                        ><ng-icon name="lucideTriangleAlert" />{{
                          'import.notReconciled' | translate
                        }}</span
                      >
                    }
                  </li>
                  @if (p.preview.budget; as b) {
                    <li class="text-muted-foreground">
                      {{
                        'import.budgetFound'
                          | translate
                            : {
                                stocks: b.stocks * 100,
                                crypto: b.crypto * 100,
                                travel: b.travel * 100,
                                other: b.otherSavings * 100,
                              }
                      }}
                    </li>
                  }
                  @for (e of p.preview.errors; track e) {
                    <li class="flex items-center gap-1.5 text-rose-600">
                      <ng-icon name="lucideCircleX" />{{ e }}
                    </li>
                  }
                  @for (w of p.preview.warnings; track w) {
                    <li class="flex items-center gap-1.5 text-amber-600">
                      <ng-icon name="lucideTriangleAlert" />{{ w }}
                    </li>
                  }
                </ul>
              </div>

              <div class="card overflow-x-auto !p-0">
                <h2 class="card-title px-5 pt-5">{{ 'import.reconciliation' | translate }}</h2>
                <table hlmTable>
                  <thead hlmTHead>
                    <tr hlmTr>
                      <th hlmTh>{{ 'common.month' | translate }}</th>
                      <th hlmTh>{{ 'import.measure' | translate }}</th>
                      <th hlmTh class="text-right">Excel</th>
                      <th hlmTh class="text-right">{{ 'import.imported' | translate }}</th>
                      <th hlmTh></th>
                    </tr>
                  </thead>
                  <tbody hlmTBody>
                    @for (r of p.preview.reconciliation; track $index) {
                      <tr hlmTr>
                        <td hlmTd>{{ r.month | monthName: 'short' }}</td>
                        <td hlmTd class="text-xs">
                          {{ 'import.measures.' + r.measure | translate }}
                        </td>
                        <td hlmTd class="num text-right">{{ r.workbook | money }}</td>
                        <td hlmTd class="num text-right">{{ r.imported | money }}</td>
                        <td hlmTd>
                          @if (r.matches) {
                            <ng-icon name="lucideCheck" class="text-emerald-600" />
                          } @else {
                            <ng-icon name="lucideX" class="text-rose-600" />
                          }
                        </td>
                      </tr>
                    }
                  </tbody>
                </table>
              </div>

              <div class="flex justify-end gap-2">
                <button hlmBtn variant="outline" (click)="reset()">
                  {{ 'common.cancel' | translate }}
                </button>
                <button hlmBtn [disabled]="!p.preview.canCommit || busy()" (click)="commit()">
                  {{ 'import.commit' | translate }}
                </button>
              </div>
            </section>
          </div>

          <section class="card mt-4 overflow-x-auto !p-0">
            <h2 class="card-title px-5 pt-5">{{ 'import.preview' | translate }}</h2>
            <table hlmTable>
              <thead hlmTHead>
                <tr hlmTr>
                  <th hlmTh>{{ 'tx.date' | translate }}</th>
                  <th hlmTh>{{ 'tx.type' | translate }}</th>
                  <th hlmTh>{{ 'tx.description' | translate }}</th>
                  <th hlmTh class="text-right">{{ 'tx.amount' | translate }}</th>
                </tr>
              </thead>
              <tbody hlmTBody>
                @for (t of p.preview.transactions.slice(0, 200); track t.externalId) {
                  <tr hlmTr>
                    <td hlmTd class="text-muted-foreground">{{ t.occurredOn | day: 'short' }}</td>
                    <td hlmTd class="text-xs">
                      {{ 'type.' + t.type | translate }}
                      @if (t.nature) {
                        · {{ 'nature.' + t.nature | translate }}
                      }
                    </td>
                    <td hlmTd class="whitespace-normal">{{ t.description }}</td>
                    <td hlmTd class="num text-right">{{ t.amount | money }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </section>
        }
      }
      @case ('done') {
        <section class="card max-w-xl space-y-3">
          <p class="flex items-center gap-2 text-lg font-semibold text-emerald-600">
            <ng-icon name="lucideCircleCheck" />{{
              'import.done' | translate: { created: result()?.created, skipped: result()?.skipped }
            }}
          </p>
          <button hlmBtn variant="outline" (click)="reset()">
            {{ 'import.another' | translate }}
          </button>
        </section>
      }
    }

    <section class="card mt-8 overflow-x-auto !p-0">
      <h2 class="card-title px-5 pt-5">{{ 'import.history' | translate }}</h2>
      <table hlmTable>
        <thead hlmTHead>
          <tr hlmTr>
            <th hlmTh>{{ 'import.file' | translate }}</th>
            <th hlmTh>{{ 'common.status' | translate }}</th>
            <th hlmTh class="text-right">{{ 'import.created' | translate }}</th>
            <th hlmTh>{{ 'tx.date' | translate }}</th>
            <th hlmTh></th>
          </tr>
        </thead>
        <tbody hlmTBody>
          @for (h of history.value() ?? []; track h.id) {
            <tr hlmTr>
              <td hlmTd>{{ h.fileName }}</td>
              <td hlmTd>
                <span class="badge bg-muted">{{ 'importStatus.' + h.status | translate }}</span>
              </td>
              <td hlmTd class="num text-right">{{ h.created }}</td>
              <td hlmTd class="text-muted-foreground">{{ h.createdAtUtc | day }}</td>
              <td hlmTd class="text-right">
                @if (h.status === 'Committed') {
                  <button
                    hlmBtn
                    variant="ghost"
                    size="sm"
                    class="text-destructive hover:text-destructive"
                    (click)="undo(h.id)"
                  >
                    {{ 'import.undo' | translate }}
                  </button>
                }
              </td>
            </tr>
          }
        </tbody>
      </table>
    </section>
  `,
})
export class ImportComponent {
  private readonly api = inject(Api);
  private readonly events = inject(DataEvents);
  private readonly toasts = inject(Toasts);
  private readonly i18n = inject(TranslateService);
  private readonly prefs = inject(Prefs);

  protected readonly stepsList: Step[] = ['upload', 'map', 'done'];
  protected readonly step = signal<Step>('upload');
  protected readonly stepIndex = computed(() => this.stepsList.indexOf(this.step()));
  protected readonly year = signal(new Date().getFullYear());
  protected readonly file = signal<File | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly preview = signal<ImportPreview | null>(null);
  protected readonly result = signal<{ created: number; skipped: number } | null>(null);

  protected readonly accounts = liveResource({
    params: () => this.events.version(),
    stream: () => this.api.accounts(),
  });
  protected readonly categories = liveResource({ stream: () => this.api.categories() });
  protected readonly history = liveResource({
    params: () => this.events.version(),
    stream: () => this.api.imports(),
  });
  protected readonly expenseCategories = computed(() =>
    (this.categories.value() ?? []).filter((c) => c.type === 'Expense'),
  );
  protected readonly categoryOptions = computed<SelectOption[]>(() => {
    this.prefs.translations();
    return this.expenseCategories().map((c) => ({
      value: c.id,
      label: categoryLabel(this.i18n, c),
    }));
  });
  private readonly accountItems = computed<SelectOption[]>(() =>
    (this.accounts.value() ?? []).map((a) => ({ value: a.id, label: a.name })),
  );
  protected readonly mainAccountOptions = computed<SelectOption[]>(() => [
    { value: '', label: '—' },
    ...this.accountItems(),
  ]);
  protected readonly optionalAccountOptions = computed<SelectOption[]>(() => {
    this.prefs.translations();
    return [{ value: '', label: this.i18n.instant('import.none') }, ...this.accountItems()];
  });

  protected async analyze() {
    const file = this.file();
    if (!file) return;
    this.busy.set(true);
    this.error.set('');
    try {
      this.preview.set(await firstValueFrom(this.api.analyzeWorkbook(file, this.year())));
      this.step.set('map');
    } catch (err) {
      this.error.set(problemMessage(err));
    } finally {
      this.busy.set(false);
    }
  }

  protected async remap(patch: Record<string, unknown>) {
    const p = this.preview();
    if (!p) return;
    const m = p.preview.mapping;
    try {
      this.preview.set(
        await firstValueFrom(
          this.api.mapImport(p.id, {
            year: m.year,
            mainAccountId: m.mainAccountId,
            investmentAccountId: m.investmentAccountId,
            savingsAccountId: m.savingsAccountId,
            importBudget: m.importBudget,
            ...patch,
          }),
        ),
      );
    } catch (err) {
      this.toasts.error(err);
    }
  }

  protected remapCategory(label: string, categoryId: string) {
    void this.remap({ categories: { [label]: categoryId } });
  }

  protected async commit() {
    const p = this.preview();
    if (!p) return;
    this.busy.set(true);
    try {
      this.result.set(await firstValueFrom(this.api.commitImport(p.id)));
      this.step.set('done');
      this.events.bump();
    } catch (err) {
      this.toasts.error(err);
    } finally {
      this.busy.set(false);
    }
  }

  protected async undo(id: string) {
    try {
      const res = await firstValueFrom(this.api.undoImport(id));
      this.events.bump();
      this.toasts.show(this.i18n.instant('import.undone', { count: res.removed }));
    } catch (err) {
      this.toasts.error(err);
    }
  }

  protected reset() {
    this.step.set('upload');
    this.preview.set(null);
    this.file.set(null);
    this.result.set(null);
  }
}
