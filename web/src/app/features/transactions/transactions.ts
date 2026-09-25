import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { rxResource, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { Api, TransactionFilter } from '../../core/api';
import { DataEvents, QuickAdd } from '../../core/data-events';
import { DayPipe, MoneyPipe } from '../../core/format';
import { AuditEntry, Transaction, TransactionType } from '../../core/models';
import { Toasts } from '../../core/toast';
import { CategoryLabelPipe } from '../../shared/category-label';
import { ModalComponent } from '../../shared/modal';

const TYPE_TONE: Record<TransactionType, string> = {
  Expense: 'text-rose-600 dark:text-rose-400',
  Income: 'text-emerald-600 dark:text-emerald-400',
  Transfer: 'text-slate-500',
  Savings: 'text-cyan-600 dark:text-cyan-400',
  InvestmentContribution: 'text-violet-600 dark:text-violet-400',
};

@Component({
  selector: 'app-transactions',
  imports: [TranslatePipe, MoneyPipe, DayPipe, CategoryLabelPipe, ModalComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mb-6 flex flex-wrap items-center justify-between gap-3">
      <h1 class="text-2xl font-semibold tracking-tight">{{ 'nav.transactions' | translate }}</h1>
      <span class="text-sm text-slate-500">{{ 'tx.count' | translate: { count: page.value()?.total ?? 0 } }}</span>
    </div>

    <section class="card mb-4 grid gap-3 !p-4 md:grid-cols-6">
      <input class="input md:col-span-2" type="search" [placeholder]="'common.search' | translate" [value]="search()" (input)="search.set($any($event.target).value); pageNo.set(1)" />
      <input class="input" type="date" [value]="from()" (input)="from.set($any($event.target).value); pageNo.set(1)" [attr.aria-label]="'common.from' | translate" />
      <input class="input" type="date" [value]="to()" (input)="to.set($any($event.target).value); pageNo.set(1)" [attr.aria-label]="'common.to' | translate" />
      <select class="input" [value]="accountId()" (change)="accountId.set($any($event.target).value); pageNo.set(1)">
        <option value="">{{ 'tx.allAccounts' | translate }}</option>
        @for (a of accounts.value() ?? []; track a.id) { <option [value]="a.id">{{ a.name }}</option> }
      </select>
      <select class="input" [value]="categoryId()" (change)="categoryId.set($any($event.target).value); pageNo.set(1)">
        <option value="">{{ 'tx.allCategories' | translate }}</option>
        @for (c of categories.value() ?? []; track c.id) { <option [value]="c.id">{{ c.parentId ? '— ' : '' }}{{ c | categoryLabel }}</option> }
      </select>
      <div class="flex flex-wrap gap-2 md:col-span-6">
        @for (t of types; track t) {
          <button class="chip" [class.chip-active]="typeFilter().includes(t)" (click)="toggleType(t)">{{ 'type.' + t | translate }}</button>
        }
        @if (hasFilters()) {
          <button class="chip" (click)="clear()">✕ {{ 'common.clear' | translate }}</button>
        }
      </div>
    </section>

    <section class="card overflow-x-auto !p-0">
      <table class="table">
        <thead>
          <tr>
            <th>{{ 'tx.date' | translate }}</th>
            <th>{{ 'tx.description' | translate }}</th>
            <th>{{ 'tx.category' | translate }}</th>
            <th>{{ 'tx.account' | translate }}</th>
            <th class="text-right">{{ 'tx.amount' | translate }}</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          @for (t of page.value()?.items ?? []; track t.id) {
            <tr class="group hover:bg-slate-50 dark:hover:bg-slate-800/40">
              <td class="text-slate-500 whitespace-nowrap">{{ t.occurredOn | day: 'short' }}</td>
              <td class="max-w-72">
                <!-- Descriptions are user/imported data; rendered as text only, never as HTML. -->
                <div class="truncate font-medium">{{ t.description || ('type.' + t.type | translate) }}</div>
                <div class="text-xs text-slate-400">
                  {{ 'type.' + t.type | translate }}
                  @if (t.nature) { · {{ 'nature.' + t.nature | translate }} }
                  @if (t.source !== 'Manual') { · <span class="badge bg-slate-100 !px-1.5 !py-0 dark:bg-slate-800">{{ 'source.' + t.source | translate }}</span> }
                </div>
              </td>
              <td>{{ t.categoryKey ? (t | categoryLabel) : (t.bucketName ?? '—') }}</td>
              <td class="text-slate-500">{{ t.accountName }}@if (t.counterAccountName) { → {{ t.counterAccountName }} }</td>
              <td class="num text-right font-semibold whitespace-nowrap" [class]="tone(t.type)">
                {{ t.type === 'Expense' ? '−' : t.type === 'Income' ? '+' : '' }}{{ t.amount | money: t.currency }}
                @if (t.currency !== 'EUR') { <div class="text-xs font-normal text-slate-400">{{ t.baseAmount | money }}</div> }
              </td>
              <td class="text-right whitespace-nowrap">
                <button class="btn btn-ghost !p-1.5 text-xs" (click)="showHistory(t)" [attr.aria-label]="'tx.history' | translate">⏱</button>
                @if (t.editable) {
                  <button class="btn btn-ghost !p-1.5 text-xs" (click)="quick.edit(t)" [attr.aria-label]="'common.edit' | translate">✎</button>
                  <button class="btn btn-ghost !p-1.5 text-xs text-rose-600" (click)="remove(t)" [attr.aria-label]="'common.delete' | translate">🗑</button>
                }
              </td>
            </tr>
          } @empty {
            <tr><td colspan="6" class="py-12 text-center text-slate-400">{{ 'tx.empty' | translate }}</td></tr>
          }
        </tbody>
      </table>
      @if ((page.value()?.total ?? 0) > pageSize) {
        <div class="flex items-center justify-end gap-2 p-3">
          <button class="btn" [disabled]="pageNo() === 1" (click)="pageNo.set(pageNo() - 1)">‹</button>
          <span class="num text-sm">{{ pageNo() }} / {{ pages() }}</span>
          <button class="btn" [disabled]="pageNo() >= pages()" (click)="pageNo.set(pageNo() + 1)">›</button>
        </div>
      }
    </section>

    <app-modal [open]="!!history()" [title]="'tx.history' | translate" (closed)="history.set(null)">
      <ol class="space-y-3">
        @for (h of history() ?? []; track $index) {
          <li class="rounded-xl border border-slate-100 p-3 text-sm dark:border-slate-800">
            <div class="flex justify-between">
              <span class="font-medium">{{ 'audit.' + h.action | translate }}</span>
              <span class="text-xs text-slate-400">{{ h.atUtc | day }} · {{ h.actor }}</span>
            </div>
            <ul class="mt-2 space-y-0.5 font-mono text-xs text-slate-500">
              @for (c of changes(h); track c.field) {
                <li>{{ c.field }}: @if (c.from !== undefined) { <s>{{ c.from }}</s> → } {{ c.to }}</li>
              }
            </ul>
          </li>
        }
      </ol>
    </app-modal>
  `,
})
export class TransactionsComponent {
  private readonly api = inject(Api);
  private readonly events = inject(DataEvents);
  private readonly toasts = inject(Toasts);
  private readonly i18n = inject(TranslateService);
  protected readonly quick = inject(QuickAdd);
  private readonly query = toSignal(inject(ActivatedRoute).queryParamMap);

  protected readonly types: TransactionType[] = ['Expense', 'Income', 'Transfer', 'Savings', 'InvestmentContribution'];
  protected readonly pageSize = 50;
  protected readonly search = signal('');
  protected readonly from = signal(this.query()?.get('from') ?? '');
  protected readonly to = signal(this.query()?.get('to') ?? '');
  protected readonly accountId = signal(this.query()?.get('accountId') ?? '');
  protected readonly categoryId = signal(this.query()?.get('categoryId') ?? '');
  protected readonly typeFilter = signal<TransactionType[]>([]);
  protected readonly pageNo = signal(1);
  protected readonly history = signal<AuditEntry[] | null>(null);

  protected readonly accounts = rxResource({ stream: () => this.api.accounts(true) });
  protected readonly categories = rxResource({ stream: () => this.api.categories() });

  private readonly filter = computed<TransactionFilter & { v: number }>(() => ({
    search: this.search(),
    from: this.from(),
    to: this.to(),
    accountId: this.accountId(),
    categoryId: this.categoryId(),
    type: this.typeFilter(),
    page: this.pageNo(),
    pageSize: this.pageSize,
    v: this.events.version(),
  }));

  protected readonly page = rxResource({
    params: this.filter,
    stream: ({ params }) => {
      const { v: _v, ...filter } = params;
      return this.api.transactions(filter);
    },
  });

  protected readonly pages = computed(() => Math.max(1, Math.ceil((this.page.value()?.total ?? 0) / this.pageSize)));
  protected readonly hasFilters = computed(
    () => !!(this.search() || this.from() || this.to() || this.accountId() || this.categoryId() || this.typeFilter().length),
  );

  protected tone = (type: TransactionType) => TYPE_TONE[type];

  protected toggleType(t: TransactionType) {
    this.typeFilter.update((list) => (list.includes(t) ? list.filter((x) => x !== t) : [...list, t]));
    this.pageNo.set(1);
  }

  protected clear() {
    this.search.set('');
    this.from.set('');
    this.to.set('');
    this.accountId.set('');
    this.categoryId.set('');
    this.typeFilter.set([]);
    this.pageNo.set(1);
  }

  protected async remove(t: Transaction) {
    try {
      await firstValueFrom(this.api.deleteTransaction(t.id));
      this.events.bump();
      this.toasts.show(this.i18n.instant('tx.deleted'), 'info', {
        label: this.i18n.instant('common.undo'),
        run: async () => {
          await firstValueFrom(this.api.restoreTransaction(t.id));
          this.events.bump();
        },
      });
    } catch (err) {
      this.toasts.error(err);
    }
  }

  protected async showHistory(t: Transaction) {
    try {
      this.history.set(await firstValueFrom(this.api.transactionHistory(t.id)));
    } catch (err) {
      this.toasts.error(err);
    }
  }

  protected changes(entry: AuditEntry): { field: string; from?: unknown; to: unknown }[] {
    const parsed = JSON.parse(entry.changes) as Record<string, { from?: unknown; to: unknown }>;
    const hidden = new Set(['Id', 'ExternalId', 'ImportId', 'ExpectedTransactionId', 'BaseCurrency', 'TimeZone']);
    return Object.entries(parsed)
      .filter(([field, value]) => !hidden.has(field) && value.to !== null)
      .map(([field, value]) => ({ field, from: value.from, to: value.to }));
  }
}
