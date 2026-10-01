import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { liveResource } from '../../core/resource';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { Api } from '../../core/api';
import { DataEvents } from '../../core/data-events';
import { DateTimePipe, DayPipe, MoneyPipe, MonthNamePipe } from '../../core/format';
import { ImportPreview } from '../../core/models';
import { Prefs } from '../../core/prefs';
import { Toasts, problemMessage } from '../../core/toast';
import { categoryLabel } from '../../shared/category-label';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { SelectComponent, SelectOption } from '../../shared/select';
import { NgIcon } from '@ng-icons/core';
import { APP_ICONS, PAGE_ICONS } from '../../shared/icons';
import { PageHeaderComponent } from '../../shared/page-header';
import { EmptyStateComponent } from '../../shared/empty-state';
import { StatusBadgeComponent, StatusTone } from '../../shared/status-badge';

type Step = 'upload' | 'map' | 'done';

/** Analyze → Map → Validate → Preview → Import. Nothing touches the ledger until "Import" is pressed. */
@Component({
  selector: 'app-import',
  imports: [
    PageHeaderComponent,
    EmptyStateComponent,
    StatusBadgeComponent,
    NgIcon,
    SelectComponent,
    HlmTableImports,
    HlmInputImports,
    HlmButtonImports,
    TranslatePipe,
    MoneyPipe,
    MonthNamePipe,
    DayPipe,
    DateTimePipe,
  ],
  providers: [APP_ICONS],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-page-header
      [icon]="icons.import"
      [title]="'import.title' | translate"
      [subtitle]="'import.subtitle' | translate"
    />

    <ol class="mb-6 flex flex-wrap gap-2 text-xs font-medium">
      @for (s of stepsList; track s; let i = $index) {
        <li
          class="inline-flex items-center gap-1.5 rounded-full px-3 py-1"
          [class]="
            stepIndex() >= i
              ? 'bg-primary text-primary-foreground'
              : 'bg-muted text-muted-foreground'
          "
          [attr.aria-current]="stepIndex() === i ? 'step' : null"
        >
          <ng-icon [name]="stepIndex() > i ? 'lucideCheck' : stepIcon[s]" aria-hidden="true" />
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
          <label
            class="border-input hover:border-ring/60 hover:bg-muted/40 flex cursor-pointer flex-col items-center gap-2 rounded-xl border border-dashed p-6 text-center transition-colors"
          >
            <span
              class="bg-primary/10 text-primary dark:bg-primary/20 flex size-10 items-center justify-center rounded-lg"
              aria-hidden="true"
            >
              <ng-icon name="lucideFileSpreadsheet" class="text-xl" />
            </span>
            <span class="text-sm font-medium">{{ file()?.name ?? '.xlsx' }}</span>
            <input
              class="text-muted-foreground block w-full max-w-xs text-xs file:mr-3 file:rounded-md file:border-0 file:bg-primary/10 file:px-3 file:py-1.5 file:text-sm file:font-medium file:text-primary"
              type="file"
              accept=".xlsx,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
              (change)="file.set($any($event.target).files?.[0] ?? null)"
            />
          </label>
          <p class="text-muted-foreground flex gap-1.5 text-xs">
            <ng-icon
              name="lucideShieldCheck"
              class="text-primary mt-px shrink-0"
              aria-hidden="true"
            />{{ 'import.privacy' | translate }}
          </p>
          @if (error()) {
            <p class="tone-neg flex items-center gap-1.5 text-sm" role="alert">
              <ng-icon name="lucideCircleAlert" aria-hidden="true" />{{ error() }}
            </p>
          }
          <button hlmBtn [disabled]="!file() || busy()" (click)="analyze()">
            <ng-icon
              [name]="busy() ? 'lucideLoaderCircle' : 'lucideSearch'"
              [class]="busy() ? 'motion-safe:animate-spin' : ''"
            />{{ 'import.analyze' | translate }}
          </button>
        </section>
      }
      @case ('map') {
        @if (preview(); as p) {
          <div class="grid gap-4 xl:grid-cols-[1fr_1fr]">
            <section class="card space-y-3">
              <h2 class="card-title">
                <ng-icon name="lucideLayers" />{{ 'import.mapping' | translate }}
              </h2>
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
                  class="flex gap-2 rounded-xl bg-amber-50 p-3 text-sm text-amber-800 dark:bg-amber-500/10 dark:text-amber-300"
                >
                  <ng-icon
                    name="lucideTriangleAlert"
                    class="mt-0.5 shrink-0"
                    aria-hidden="true"
                  />{{ 'import.needAccount' | translate }}
                </p>
              }
              <h3 class="flex items-center gap-2 pt-2 text-sm font-semibold">
                <ng-icon name="lucideTags" class="text-primary" aria-hidden="true" />{{
                  'import.categoryMapping' | translate
                }}
              </h3>
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
                <h2 class="card-title">
                  <ng-icon name="lucideListChecks" />{{ 'import.validation' | translate }}
                </h2>
                <ul class="space-y-1 text-sm">
                  <li>
                    {{
                      'import.transactionsFound'
                        | translate: { count: p.preview.transactions.length }
                    }}
                  </li>
                  <li>
                    @if (p.preview.reconciled) {
                      <span class="tone-pos inline-flex items-center gap-1.5"
                        ><ng-icon name="lucideCircleCheck" />{{
                          'import.reconciled' | translate
                        }}</span
                      >
                    } @else {
                      <span
                        class="inline-flex items-center gap-1.5 text-amber-700 dark:text-amber-400"
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
                    <li class="tone-neg flex items-center gap-1.5">
                      <ng-icon name="lucideCircleX" />{{ e }}
                    </li>
                  }
                  @for (w of p.preview.warnings; track w) {
                    <li class="flex items-center gap-1.5 text-amber-700 dark:text-amber-400">
                      <ng-icon name="lucideTriangleAlert" />{{ w }}
                    </li>
                  }
                </ul>
              </div>

              <div class="card overflow-x-auto !p-0">
                <h2 class="card-title px-5 pt-5">
                  <ng-icon name="lucideScale" />{{ 'import.reconciliation' | translate }}
                </h2>
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
                            <ng-icon name="lucideCheck" class="tone-pos" aria-label="OK" />
                          } @else {
                            <ng-icon name="lucideX" class="tone-neg" aria-label="≠" />
                          }
                        </td>
                      </tr>
                    }
                  </tbody>
                </table>
              </div>

              <div class="flex justify-end gap-2">
                <button hlmBtn variant="outline" (click)="reset()">
                  <ng-icon name="lucideX" />{{ 'common.cancel' | translate }}
                </button>
                <button hlmBtn [disabled]="!p.preview.canCommit || busy()" (click)="commit()">
                  <ng-icon
                    [name]="busy() ? 'lucideLoaderCircle' : 'lucideUpload'"
                    [class]="busy() ? 'motion-safe:animate-spin' : ''"
                  />{{ 'import.commit' | translate }}
                </button>
              </div>
            </section>
          </div>

          <section class="card mt-4 overflow-x-auto !p-0">
            <h2 class="card-title px-5 pt-5">
              <ng-icon name="lucideEye" />{{ 'import.preview' | translate }}
            </h2>
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
                    <td
                      hlmTd
                      class="num text-right"
                      [class.tone-pos]="t.type === 'Income'"
                      [class.tone-neg]="t.type === 'Expense'"
                    >
                      {{ t.amount | money }}
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </section>
        }
      }
      @case ('done') {
        <section class="card max-w-xl space-y-3">
          <p class="tone-pos flex items-center gap-2 text-lg font-semibold">
            <ng-icon name="lucideCircleCheck" />{{
              'import.done' | translate: { created: result()?.created, skipped: result()?.skipped }
            }}
          </p>
          <button hlmBtn variant="outline" (click)="reset()">
            <ng-icon name="lucideFilePlus" />{{ 'import.another' | translate }}
          </button>
        </section>
      }
    }

    <section class="card mt-8 overflow-x-auto !p-0">
      <h2 class="card-title px-5 pt-5">
        <ng-icon name="lucideHistory" />{{ 'import.history' | translate }}
      </h2>
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
              <td hlmTd>
                <span class="inline-flex items-center gap-2"
                  ><ng-icon
                    name="lucideFileSpreadsheet"
                    class="text-muted-foreground"
                    aria-hidden="true"
                  />{{ h.fileName }}</span
                >
              </td>
              <td hlmTd>
                <app-status-badge [tone]="importTone[h.status]" [icon]="importIcon[h.status]">{{
                  'importStatus.' + h.status | translate
                }}</app-status-badge>
              </td>
              <td hlmTd class="num text-right">{{ h.created }}</td>
              <td hlmTd class="text-muted-foreground">{{ h.createdAtUtc | dateTime }}</td>
              <td hlmTd class="text-right">
                @if (h.status === 'Committed') {
                  <button
                    hlmBtn
                    variant="ghost"
                    size="sm"
                    class="text-destructive hover:text-destructive"
                    (click)="undo(h.id)"
                  >
                    <ng-icon name="lucideUndo2" />{{ 'import.undo' | translate }}
                  </button>
                }
              </td>
            </tr>
          } @empty {
            <tr hlmTr class="hover:bg-transparent">
              <td hlmTd colspan="5" class="whitespace-normal">
                <app-empty-state icon="lucideInbox" [text]="'import.noHistory' | translate" />
              </td>
            </tr>
          }
        </tbody>
      </table>
    </section>
  `,
})
export class ImportComponent {
  protected readonly icons = PAGE_ICONS;
  protected readonly stepIcon: Record<Step, string> = {
    upload: 'lucideUpload',
    map: 'lucideListChecks',
    done: 'lucideCircleCheck',
  };
  protected readonly importTone: Record<ImportPreview['status'], StatusTone> = {
    Previewed: 'info',
    Committed: 'success',
    RolledBack: 'neutral',
    Failed: 'danger',
  };
  protected readonly importIcon: Record<ImportPreview['status'], string> = {
    Previewed: 'lucideEye',
    Committed: 'lucideCircleCheck',
    RolledBack: 'lucideRotateCcw',
    Failed: 'lucideCircleX',
  };
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
