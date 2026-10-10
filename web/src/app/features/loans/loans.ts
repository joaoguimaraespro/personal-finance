import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { NgIcon } from '@ng-icons/core';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { firstValueFrom } from 'rxjs';
import { Api } from '../../core/api';
import { DataEvents } from '../../core/data-events';
import { DayPipe, MoneyPipe, PercentPipe } from '../../core/format';
import { liveResource } from '../../core/resource';
import { Toasts } from '../../core/toast';
import { EmptyStateComponent } from '../../shared/empty-state';
import { APP_ICONS, PAGE_ICONS } from '../../shared/icons';
import { ModalComponent } from '../../shared/modal';
import { PageHeaderComponent } from '../../shared/page-header';
import { ProgressComponent } from '../../shared/progress';

/**
 * Every loan in one place: what is owed, the next instalment and when each ends. A loan is a debt account (so it
 * counts in net worth and takes the capital transfers) with credit terms; new ones are created here.
 */
@Component({
  selector: 'app-loans',
  imports: [
    RouterLink,
    NgIcon,
    TranslatePipe,
    HlmButtonImports,
    HlmInputImports,
    MoneyPipe,
    DayPipe,
    PercentPipe,
    EmptyStateComponent,
    ModalComponent,
    PageHeaderComponent,
    ProgressComponent,
  ],
  providers: [APP_ICONS],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-page-header
      [icon]="icons.loans"
      [title]="'nav.loans' | translate"
      [subtitle]="'loans.listSubtitle' | translate"
    >
      <button hlmBtn (click)="newOpen.set(true)">
        <ng-icon name="lucidePlus" />{{ 'loans.new' | translate }}
      </button>
    </app-page-header>

    @let ls = loans.value() ?? [];
    @if (ls.length) {
      <section class="mb-4 grid grid-cols-2 gap-3 lg:grid-cols-3">
        <div class="card !p-4">
          <p class="text-muted-foreground text-xs">{{ 'loans.owed' | translate }}</p>
          <p class="num mt-1 text-2xl font-semibold">{{ owed() | money }}</p>
        </div>
        <div class="card !p-4">
          <p class="text-muted-foreground text-xs">{{ 'loans.monthly' | translate }}</p>
          <p class="num mt-1 text-2xl font-semibold">{{ monthly() | money }}</p>
        </div>
        <div class="card col-span-2 !p-4 lg:col-span-1">
          <p class="text-muted-foreground text-xs">{{ 'loans.interestLeftAll' | translate }}</p>
          <p class="num mt-1 text-2xl font-semibold">{{ interestLeft() | money }}</p>
        </div>
      </section>
      <section class="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
        @for (l of ls; track l.accountId) {
          <a [routerLink]="['/loans', l.accountId]" class="card card-hover block">
            <div class="flex items-baseline justify-between gap-2">
              <h2 class="truncate font-semibold">{{ l.accountName }}</h2>
              <span class="text-muted-foreground num text-xs">{{
                l.currentRatePercent / 100 | pct: 2
              }}</span>
            </div>
            <p class="num mt-3 text-2xl font-semibold">{{ l.outstanding | money }}</p>
            <app-progress class="mt-2 block" [value]="l.paidOffShare" />
            <dl class="text-muted-foreground num mt-3 grid grid-cols-2 gap-2 text-xs">
              <div>
                <dt>{{ 'loans.nextPayment' | translate }}</dt>
                <dd class="text-foreground font-medium">
                  {{ l.next ? (l.next.payment | money) : '—' }}
                </dd>
              </div>
              <div>
                <dt>{{ 'loans.ends' | translate }}</dt>
                <dd class="text-foreground font-medium">{{ l.endDate | day }}</dd>
              </div>
            </dl>
          </a>
        }
      </section>
    } @else if (loans.hasValue()) {
      <section class="card !p-0">
        <app-empty-state
          [icon]="icons.loans"
          [title]="'loans.emptyTitle' | translate"
          [text]="'loans.emptyText' | translate"
        >
          <button hlmBtn (click)="newOpen.set(true)">
            <ng-icon name="lucidePlus" />{{ 'loans.new' | translate }}
          </button>
        </app-empty-state>
      </section>
    }

    <app-modal [open]="newOpen()" [title]="'loans.new' | translate" (closed)="newOpen.set(false)">
      <form class="space-y-3" (submit)="$event.preventDefault(); create()">
        <div>
          <label class="label" for="loan-name">{{ 'common.name' | translate }}</label>
          <input
            id="loan-name"
            hlmInput
            class="w-full"
            maxlength="80"
            required
            [placeholder]="'loans.namePlaceholder' | translate"
            [value]="name()"
            (input)="name.set($any($event.target).value)"
          />
          <p class="text-muted-foreground mt-1 text-xs">{{ 'loans.newHint' | translate }}</p>
        </div>
        <div class="flex justify-end gap-2">
          <button type="button" hlmBtn variant="outline" (click)="newOpen.set(false)">
            {{ 'common.cancel' | translate }}
          </button>
          <button hlmBtn [disabled]="!name().trim()">{{ 'loans.next' | translate }}</button>
        </div>
      </form>
    </app-modal>
  `,
})
export class LoansComponent {
  protected readonly icons = PAGE_ICONS;
  private readonly api = inject(Api);
  private readonly events = inject(DataEvents);
  private readonly toasts = inject(Toasts);
  private readonly router = inject(Router);
  private readonly i18n = inject(TranslateService);

  protected readonly loans = liveResource({
    params: () => this.events.version(),
    stream: () => this.api.loans(),
  });
  protected readonly owed = computed(() =>
    (this.loans.value() ?? []).reduce((s, l) => s + l.outstanding, 0),
  );
  protected readonly monthly = computed(() =>
    (this.loans.value() ?? []).reduce((s, l) => s + (l.next?.payment ?? 0), 0),
  );
  protected readonly interestLeft = computed(() =>
    (this.loans.value() ?? []).reduce((s, l) => s + l.interestLeft, 0),
  );

  protected readonly newOpen = signal(false);
  protected readonly name = signal('');

  /** A loan is a debt account; its terms are filled in on the next screen. */
  protected async create() {
    try {
      const account = await firstValueFrom(
        this.api.createAccount({
          name: this.name().trim(),
          kind: 'Loan',
          currency: 'EUR',
          openingBalance: 0,
          institution: null,
        }),
      );
      this.newOpen.set(false);
      this.name.set('');
      this.events.bump();
      await this.router.navigate(['/loans', account.id]);
    } catch (err) {
      this.toasts.error(err);
    }
  }
}
