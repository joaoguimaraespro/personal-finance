import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FORM_ERRORS, FormState, rules } from '../../shared/form-errors';
import { accountOption } from '../../shared/account-option';
import { liveResource } from '../../core/resource';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { Api } from '../../core/api';
import { DataEvents } from '../../core/data-events';
import { DayPipe, MoneyPipe, today } from '../../core/format';
import { Expected, Frequency, Recurring, TransactionType } from '../../core/models';
import { Prefs } from '../../core/prefs';
import { Toasts } from '../../core/toast';
import { CategoryLabelPipe, categoryLabel } from '../../shared/category-label';
import { bucketLabel } from '../../shared/bucket-label';
import { ModalComponent } from '../../shared/modal';
import { parseAmount } from '../transactions/quick-add';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { SelectComponent, SelectOption } from '../../shared/select';
import { DateFieldComponent } from '../../shared/date-field';
import { NgIcon } from '@ng-icons/core';
import { APP_ICONS, PAGE_ICONS } from '../../shared/icons';
import { PageHeaderComponent } from '../../shared/page-header';
import { EmptyStateComponent } from '../../shared/empty-state';
import { StatusBadgeComponent } from '../../shared/status-badge';
import { InterestPendingComponent } from '../accounts/interest-pending';
import {
  maxInterval,
  scheduleBody,
  scheduleLabel,
  usesDayOfMonth,
  withFrequency,
} from './schedule';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';
import {
  SplitDraftLine,
  SplitLinesComponent,
  fromSplits,
  splitError,
  startSplit,
  toSplitRequests,
} from '../../shared/split-lines';
import { equalShares, shareError } from '../../shared/share-lines';

interface Form {
  name: string;
  type: TransactionType;
  amount: string;
  accountId: string;
  categoryId: string;
  bucketId: string;
  frequency: Frequency;
  interval: string;
  dayOfMonth: string;
  startOn: string;
  endOn: string;
  /** Category lines when the item is split (e.g. one energy bill: electricity + gas); null otherwise. */
  splits: SplitDraftLine[] | null;
  /** People who share it (ADR-0011): equal parts with the owner, or an amount each of the usual bill. */
  sharePeople: string[];
  shareEqual: boolean;
  shareAmounts: Record<string, string>;
  /** Paid by another person each time (the owner owes it to them): that person's id, '' when the owner pays. */
  paidBy: string;
}

@Component({
  selector: 'app-recurring',
  imports: [
    ...FORM_ERRORS,
    PageHeaderComponent,
    EmptyStateComponent,
    StatusBadgeComponent,
    HlmTooltipImports,
    NgIcon,
    DateFieldComponent,
    SelectComponent,
    HlmTableImports,
    HlmInputImports,
    HlmButtonImports,
    SplitLinesComponent,
    TranslatePipe,
    MoneyPipe,
    DayPipe,
    ModalComponent,
    CategoryLabelPipe,
    InterestPendingComponent,
  ],
  providers: [APP_ICONS],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-page-header
      [icon]="icons.recurring"
      [title]="'nav.recurring' | translate"
      [subtitle]="'recurring.subtitle' | translate"
    >
      <button hlmBtn class="self-start sm:self-auto" (click)="open(null)">
        <ng-icon name="lucidePlus" />{{ 'recurring.new' | translate }}
      </button>
    </app-page-header>

    <app-interest-pending />

    @if ((expected.value() ?? []).length) {
      <section
        class="card mb-6 border-amber-200 bg-amber-50/60 dark:border-amber-500/30 dark:bg-amber-500/5"
      >
        <h2 class="card-title !text-amber-700 dark:!text-amber-300">
          <ng-icon name="lucideCalendarClock" class="!text-amber-600 dark:!text-amber-300" />{{
            'recurring.pendingTitle' | translate
          }}
        </h2>
        <ul class="divide-y divide-amber-100 dark:divide-amber-500/10">
          @for (e of expected.value(); track e.id) {
            <li class="flex flex-wrap items-center gap-3 py-2.5">
              <span class="flex-1 text-sm font-medium">{{ e.name }}</span>
              <span class="text-xs text-muted-foreground">{{ e.dueOn | day }}</span>
              <input
                hlmInput
                class="num h-8 w-28 text-right"
                inputmode="decimal"
                [value]="e.amount"
                #amt
              />
              <button hlmBtn variant="outline" size="sm" (click)="skip(e)">
                <ng-icon name="lucideSkipForward" />{{ 'recurring.skip' | translate }}
              </button>
              <button hlmBtn size="sm" (click)="confirm(e, amt.value)">
                <ng-icon name="lucideCheck" />{{ 'recurring.confirm' | translate }}
              </button>
            </li>
          }
        </ul>
      </section>
    }

    <section class="card overflow-x-auto !p-0">
      <table hlmTable>
        <thead hlmTHead>
          <tr hlmTr>
            <th hlmTh>{{ 'common.name' | translate }}</th>
            <th hlmTh class="hidden sm:table-cell">{{ 'recurring.schedule' | translate }}</th>
            <th hlmTh class="hidden sm:table-cell">{{ 'recurring.next' | translate }}</th>
            <th hlmTh>{{ 'common.status' | translate }}</th>
            <th hlmTh class="text-right">{{ 'tx.amount' | translate }}</th>
            <th hlmTh></th>
          </tr>
        </thead>
        <tbody hlmTBody>
          @for (r of recurring.value() ?? []; track r.id) {
            <tr hlmTr [class.opacity-50]="!r.isActive">
              <td hlmTd>
                <div class="font-medium">{{ r.name }}</div>
                <div class="text-xs text-muted-foreground">
                  {{ 'type.' + r.type | translate }} ·
                  @if (r.splits.length) {
                    {{ 'split.count' | translate: { count: r.splits.length } }}
                  } @else {
                    {{ categoryFor(r.categoryId) | categoryLabel }}
                  }
                  @if (paidByOf(r); as who) {
                    ·
                    <span class="text-violet-700 dark:text-violet-300">{{
                      'recurring.paidByName' | translate: { name: who }
                    }}</span>
                  }
                  @if (r.shares?.length) {
                    ·
                    <span class="text-violet-700 dark:text-violet-300">{{
                      'tx.sharedWith' | translate: { names: shareNames(r) }
                    }}</span>
                  }
                </div>
                <!-- Narrow screens: schedule and next date under the name instead of their own columns. -->
                <div class="text-xs text-muted-foreground sm:hidden">
                  {{ schedule(r).key | translate: schedule(r).params }} ·
                  {{ 'recurring.next' | translate }} {{ r.nextDueOn | day }}
                </div>
              </td>
              <td hlmTd class="hidden text-sm text-muted-foreground sm:table-cell">
                @let label = schedule(r);
                {{ label.key | translate: label.params }}
                @if (r.dayOfMonth && hasDay(r.frequency)) {
                  · {{ 'recurring.day' | translate: { day: r.dayOfMonth } }}
                }
              </td>
              <td hlmTd class="hidden text-sm sm:table-cell">{{ r.nextDueOn | day }}</td>
              <td hlmTd>
                @if (r.isActive) {
                  <app-status-badge tone="success">{{
                    'recurring.active' | translate
                  }}</app-status-badge>
                } @else {
                  <app-status-badge>{{ 'recurring.paused' | translate }}</app-status-badge>
                }
              </td>
              <td hlmTd class="num text-right font-medium" [class]="tone(r.type)">
                {{ r.amount | money: r.currency }}
              </td>
              <td hlmTd class="text-right whitespace-nowrap">
                <button
                  hlmBtn
                  variant="ghost"
                  size="icon-sm"
                  (click)="open(r)"
                  [attr.aria-label]="'common.edit' | translate"
                  [hlmTooltip]="'common.edit' | translate"
                >
                  <ng-icon name="lucidePencil" />
                </button>
                <button
                  hlmBtn
                  variant="ghost"
                  size="icon-sm"
                  (click)="toggle(r)"
                  [attr.aria-label]="
                    (r.isActive ? 'recurring.pause' : 'recurring.resume') | translate
                  "
                  [hlmTooltip]="(r.isActive ? 'recurring.pause' : 'recurring.resume') | translate"
                >
                  <ng-icon [name]="r.isActive ? 'lucidePause' : 'lucidePlay'" />
                </button>
                <button
                  hlmBtn
                  variant="ghost"
                  size="icon-sm"
                  class="text-destructive hover:text-destructive"
                  (click)="remove(r)"
                  [attr.aria-label]="'common.delete' | translate"
                  [hlmTooltip]="'common.delete' | translate"
                >
                  <ng-icon name="lucideTrash2" />
                </button>
              </td>
            </tr>
          } @empty {
            <tr hlmTr class="hover:bg-transparent">
              <td hlmTd colspan="6" class="whitespace-normal">
                <app-empty-state [icon]="icons.recurring" [text]="'recurring.empty' | translate">
                  <button hlmBtn size="sm" (click)="open(null)">
                    <ng-icon name="lucidePlus" />{{ 'recurring.new' | translate }}
                  </button>
                </app-empty-state>
              </td>
            </tr>
          }
        </tbody>
      </table>
    </section>

    <app-modal
      [open]="formOpen()"
      [title]="(editingId() ? 'recurring.edit' : 'recurring.new') | translate"
      (closed)="formOpen.set(false)"
    >
      <form novalidate class="form-grid" (submit)="$event.preventDefault(); save()">
        <div class="col-span-2">
          <label class="label" for="r-name">{{ 'common.name' | translate }}</label>
          <input
            id="r-name"
            hlmInput
            required
            maxlength="80"
            placeholder="Spotify"
            [value]="form().name"
            (input)="fs.touch('name'); patch({ name: $any($event.target).value })"
            [attr.aria-invalid]="!!fs.errors().name || null"
          />
          <app-field-error [error]="fs.errors().name" />
        </div>
        <div>
          <label class="label" for="r-type">{{ 'recurring.type' | translate }}</label>
          <app-select
            inputId="r-type"
            [options]="typeOptions()"
            [value]="form().type"
            (valueChange)="
              patch({ type: $any($event), categoryId: '', bucketId: '', splits: null })
            "
          />
        </div>
        <div>
          <label class="label" for="r-amount">{{ 'tx.amount' | translate }}</label>
          <input
            id="r-amount"
            hlmInput
            class="num"
            inputmode="decimal"
            required
            [value]="form().amount"
            (input)="fs.touch('amount'); patch({ amount: $any($event.target).value })"
            [attr.aria-invalid]="!!fs.errors().amount || null"
          />
          <app-field-error [error]="fs.errors().amount" />
          <p class="mt-1 text-[11px] text-muted-foreground">
            {{ 'recurring.variableHint' | translate }}
          </p>
        </div>
        @if ((form().type === 'Expense' || form().type === 'Income') && form().splits) {
          <div class="col-span-2">
            <div class="mb-1 flex items-center justify-between gap-2">
              <span class="label !mb-0">{{ 'split.title' | translate }}</span>
              <button type="button" hlmBtn variant="ghost" size="sm" (click)="cancelSplit()">
                {{ 'split.single' | translate }}
              </button>
            </div>
            <app-split-lines
              idPrefix="r-split"
              [lines]="form().splits ?? []"
              (linesChange)="patch({ splits: $event })"
              [options]="splitCategoryOptions()"
              [total]="parsedAmount()"
            />
          </div>
        } @else if (form().type === 'Expense' || form().type === 'Income') {
          <div class="col-span-2">
            <div class="mb-1 flex items-center justify-between gap-2">
              <label class="label !mb-0" for="r-cat">{{ 'tx.category' | translate }}</label>
              <button
                type="button"
                hlmBtn
                variant="ghost"
                size="sm"
                (click)="patch({ splits: startSplit(form().categoryId || null) })"
              >
                <ng-icon name="lucideChartPie" />{{ 'split.action' | translate }}
              </button>
            </div>
            <app-select
              inputId="r-cat"
              [options]="categorySelectOptions()"
              [value]="form().categoryId"
              (valueChange)="fs.touch('categoryId'); patch({ categoryId: $event })"
              [attr.aria-invalid]="!!fs.errors().categoryId || null"
            />
            <app-field-error [error]="fs.errors().categoryId" />
          </div>
        } @else if (form().type !== 'Transfer') {
          <div class="col-span-2">
            <label class="label" for="r-bucket">{{ 'tx.bucket' | translate }}</label>
            <app-select
              inputId="r-bucket"
              [options]="bucketSelectOptions()"
              [value]="form().bucketId"
              (valueChange)="fs.touch('bucketId'); patch({ bucketId: $event })"
              [attr.aria-invalid]="!!fs.errors().bucketId || null"
            />
            <app-field-error [error]="fs.errors().bucketId" />
          </div>
        }
        @if (form().type === 'Expense' && people().length) {
          <!-- Someone else pays it each time (a flatmate pays the rent): the owner owes them, nothing leaves an account. -->
          <div class="col-span-2">
            <p class="label">{{ 'recurring.whoPays' | translate }}</p>
            <div
              class="segmented text-xs"
              role="group"
              [attr.aria-label]="'recurring.whoPays' | translate"
            >
              <button type="button" [class.active]="!form().paidBy" (click)="patch({ paidBy: '' })">
                {{ 'recurring.paidByMe' | translate }}
              </button>
              <button
                type="button"
                [class.active]="!!form().paidBy"
                (click)="patch({ paidBy: form().paidBy || people()[0].id, sharePeople: [] })"
              >
                {{ 'recurring.paidByPerson' | translate }}
              </button>
            </div>
          </div>
        }
        @if (form().paidBy) {
          <div class="col-span-2">
            <label class="label" for="r-paid-by">{{ 'recurring.paidByWho' | translate }}</label>
            <app-select
              inputId="r-paid-by"
              [options]="personOptions()"
              [value]="form().paidBy"
              (valueChange)="patch({ paidBy: $event })"
            />
            <p class="mt-1 text-xs text-muted-foreground">
              {{ 'recurring.paidByHint' | translate }}
            </p>
          </div>
        } @else {
          <div class="col-span-2">
            <label class="label" for="r-acc">{{ 'tx.account' | translate }}</label>
            <app-select
              inputId="r-acc"
              [options]="accountOptions()"
              [value]="form().accountId"
              (valueChange)="fs.touch('accountId'); patch({ accountId: $event })"
              [attr.aria-invalid]="!!fs.errors().accountId || null"
            />
            <app-field-error [error]="fs.errors().accountId" />
          </div>
        }
        @if (canShare()) {
          <!-- Shared with people (ADR-0011): each confirmed occurrence owes them their part of what was paid. -->
          <div class="col-span-2 rounded-lg border p-3">
            <p class="label">{{ 'tx.share' | translate }}</p>
            <div class="flex flex-wrap gap-1.5">
              @for (p of people(); track p.id) {
                <button
                  type="button"
                  class="chip"
                  [class.chip-active]="form().sharePeople.includes(p.id)"
                  (click)="togglePerson(p.id)"
                >
                  {{ p.name }}
                </button>
              }
            </div>
            @if (form().sharePeople.length) {
              <div
                class="segmented mt-3 text-xs"
                role="group"
                [attr.aria-label]="'tx.share' | translate"
              >
                <button
                  type="button"
                  [class.active]="form().shareEqual"
                  (click)="patch({ shareEqual: true })"
                >
                  {{ 'tx.shareEqual' | translate }}
                </button>
                <button type="button" [class.active]="!form().shareEqual" (click)="customShares()">
                  {{ 'tx.shareCustom' | translate }}
                </button>
              </div>
              @if (!form().shareEqual) {
                <div class="mt-3 space-y-2">
                  @for (p of selectedPeople(); track p.id) {
                    <div class="flex items-center gap-2 text-sm">
                      <span class="min-w-0 flex-1 truncate">{{ p.name }}</span>
                      <input
                        hlmInput
                        class="num h-8 w-28"
                        inputmode="decimal"
                        [attr.aria-label]="p.name"
                        [value]="form().shareAmounts[p.id] ?? ''"
                        (input)="setShareAmount(p.id, $any($event.target).value)"
                      />
                    </div>
                  }
                </div>
              }
              <p class="text-muted-foreground mt-3 text-xs">
                {{ 'recurring.shareHint' | translate }}
              </p>
            }
          </div>
        }
        <div>
          <label class="label" for="r-freq">{{ 'recurring.frequency' | translate }}</label>
          <app-select
            inputId="r-freq"
            [options]="frequencyOptions()"
            [value]="form().frequency"
            (valueChange)="patch(withFrequency(form(), $any($event)))"
          />
        </div>
        @if (form().frequency === 'Daily') {
          <div>
            <label class="label" for="r-interval">{{ 'recurring.intervalDays' | translate }}</label>
            <input
              id="r-interval"
              hlmInput
              class="num"
              type="number"
              inputmode="numeric"
              required
              min="1"
              [max]="maxInterval(form().frequency)"
              aria-describedby="r-interval-hint"
              [value]="form().interval"
              (input)="fs.touch('interval'); patch({ interval: $any($event.target).value })"
              [attr.aria-invalid]="!!fs.errors().interval || null"
            />
            <app-field-error [error]="fs.errors().interval" />
            <p id="r-interval-hint" class="mt-1 text-xs text-muted-foreground">
              {{ 'recurring.intervalDaysHint' | translate }}
            </p>
          </div>
        } @else if (hasDay(form().frequency) && form().frequency !== 'Yearly') {
          <div>
            <label class="label" for="r-day">{{ 'recurring.dayOfMonth' | translate }}</label>
            <input
              id="r-day"
              hlmInput
              class="num"
              type="number"
              min="1"
              max="31"
              [value]="form().dayOfMonth"
              (input)="fs.touch('dayOfMonth'); patch({ dayOfMonth: $any($event.target).value })"
              [attr.aria-invalid]="!!fs.errors().dayOfMonth || null"
            />
            <app-field-error [error]="fs.errors().dayOfMonth" />
          </div>
        }
        <div>
          <label class="label" for="r-start">{{
            (form().frequency === 'Yearly' ? 'recurring.yearlyOn' : 'recurring.start') | translate
          }}</label>
          <app-date-field
            inputId="r-start"
            [value]="form().startOn"
            (valueChange)="patch({ startOn: $event })"
          />
          @if (form().frequency === 'Yearly' && form().startOn) {
            <p class="mt-1 text-xs text-muted-foreground">
              {{ 'recurring.yearlyHint' | translate: { date: dayMonth(form().startOn) } }}
            </p>
          }
        </div>
        <div>
          <label class="label" for="r-end">{{ 'recurring.end' | translate }}</label>
          <app-date-field
            inputId="r-end"
            [value]="form().endOn"
            (valueChange)="patch({ endOn: $event })"
            clearable
          />
        </div>
        <app-form-alert class="col-span-2" [message]="fs.message()" />
        <div class="col-span-2 flex justify-end gap-2 pt-2">
          <button type="button" hlmBtn variant="outline" (click)="formOpen.set(false)">
            {{ 'common.cancel' | translate }}
          </button>
          <button hlmBtn><ng-icon name="lucideSave" />{{ 'common.save' | translate }}</button>
        </div>
      </form>
    </app-modal>
  `,
})
export class RecurringComponent {
  protected readonly icons = PAGE_ICONS;
  protected readonly fs = new FormState<
    'name' | 'amount' | 'accountId' | 'categoryId' | 'bucketId' | 'interval' | 'dayOfMonth'
  >();
  protected tone(type: TransactionType) {
    return type === 'Income' ? 'tone-pos' : type === 'Expense' ? 'tone-neg' : '';
  }
  private readonly api = inject(Api);
  private readonly events = inject(DataEvents);
  private readonly toasts = inject(Toasts);
  private readonly i18n = inject(TranslateService);
  private readonly prefs = inject(Prefs);
  protected readonly types: TransactionType[] = [
    'Expense',
    'Income',
    'Savings',
    'InvestmentContribution',
    'Transfer',
  ];
  protected readonly recurring = liveResource({
    params: () => this.events.version(),
    stream: () => this.api.recurring(),
  });
  protected readonly expected = liveResource({
    params: () => this.events.version(),
    stream: () => this.api.expected(),
  });
  protected readonly accounts = liveResource({ stream: () => this.api.accounts() });
  protected readonly categories = liveResource({ stream: () => this.api.categories() });
  protected readonly buckets = liveResource({ stream: () => this.api.buckets() });
  protected readonly formOpen = signal(false);
  protected readonly editingId = signal<string | null>(null);
  protected readonly form = signal<Form>(this.blank());

  protected readonly manualAccounts = computed(() =>
    (this.accounts.value() ?? []).filter((a) => a.isManual && a.kind !== 'Person'),
  );
  private readonly sharing = liveResource({ stream: () => this.api.sharing() });
  protected readonly people = computed(() => this.sharing.value()?.people ?? []);
  protected readonly selectedPeople = computed(() =>
    this.people().filter((p) => this.form().sharePeople.includes(p.id)),
  );
  protected readonly canShare = computed(
    () =>
      this.form().type === 'Expense' &&
      !this.form().splits &&
      !this.form().paidBy &&
      this.people().length > 0,
  );
  protected readonly personOptions = computed(() =>
    this.people().map((p) => ({ value: p.id, label: p.name })),
  );
  /** The person who pays an item for the owner, when it is one of them. */
  protected paidByOf = (r: Recurring) => this.people().find((p) => p.id === r.accountId)?.name;
  protected shareNames = (r: Recurring) =>
    (r.shares ?? [])
      .map((s) => this.people().find((p) => p.id === s.personId)?.name)
      .filter((n) => !!n)
      .join(', ');

  protected togglePerson(id: string) {
    const ids = this.form().sharePeople;
    this.patch({ sharePeople: ids.includes(id) ? ids.filter((x) => x !== id) : [...ids, id] });
  }

  /** Switching to amounts starts from the equal split of the usual amount, to adjust. */
  protected customShares() {
    const f = this.form();
    if (f.shareEqual) {
      const amounts: Record<string, string> = {};
      for (const s of equalShares(parseAmount(f.amount) ?? 0, f.sharePeople))
        amounts[s.personId] = String(s.amount).replace('.', ',');
      this.patch({ shareAmounts: amounts });
    }
    this.patch({ shareEqual: false });
  }

  protected setShareAmount(id: string, value: string) {
    this.patch({ shareAmounts: { ...this.form().shareAmounts, [id]: value } });
  }
  protected readonly categoryOptions = computed(() =>
    (this.categories.value() ?? []).filter(
      (c) => c.type === (this.form().type === 'Income' ? 'Income' : 'Expense'),
    ),
  );
  protected readonly bucketOptions = computed(() =>
    (this.buckets.value() ?? []).filter(
      (b) => b.group === (this.form().type === 'Savings' ? 'Savings' : 'Investment'),
    ),
  );
  protected readonly typeOptions = computed<SelectOption[]>(() => {
    this.prefs.translations();
    return this.types.map((t) => ({ value: t, label: this.i18n.instant(`type.${t}`) }));
  });
  protected readonly frequencyOptions = computed<SelectOption[]>(() => {
    this.prefs.translations();
    return (['Monthly', 'Weekly', 'Daily', 'Yearly'] as const).map((f) => ({
      value: f,
      label: this.i18n.instant(`frequency.${f}`),
    }));
  });
  protected readonly categorySelectOptions = computed<SelectOption[]>(() => {
    this.prefs.translations();
    return [
      { value: '', label: '—' },
      ...this.categoryOptions().map((c) => ({ value: c.id, label: categoryLabel(this.i18n, c) })),
    ];
  });
  protected readonly splitCategoryOptions = computed<SelectOption[]>(() => {
    this.prefs.translations();
    return [
      { value: '', label: this.i18n.instant('split.pickCategory') },
      ...this.categoryOptions().map((c) => ({ value: c.id, label: categoryLabel(this.i18n, c) })),
    ];
  });
  protected readonly parsedAmount = computed(() => parseAmount(this.form().amount));
  protected readonly startSplit = startSplit;
  protected readonly bucketSelectOptions = computed<SelectOption[]>(() => {
    this.prefs.translations();
    return [
      { value: '', label: '—' },
      ...this.bucketOptions().map((b) => ({ value: b.id, label: bucketLabel(this.i18n, b.name) })),
    ];
  });
  protected readonly accountOptions = computed<SelectOption[]>(() =>
    this.manualAccounts().map(accountOption),
  );

  protected readonly schedule = scheduleLabel;
  protected readonly hasDay = usesDayOfMonth;
  /** "15 de março": the day a yearly item repeats on. */
  protected dayMonth(iso: string): string {
    const [y, m, d] = iso.split('-').map(Number);
    return new Intl.DateTimeFormat(this.prefs.locale(), { day: 'numeric', month: 'long' }).format(
      new Date(y, m - 1, d),
    );
  }
  protected readonly maxInterval = maxInterval;
  protected readonly withFrequency = withFrequency;

  protected categoryFor = (id: string | null) =>
    this.categories.value()?.find((c) => c.id === id) ?? null;

  protected patch(p: Partial<Form>) {
    this.form.update((f) => ({ ...f, ...p }));
  }

  protected open(r: Recurring | null) {
    this.editingId.set(r?.id ?? null);
    this.form.set(
      r
        ? {
            name: r.name,
            type: r.type,
            amount: String(r.amount),
            accountId: this.people().some((p) => p.id === r.accountId)
              ? (this.manualAccounts()[0]?.id ?? '')
              : r.accountId,
            paidBy: this.people().some((p) => p.id === r.accountId) ? r.accountId : '',
            categoryId: r.categoryId ?? '',
            bucketId: r.bucketId ?? '',
            frequency: r.frequency,
            interval: String(r.interval),
            dayOfMonth: r.dayOfMonth ? String(r.dayOfMonth) : '',
            startOn: r.startOn,
            endOn: r.endOn ?? '',
            splits: r.splits?.length ? fromSplits(r.splits) : null,
            ...sharesForm(r),
          }
        : { ...this.blank(), accountId: this.manualAccounts()[0]?.id ?? '' },
    );
    this.fs.reset();
    this.formOpen.set(true);
  }

  protected cancelSplit() {
    const first = this.form().splits?.find((l) => l.categoryId)?.categoryId ?? '';
    this.patch({ splits: null, categoryId: first });
  }

  protected async save() {
    const f = this.form();
    const split = !!f.splits && (f.type === 'Expense' || f.type === 'Income');
    const everyday = f.type === 'Expense' || f.type === 'Income';
    const interval = Number(f.interval);
    const day = f.dayOfMonth.trim() ? Number(f.dayOfMonth) : null;
    const valid = this.fs.check({
      name: rules.required(f.name),
      amount: rules.positive(parseAmount(f.amount)),
      accountId: f.paidBy ? undefined : rules.pick(f.accountId),
      categoryId: everyday && !split ? rules.pick(f.categoryId) : undefined,
      bucketId: !everyday && f.type !== 'Transfer' ? rules.pick(f.bucketId) : undefined,
      interval:
        Number.isInteger(interval) && interval >= 1 && interval <= maxInterval(f.frequency)
          ? undefined
          : 'validation.positive',
      dayOfMonth:
        day === null || (Number.isInteger(day) && day >= 1 && day <= 31)
          ? undefined
          : 'validation.number',
    });
    if (!valid) return;
    if (split) {
      const problem = splitError(parseAmount(f.amount), f.splits!);
      if (problem) {
        this.fs.message.set(this.i18n.instant('split.errors.' + problem));
        return;
      }
    }
    const sharing = f.type === 'Expense' && !split && f.sharePeople.length > 0;
    const shares = sharing ? sharePercents(f) : null;
    if (sharing && !shares) {
      this.fs.message.set(this.i18n.instant('tx.errors.share.amount'));
      return;
    }
    const body = {
      name: f.name,
      type: f.type,
      amount: parseAmount(f.amount),
      currency: 'EUR',
      // Paid by someone else: the expense is booked in that person's account (the owner owes it to them).
      accountId: f.type === 'Expense' && f.paidBy ? f.paidBy : f.accountId,
      ...scheduleBody(f),
      startOn: f.startOn,
      endOn: f.endOn || null,
      categoryId: split ? null : f.categoryId || null,
      bucketId: f.bucketId || null,
      splits: split ? toSplitRequests(f.splits!) : null,
      shares,
    };
    try {
      const id = this.editingId();
      if (id) await firstValueFrom(this.api.updateRecurring(id, body));
      else await firstValueFrom(this.api.createRecurring(body));
      this.formOpen.set(false);
      this.events.bump();
      this.toasts.show(this.i18n.instant('common.saved'));
    } catch (err) {
      this.fs.fail(err);
    }
  }

  protected async confirm(e: Expected, amountText: string) {
    try {
      await firstValueFrom(
        this.api.confirmExpected(e.id, { amount: parseAmount(amountText) ?? e.amount }),
      );
      this.events.bump();
      this.toasts.show(this.i18n.instant('recurring.confirmed'));
    } catch (err) {
      this.toasts.error(err);
    }
  }

  protected async skip(e: Expected) {
    await this.run(this.api.skipExpected(e.id));
  }

  protected async toggle(r: Recurring) {
    await this.run(r.isActive ? this.api.pauseRecurring(r.id) : this.api.resumeRecurring(r.id));
  }

  protected async remove(r: Recurring) {
    await this.run(this.api.deleteRecurring(r.id));
  }

  private async run(request: ReturnType<Api['skipExpected']>) {
    try {
      await firstValueFrom(request);
      this.events.bump();
    } catch (err) {
      this.toasts.error(err);
    }
  }

  private blank(): Form {
    return {
      name: '',
      type: 'Expense',
      amount: '',
      accountId: '',
      categoryId: '',
      bucketId: '',
      frequency: 'Monthly',
      interval: '1',
      dayOfMonth: '',
      startOn: today(),
      endOn: '',
      splits: null,
      sharePeople: [],
      shareEqual: true,
      shareAmounts: {},
      paidBy: '',
    };
  }
}

/** The form's sharing fields for an item: equal parts when every person has 1/(n+1) of it. */
function sharesForm(r: Recurring): Pick<Form, 'sharePeople' | 'shareEqual' | 'shareAmounts'> {
  const shares = r.shares ?? [];
  const equal = 100 / (shares.length + 1);
  return {
    sharePeople: shares.map((s) => s.personId),
    shareEqual: shares.every((s) => Math.abs(s.percent - equal) < 0.0001),
    shareAmounts: Object.fromEntries(
      shares.map((s) => [
        s.personId,
        String(Math.round(r.amount * s.percent) / 100).replace('.', ','),
      ]),
    ),
  };
}

/**
 * Each person's percentage of every occurrence: equal parts with the owner, or their amount of the usual bill (so a
 * bill that varies keeps the same split). Null when the amounts are not valid.
 */
function sharePercents(f: Form): { personId: string; percent: number }[] | null {
  const round = (n: number) => Math.round(n * 1e6) / 1e6;
  if (f.shareEqual) {
    const percent = round(100 / (f.sharePeople.length + 1));
    return f.sharePeople.map((personId) => ({ personId, percent }));
  }
  const total = parseAmount(f.amount) ?? 0;
  const shares = f.sharePeople.map((personId) => ({
    personId,
    amount: parseAmount(f.shareAmounts[personId] ?? '') ?? 0,
  }));
  if (!(total > 0) || shareError(total, shares)) return null;
  return shares.map((s) => ({ personId: s.personId, percent: round((s.amount / total) * 100) }));
}
