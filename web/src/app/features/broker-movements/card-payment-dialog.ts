import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { Api } from '../../core/api';
import { CardPayment } from '../../core/card-payment';
import { DataEvents } from '../../core/data-events';
import { DayPipe, MoneyPipe } from '../../core/format';
import { CardPaymentRequest } from '../../core/models';
import { Prefs } from '../../core/prefs';
import { liveResource } from '../../core/resource';
import { Toasts } from '../../core/toast';
import { categoryLabel } from '../../shared/category-label';
import { ModalComponent } from '../../shared/modal';
import { SelectComponent, SelectOption } from '../../shared/select';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmInputImports } from '@spartan-ng/helm/input';

/** What the card paid: a recurring item to confirm, an expense already recorded, or a new expense. */
type Choice = { kind: 'expected' | 'transaction'; id: string } | { kind: 'new' };

/**
 * A withdrawal a broker reported that was money spent with its card. The closest recurring items still to confirm
 * and expenses already recorded come first (a shared recurring keeps each person's part); otherwise a new expense.
 * Either way it is paid from the broker account, counts as the month's expense and as that much less invested.
 */
@Component({
  selector: 'app-card-payment-dialog',
  imports: [
    TranslatePipe,
    MoneyPipe,
    DayPipe,
    ModalComponent,
    SelectComponent,
    HlmButtonImports,
    HlmInputImports,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-modal
      [open]="!!card.target()"
      [title]="'brokerMovements.cardTitle' | translate"
      (closed)="card.close()"
    >
      @if (card.target(); as t) {
        <form class="space-y-4" (submit)="$event.preventDefault(); save()" novalidate>
          <p class="text-sm">
            {{
              'brokerMovements.cardIntro'
                | translate: { amount: (t.amount | money), broker: t.broker, date: (t.on | day) }
            }}
          </p>

          @if (candidates.value(); as c) {
            @if (c.recurring.length) {
              <fieldset class="space-y-2">
                <legend class="label">{{ 'brokerMovements.dueRecurring' | translate }}</legend>
                @for (r of c.recurring; track r.expectedId) {
                  <label class="choice" [class.choice-active]="isChosen('expected', r.expectedId)">
                    <input
                      type="radio"
                      name="card-choice"
                      [checked]="isChosen('expected', r.expectedId)"
                      (change)="choice.set({ kind: 'expected', id: r.expectedId })"
                    />
                    <span class="min-w-0 flex-1">
                      <span class="block truncate font-medium">{{ r.name }}</span>
                      <span class="text-muted-foreground block text-xs">
                        {{ 'brokerMovements.due' | translate: { date: (r.dueOn | day) } }}
                        @if (r.shared) {
                          ·
                          <span class="text-violet-700 dark:text-violet-300">{{
                            'brokerMovements.shared' | translate
                          }}</span>
                        }
                      </span>
                    </span>
                    <span class="num text-sm">{{ r.amount | money: r.currency }}</span>
                  </label>
                }
              </fieldset>
            }
            @if (c.expenses.length) {
              <fieldset class="space-y-2">
                <legend class="label">{{ 'brokerMovements.recorded' | translate }}</legend>
                @for (e of c.expenses; track e.transactionId) {
                  <label
                    class="choice"
                    [class.choice-active]="isChosen('transaction', e.transactionId)"
                  >
                    <input
                      type="radio"
                      name="card-choice"
                      [checked]="isChosen('transaction', e.transactionId)"
                      (change)="choice.set({ kind: 'transaction', id: e.transactionId })"
                    />
                    <span class="min-w-0 flex-1">
                      <span class="block truncate font-medium">{{
                        e.description || category(e)
                      }}</span>
                      <span class="text-muted-foreground block text-xs">
                        {{ e.on | day }} · {{ category(e) }} ·
                        {{ 'brokerMovements.onAccount' | translate: { account: e.account } }}
                        @if (e.recurring) {
                          · {{ 'brokerMovements.recurringBadge' | translate }}
                        }
                        @if (e.shared) {
                          ·
                          <span class="text-violet-700 dark:text-violet-300">{{
                            'brokerMovements.shared' | translate
                          }}</span>
                        }
                      </span>
                    </span>
                    <span class="num text-sm">{{ e.amount | money }}</span>
                  </label>
                }
              </fieldset>
            }
          }

          <fieldset class="space-y-2">
            <label class="choice" [class.choice-active]="choice().kind === 'new'">
              <input
                type="radio"
                name="card-choice"
                [checked]="choice().kind === 'new'"
                (change)="choice.set({ kind: 'new' })"
              />
              <span class="flex-1 font-medium">{{ 'brokerMovements.newExpense' | translate }}</span>
            </label>
            @if (choice().kind === 'new') {
              <div class="grid gap-3 sm:grid-cols-2">
                <div>
                  <label class="label" for="card-category">{{ 'tx.category' | translate }}</label>
                  <app-select
                    inputId="card-category"
                    [options]="categoryOptions()"
                    [value]="categoryId()"
                    (valueChange)="categoryId.set($event || '')"
                  />
                </div>
                <div>
                  <label class="label" for="card-description">{{
                    'tx.description' | translate
                  }}</label>
                  <input
                    hlmInput
                    id="card-description"
                    class="w-full"
                    [value]="description()"
                    (input)="description.set($any($event.target).value)"
                  />
                </div>
              </div>
            }
          </fieldset>

          <p class="text-muted-foreground text-xs">
            {{ 'brokerMovements.cardEffect' | translate: { broker: t.broker } }}
          </p>
          @if (error()) {
            <p class="text-destructive text-sm" role="alert">{{ error() }}</p>
          }
          <div class="flex justify-end gap-2">
            <button hlmBtn type="button" variant="outline" (click)="card.close()">
              {{ 'common.cancel' | translate }}
            </button>
            <button hlmBtn type="submit" [disabled]="saving()">
              {{ 'common.save' | translate }}
            </button>
          </div>
        </form>
      }
    </app-modal>
  `,
  styles: `
    .choice {
      display: flex;
      align-items: center;
      gap: 0.75rem;
      border: 1px solid var(--border);
      border-radius: 0.5rem;
      padding: 0.5rem 0.75rem;
      cursor: pointer;
    }
    .choice-active {
      border-color: var(--primary);
      background: color-mix(in oklab, var(--primary) 6%, transparent);
    }
  `,
})
export class CardPaymentDialogComponent {
  protected readonly card = inject(CardPayment);
  private readonly api = inject(Api);
  private readonly events = inject(DataEvents);
  private readonly toasts = inject(Toasts);
  private readonly i18n = inject(TranslateService);
  private readonly prefs = inject(Prefs);

  protected readonly candidates = liveResource({
    params: () => this.card.target()?.id,
    stream: ({ params }) => this.api.cardPaymentCandidates(params),
  });
  private readonly categories = liveResource({
    params: () => (this.card.target() ? true : undefined),
    stream: () => this.api.categories(),
  });

  protected readonly choice = signal<Choice>({ kind: 'new' });
  protected readonly categoryId = signal('');
  protected readonly description = signal('');
  protected readonly saving = signal(false);
  protected readonly error = signal('');

  protected readonly categoryOptions = computed<SelectOption[]>(() => {
    this.prefs.translations();
    return [
      { value: '', label: this.i18n.instant('split.pickCategory') },
      ...(this.categories.value() ?? [])
        .filter((c) => c.type === 'Expense' && !c.archived)
        .map((c) => ({
          value: c.id,
          label: (c.parentId ? '— ' : '') + categoryLabel(this.i18n, c),
        })),
    ];
  });

  constructor() {
    // A fresh dialog for each withdrawal: nothing chosen yet, then the closest candidate once they load.
    effect(() => {
      this.card.target();
      this.choice.set({ kind: 'new' });
      this.categoryId.set('');
      this.description.set('');
      this.error.set('');
    });
    effect(() => {
      const c = this.candidates.value();
      if (!c) return;
      if (c.recurring.length) this.choice.set({ kind: 'expected', id: c.recurring[0].expectedId });
      else if (c.expenses.length)
        this.choice.set({ kind: 'transaction', id: c.expenses[0].transactionId });
    });
  }

  protected isChosen(kind: 'expected' | 'transaction', id: string) {
    const c = this.choice();
    return c.kind === kind && c.id === id;
  }

  protected category(e: { categoryKey: string | null; categoryName: string | null }) {
    return categoryLabel(this.i18n, { key: e.categoryKey, name: e.categoryName });
  }

  protected async save() {
    const target = this.card.target();
    if (!target) return;
    const c = this.choice();
    if (c.kind === 'new' && !this.categoryId()) {
      this.error.set(this.i18n.instant('brokerMovements.pickCategory'));
      return;
    }
    const body: CardPaymentRequest =
      c.kind === 'expected'
        ? { expectedId: c.id }
        : c.kind === 'transaction'
          ? { transactionId: c.id }
          : { categoryId: this.categoryId(), description: this.description() || null };
    this.saving.set(true);
    this.error.set('');
    try {
      await firstValueFrom(this.api.recordCardPayment(target.id, body));
      this.toasts.show(this.i18n.instant('brokerMovements.cardSaved'));
      this.events.bump();
      this.card.close();
    } catch (err) {
      const e = err as { error?: { detail?: string } };
      this.error.set(e.error?.detail ?? this.i18n.instant('common.error'));
    } finally {
      this.saving.set(false);
    }
  }
}
