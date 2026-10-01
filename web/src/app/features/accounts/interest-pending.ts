import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { NgIcon } from '@ng-icons/core';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { Api } from '../../core/api';
import { DataEvents } from '../../core/data-events';
import { MoneyPipe, MonthNamePipe } from '../../core/format';
import { InterestMonth } from '../../core/models';
import { liveResource } from '../../core/resource';
import { Toasts } from '../../core/toast';
import { APP_ICONS } from '../../shared/icons';
import { parseAmount } from '../transactions/quick-add';

/**
 * Monthly reconciliation of estimated interest: once a month closes, ask whether the bank paid the estimate.
 * Confirming books the estimate as real; typing another amount replaces it (0 = nothing was paid).
 * Unanswered months simply stay estimated.
 */
@Component({
  selector: 'app-interest-pending',
  imports: [NgIcon, HlmButtonImports, HlmInputImports, TranslatePipe, MoneyPipe, MonthNamePipe],
  providers: [APP_ICONS],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if ((pending.value() ?? []).length) {
      <section
        class="card mb-6 border-amber-200 bg-amber-50/60 dark:border-amber-500/30 dark:bg-amber-500/5"
      >
        <h2 class="card-title !text-amber-700 dark:!text-amber-300">
          <ng-icon name="lucidePercent" class="!text-amber-600 dark:!text-amber-300" />{{
            'interest.pendingTitle' | translate
          }}
        </h2>
        <p class="-mt-1 mb-2 text-xs text-muted-foreground">
          {{ 'interest.pendingHint' | translate }}
        </p>
        <ul class="divide-y divide-amber-100 dark:divide-amber-500/10">
          @for (m of pending.value(); track m.id) {
            <li class="flex flex-wrap items-center gap-3 py-2.5">
              <span class="min-w-48 flex-1 text-sm">
                {{
                  'interest.question'
                    | translate
                      : {
                          bank: m.institution || m.accountName,
                          amount: (m.estimatedAmount | money: m.currency),
                          month: (monthNumber(m) | monthName) + ' ' + m.month.slice(0, 4),
                        }
                }}
              </span>
              <label class="sr-only" [for]="'int-' + m.id">{{
                'interest.realAmount' | translate
              }}</label>
              <input
                [id]="'int-' + m.id"
                hlmInput
                class="num h-8 w-28 text-right"
                inputmode="decimal"
                [value]="m.estimatedAmount"
                #amt
              />
              <button hlmBtn size="sm" (click)="reconcile(m, amt.value)">
                <ng-icon name="lucideCheck" />{{ 'interest.confirm' | translate }}
              </button>
            </li>
          }
        </ul>
      </section>
    }
  `,
})
export class InterestPendingComponent {
  private readonly api = inject(Api);
  private readonly events = inject(DataEvents);
  private readonly toasts = inject(Toasts);
  private readonly i18n = inject(TranslateService);

  protected readonly pending = liveResource({
    params: () => this.events.version(),
    stream: () => this.api.interestPending(),
  });

  protected monthNumber(m: InterestMonth): number {
    return Number(m.month.slice(5, 7));
  }

  protected async reconcile(m: InterestMonth, amountText: string) {
    const amount = parseAmount(amountText);
    if (amount === null || amount < 0) {
      this.toasts.show(this.i18n.instant('interest.invalidAmount'), 'error');
      return;
    }
    try {
      // Unchanged amount = the estimate was right; anything else replaces it with the real figure.
      await firstValueFrom(
        this.api.reconcileInterest(m.id, amount === m.estimatedAmount ? {} : { amount }),
      );
      this.events.bump();
      this.toasts.show(this.i18n.instant('interest.reconciled'));
    } catch (err) {
      this.toasts.error(err);
    }
  }
}
