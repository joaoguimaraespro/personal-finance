import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { Api } from '../../core/api';
import { DataEvents } from '../../core/data-events';
import { DayPipe, MoneyPipe, today } from '../../core/format';
import { Expected, Frequency, Recurring, TransactionType } from '../../core/models';
import { Toasts } from '../../core/toast';
import { CategoryLabelPipe } from '../../shared/category-label';
import { ModalComponent } from '../../shared/modal';
import { parseAmount } from '../transactions/quick-add';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { UiSelect } from '../../shared/select';
import { DateFieldComponent } from '../../shared/date-field';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucidePlus } from '@ng-icons/lucide';

interface Form {
  name: string;
  type: TransactionType;
  amount: string;
  accountId: string;
  categoryId: string;
  bucketId: string;
  frequency: Frequency;
  dayOfMonth: string;
  startOn: string;
  endOn: string;
}

@Component({
  selector: 'app-recurring',
  imports: [
    NgIcon,
    DateFieldComponent,
    UiSelect,
    HlmTableImports,
    HlmInputImports,
    HlmButtonImports,
    TranslatePipe,
    MoneyPipe,
    DayPipe,
    ModalComponent,
    CategoryLabelPipe,
  ],
  providers: [provideIcons({ lucidePlus })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mb-6 flex flex-wrap items-center justify-between gap-3">
      <div>
        <h1 class="text-2xl font-semibold tracking-tight">{{ 'nav.recurring' | translate }}</h1>
        <p class="text-sm text-muted-foreground">{{ 'recurring.subtitle' | translate }}</p>
      </div>
      <button hlmBtn (click)="open(null)">
        <ng-icon name="lucidePlus" />{{ 'recurring.new' | translate }}
      </button>
    </div>

    @if ((expected.value() ?? []).length) {
      <section class="card mb-6">
        <h2 class="card-title">{{ 'recurring.pendingTitle' | translate }}</h2>
        <ul class="divide-y divide-border">
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
                {{ 'recurring.skip' | translate }}
              </button>
              <button hlmBtn size="sm" (click)="confirm(e, amt.value)">
                {{ 'recurring.confirm' | translate }}
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
            <th hlmTh>{{ 'recurring.schedule' | translate }}</th>
            <th hlmTh>{{ 'recurring.next' | translate }}</th>
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
                  {{ categoryFor(r.categoryId) | categoryLabel }}
                </div>
              </td>
              <td hlmTd class="text-sm text-muted-foreground">
                {{ 'frequency.' + r.frequency | translate }}
                @if (r.dayOfMonth) {
                  · {{ 'recurring.day' | translate: { day: r.dayOfMonth } }}
                }
              </td>
              <td hlmTd class="text-sm">{{ r.nextDueOn | day }}</td>
              <td hlmTd class="num text-right font-medium">{{ r.amount | money: r.currency }}</td>
              <td hlmTd class="text-right whitespace-nowrap">
                <button hlmBtn variant="ghost" size="sm" (click)="open(r)">
                  {{ 'common.edit' | translate }}
                </button>
                <button hlmBtn variant="ghost" size="sm" (click)="toggle(r)">
                  {{ (r.isActive ? 'recurring.pause' : 'recurring.resume') | translate }}
                </button>
                <button
                  hlmBtn
                  variant="ghost"
                  size="sm"
                  class="text-destructive hover:text-destructive"
                  (click)="remove(r)"
                >
                  {{ 'common.delete' | translate }}
                </button>
              </td>
            </tr>
          } @empty {
            <tr hlmTr>
              <td hlmTd colspan="5" class="py-12 text-center text-muted-foreground">
                {{ 'recurring.empty' | translate }}
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
      <form class="grid grid-cols-2 gap-3" (submit)="$event.preventDefault(); save()">
        <div class="col-span-2">
          <label class="label" for="r-name">{{ 'common.name' | translate }}</label>
          <input
            id="r-name"
            hlmInput
            required
            maxlength="80"
            placeholder="Spotify"
            [value]="form().name"
            (input)="patch({ name: $any($event.target).value })"
          />
        </div>
        <div>
          <label class="label" for="r-type">{{ 'recurring.type' | translate }}</label>
          <select
            id="r-type"
            uiSelect
            [value]="form().type"
            (change)="patch({ type: $any($event.target).value, categoryId: '', bucketId: '' })"
          >
            @for (t of types; track t) {
              <option [value]="t">{{ 'type.' + t | translate }}</option>
            }
          </select>
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
            (input)="patch({ amount: $any($event.target).value })"
          />
        </div>
        @if (form().type === 'Expense' || form().type === 'Income') {
          <div class="col-span-2">
            <label class="label" for="r-cat">{{ 'tx.category' | translate }}</label>
            <select
              id="r-cat"
              uiSelect
              [value]="form().categoryId"
              (change)="patch({ categoryId: $any($event.target).value })"
            >
              <option value="">—</option>
              @for (c of categoryOptions(); track c.id) {
                <option [value]="c.id">{{ c | categoryLabel }}</option>
              }
            </select>
          </div>
        } @else if (form().type !== 'Transfer') {
          <div class="col-span-2">
            <label class="label" for="r-bucket">{{ 'tx.bucket' | translate }}</label>
            <select
              id="r-bucket"
              uiSelect
              [value]="form().bucketId"
              (change)="patch({ bucketId: $any($event.target).value })"
            >
              <option value="">—</option>
              @for (b of bucketOptions(); track b.id) {
                <option [value]="b.id">{{ b.name }}</option>
              }
            </select>
          </div>
        }
        <div class="col-span-2">
          <label class="label" for="r-acc">{{ 'tx.account' | translate }}</label>
          <select
            id="r-acc"
            uiSelect
            [value]="form().accountId"
            (change)="patch({ accountId: $any($event.target).value })"
          >
            @for (a of manualAccounts(); track a.id) {
              <option [value]="a.id">{{ a.name }}</option>
            }
          </select>
        </div>
        <div>
          <label class="label" for="r-freq">{{ 'recurring.frequency' | translate }}</label>
          <select
            id="r-freq"
            uiSelect
            [value]="form().frequency"
            (change)="patch({ frequency: $any($event.target).value })"
          >
            <option value="Monthly">{{ 'frequency.Monthly' | translate }}</option>
            <option value="Weekly">{{ 'frequency.Weekly' | translate }}</option>
            <option value="Yearly">{{ 'frequency.Yearly' | translate }}</option>
          </select>
        </div>
        @if (form().frequency !== 'Weekly') {
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
              (input)="patch({ dayOfMonth: $any($event.target).value })"
            />
          </div>
        }
        <div>
          <label class="label" for="r-start">{{ 'recurring.start' | translate }}</label>
          <app-date-field
            inputId="r-start"
            [value]="form().startOn"
            (valueChange)="patch({ startOn: $event })"
          />
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
        <div class="col-span-2 flex justify-end gap-2 pt-2">
          <button type="button" hlmBtn variant="outline" (click)="formOpen.set(false)">
            {{ 'common.cancel' | translate }}
          </button>
          <button hlmBtn>{{ 'common.save' | translate }}</button>
        </div>
      </form>
    </app-modal>
  `,
})
export class RecurringComponent {
  private readonly api = inject(Api);
  private readonly events = inject(DataEvents);
  private readonly toasts = inject(Toasts);
  private readonly i18n = inject(TranslateService);
  protected readonly types: TransactionType[] = [
    'Expense',
    'Income',
    'Savings',
    'InvestmentContribution',
    'Transfer',
  ];
  protected readonly recurring = rxResource({
    params: () => this.events.version(),
    stream: () => this.api.recurring(),
  });
  protected readonly expected = rxResource({
    params: () => this.events.version(),
    stream: () => this.api.expected(),
  });
  protected readonly accounts = rxResource({ stream: () => this.api.accounts() });
  protected readonly categories = rxResource({ stream: () => this.api.categories() });
  protected readonly buckets = rxResource({ stream: () => this.api.buckets() });
  protected readonly formOpen = signal(false);
  protected readonly editingId = signal<string | null>(null);
  protected readonly form = signal<Form>(this.blank());

  protected readonly manualAccounts = computed(() =>
    (this.accounts.value() ?? []).filter((a) => a.isManual),
  );
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
            accountId: r.accountId,
            categoryId: r.categoryId ?? '',
            bucketId: r.bucketId ?? '',
            frequency: r.frequency,
            dayOfMonth: r.dayOfMonth ? String(r.dayOfMonth) : '',
            startOn: r.startOn,
            endOn: r.endOn ?? '',
          }
        : { ...this.blank(), accountId: this.manualAccounts()[0]?.id ?? '' },
    );
    this.formOpen.set(true);
  }

  protected async save() {
    const f = this.form();
    const body = {
      name: f.name,
      type: f.type,
      amount: parseAmount(f.amount),
      currency: 'EUR',
      accountId: f.accountId,
      frequency: f.frequency,
      startOn: f.startOn,
      dayOfMonth: f.dayOfMonth ? Number(f.dayOfMonth) : null,
      endOn: f.endOn || null,
      categoryId: f.categoryId || null,
      bucketId: f.bucketId || null,
    };
    try {
      const id = this.editingId();
      if (id) await firstValueFrom(this.api.updateRecurring(id, body));
      else await firstValueFrom(this.api.createRecurring(body));
      this.formOpen.set(false);
      this.events.bump();
      this.toasts.show(this.i18n.instant('common.saved'));
    } catch (err) {
      this.toasts.error(err);
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
      dayOfMonth: '',
      startOn: today(),
      endOn: '',
    };
  }
}
