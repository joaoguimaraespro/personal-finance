import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { liveResource } from '../../core/resource';
import { RouterLink } from '@angular/router';
import { BrokerLogoComponent } from '../../shared/broker-logo';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { Api } from '../../core/api';
import { DataEvents } from '../../core/data-events';
import { DayPipe, MoneyPipe, PercentPipe, today } from '../../core/format';
import { Account, AccountKind, Broker, InterestPayout, InterestRate } from '../../core/models';
import { Prefs } from '../../core/prefs';
import { Toasts } from '../../core/toast';
import { ModalComponent } from '../../shared/modal';
import { parseAmount } from '../transactions/quick-add';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { SelectComponent, SelectOption } from '../../shared/select';
import { DateFieldComponent } from '../../shared/date-field';
import { NgIcon } from '@ng-icons/core';
import { APP_ICONS, PAGE_ICONS } from '../../shared/icons';
import { PageHeaderComponent } from '../../shared/page-header';
import { EmptyStateComponent } from '../../shared/empty-state';
import { StatusBadgeComponent } from '../../shared/status-badge';
import { InterestPendingComponent } from './interest-pending';

interface AccountForm {
  name: string;
  kind: AccountKind;
  currency: string;
  openingBalance: string;
  openingBalanceOn: string;
  institution: string;
  identifier: string;
  interestPayout: InterestPayout;
  /** A new rate period to add (empty = keep the current rate). */
  rate: string;
  rateFrom: string;
  withholding: string;
}

/** Portuguese "taxa liberatória" on deposit interest. */
const DEFAULT_WITHHOLDING = '28';

const EMPTY: AccountForm = {
  name: '',
  kind: 'Bank',
  currency: 'EUR',
  openingBalance: '0',
  openingBalanceOn: today(),
  institution: '',
  identifier: '',
  interestPayout: 'Monthly',
  rate: '',
  rateFrom: today(),
  withholding: DEFAULT_WITHHOLDING,
};

/** Only savings and current accounts can carry a rate (TANB). */
export const supportsInterest = (kind: AccountKind) => kind === 'Savings' || kind === 'Bank';

@Component({
  selector: 'app-accounts',
  imports: [
    BrokerLogoComponent,
    RouterLink,
    PageHeaderComponent,
    EmptyStateComponent,
    StatusBadgeComponent,
    NgIcon,
    DateFieldComponent,
    SelectComponent,
    HlmInputImports,
    HlmButtonImports,
    TranslatePipe,
    MoneyPipe,
    PercentPipe,
    DayPipe,
    ModalComponent,
    InterestPendingComponent,
  ],
  providers: [APP_ICONS],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-page-header [icon]="icons.accounts" [title]="'nav.accounts' | translate">
      <div class="flex flex-wrap items-center gap-3">
        <label class="flex items-center gap-2 text-sm text-muted-foreground">
          <input
            type="checkbox"
            [checked]="showArchived()"
            (change)="showArchived.set($any($event.target).checked)"
          />
          {{ 'common.showArchived' | translate }}
        </label>
        <button hlmBtn (click)="openNew()">
          <ng-icon name="lucidePlus" />{{ 'accounts.new' | translate }}
        </button>
      </div>
    </app-page-header>

    <app-interest-pending />

    <div class="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
      @for (a of visible(); track a.id) {
        <div class="card card-hover flex flex-col" [class.opacity-60]="a.archived">
          <div class="flex items-start justify-between gap-3">
            <div class="flex min-w-0 items-center gap-3">
              @if (brokerOf(a); as broker) {
                <app-broker-logo [broker]="broker" [size]="40" aria-hidden="true" />
              } @else {
                <span
                  class="bg-primary/10 text-primary dark:bg-primary/20 flex size-10 shrink-0 items-center justify-center rounded-full"
                  aria-hidden="true"
                >
                  <ng-icon [name]="kindIcon[a.kind] ?? 'lucideWallet'" class="text-lg" />
                </span>
              }
              <div class="min-w-0">
                <p class="truncate font-semibold">{{ a.name }}</p>
                <p class="text-xs text-muted-foreground">
                  {{ 'accountKind.' + a.kind | translate }}
                  @if (a.institution) {
                    · {{ a.institution }}
                  }
                  @if (a.identifierMasked) {
                    · <span class="font-mono">{{ a.identifierMasked }}</span>
                  }
                </p>
              </div>
            </div>
            @if (!a.isManual) {
              <app-status-badge tone="info" icon="lucideLock">{{
                'accounts.readOnly' | translate
              }}</app-status-badge>
            } @else if (a.archived) {
              <app-status-badge icon="lucideArchive">{{
                'common.archive' | translate
              }}</app-status-badge>
            }
          </div>
          @if (a.kind === 'Broker') {
            <!-- A broker account's worth is its synced portfolio (positions + cash), not a ledger balance. -->
            <p class="num mt-5 text-2xl font-semibold tracking-tight">
              {{ brokerValue(a.id) | money }}
            </p>
            <a
              routerLink="/portfolio"
              class="mt-1 inline-flex items-center gap-1 text-xs text-muted-foreground hover:text-foreground"
            >
              {{ 'accounts.brokerValue' | translate }}
              <ng-icon name="lucideArrowRight" aria-hidden="true" />
            </a>
          } @else {
            <p
              class="num mt-5 text-2xl font-semibold tracking-tight"
              [class.tone-neg]="a.balance < 0"
              [attr.title]="
                a.interest?.estimatedInBalance
                  ? ('interest.balanceIncludes'
                    | translate: { amount: (a.interest!.estimatedInBalance | money: a.currency) })
                  : null
              "
            >
              @if (a.interest?.estimatedInBalance) {
                <span class="text-muted-foreground" aria-hidden="true">≈</span>
              }
              {{ a.balance | money: a.currency }}
            </p>
            @if (a.interest; as i) {
              @if (i.annualRatePercent !== null || i.yearToDate) {
                <p class="mt-1 flex flex-wrap items-center gap-x-1 text-xs text-muted-foreground">
                  @if (i.annualRatePercent !== null) {
                    <span class="num"
                      >{{ 'interest.tanb' | translate }}
                      {{ i.annualRatePercent / 100 | pct: 2 }}</span
                    >
                    ·
                  }
                  <span class="num tone-pos">{{
                    'interest.thisYear'
                      | translate: { amount: (i.yearToDate | money: a.currency : true) }
                  }}</span>
                  @if (i.yearToDateEstimated) {
                    <span
                      class="badge bg-muted !px-1.5 !py-0"
                      [attr.title]="'interest.estimatedHint' | translate"
                    >
                      ≈ {{ 'interest.estimated' | translate }}
                    </span>
                  }
                </p>
              }
            }
          }
          <div class="flex-1"></div>
          @if (a.isManual) {
            <div class="mt-4 flex gap-2 border-t pt-4">
              <button hlmBtn variant="outline" size="sm" (click)="openEdit(a)">
                <ng-icon name="lucidePencil" />{{ 'common.edit' | translate }}
              </button>
              <button hlmBtn variant="ghost" size="sm" (click)="toggleArchive(a)">
                <ng-icon [name]="a.archived ? 'lucideArchiveRestore' : 'lucideArchive'" />{{
                  (a.archived ? 'common.restore' : 'common.archive') | translate
                }}
              </button>
            </div>
          }
        </div>
      } @empty {
        <div class="card col-span-full !p-0">
          <app-empty-state [icon]="icons.accounts" [text]="'accounts.empty' | translate">
            <button hlmBtn size="sm" (click)="openNew()">
              <ng-icon name="lucidePlus" />{{ 'accounts.new' | translate }}
            </button>
          </app-empty-state>
        </div>
      }
    </div>

    <app-modal
      [open]="formOpen()"
      [title]="(editingId() ? 'accounts.edit' : 'accounts.new') | translate"
      (closed)="formOpen.set(false)"
    >
      <form class="form-grid" (submit)="$event.preventDefault(); save()">
        <div class="col-span-2">
          <label class="label" for="a-name">{{ 'common.name' | translate }}</label>
          <input
            id="a-name"
            hlmInput
            required
            maxlength="80"
            [value]="form().name"
            (input)="patch({ name: $any($event.target).value })"
          />
        </div>
        <div>
          <label class="label" for="a-kind">{{ 'accounts.kind' | translate }}</label>
          <app-select
            inputId="a-kind"
            [disabled]="!!editingId()"
            [options]="kindOptions()"
            [value]="form().kind"
            (valueChange)="patch({ kind: $any($event) })"
          />
        </div>
        <div>
          <label class="label" for="a-cur">{{ 'tx.currency' | translate }}</label>
          <input
            id="a-cur"
            hlmInput
            class="uppercase"
            maxlength="3"
            [disabled]="!!editingId()"
            [value]="form().currency"
            (input)="patch({ currency: $any($event.target).value.toUpperCase() })"
          />
        </div>
        <div>
          <label class="label" for="a-ob">{{ 'accounts.openingBalance' | translate }}</label>
          <input
            id="a-ob"
            hlmInput
            class="num"
            inputmode="decimal"
            [value]="form().openingBalance"
            (input)="patch({ openingBalance: $any($event.target).value })"
          />
        </div>
        <div>
          <label class="label" for="a-obd">{{ 'accounts.openingBalanceOn' | translate }}</label>
          <app-date-field
            inputId="a-obd"
            [value]="form().openingBalanceOn"
            (valueChange)="patch({ openingBalanceOn: $event })"
          />
        </div>
        <div>
          <label class="label" for="a-inst">{{ 'accounts.institution' | translate }}</label>
          <input
            id="a-inst"
            hlmInput
            maxlength="80"
            [value]="form().institution"
            (input)="patch({ institution: $any($event.target).value })"
          />
        </div>
        <div>
          <label class="label" for="a-id">{{ 'accounts.identifier' | translate }}</label>
          <input
            id="a-id"
            hlmInput
            class="font-mono"
            maxlength="64"
            autocomplete="off"
            [placeholder]="editingId() ? ('accounts.identifierKeep' | translate) : 'PT50…'"
            (input)="patch({ identifier: $any($event.target).value })"
          />
          <p class="mt-1 text-[11px] text-muted-foreground">
            {{ 'accounts.identifierNote' | translate }}
          </p>
        </div>
        @if (showInterest()) {
          <fieldset class="col-span-2 mt-2 grid grid-cols-2 gap-3 border-t pt-4">
            <legend class="sr-only">{{ 'interest.section' | translate }}</legend>
            <div class="col-span-2 flex items-center gap-2 text-sm font-semibold">
              <ng-icon name="lucidePercent" class="text-primary" />{{
                'interest.section' | translate
              }}
            </div>
            <p class="col-span-2 -mt-2 text-[11px] text-muted-foreground">
              {{ 'interest.sectionHint' | translate }}
            </p>
            <div>
              <label class="label" for="a-rate">{{
                (editingId() && currentRate() ? 'interest.newRate' : 'interest.rate') | translate
              }}</label>
              <input
                id="a-rate"
                hlmInput
                class="num"
                inputmode="decimal"
                placeholder="2,25"
                [value]="form().rate"
                (input)="patch({ rate: $any($event.target).value })"
              />
            </div>
            <div>
              <label class="label" for="a-rate-from">{{ 'interest.from' | translate }}</label>
              <app-date-field
                inputId="a-rate-from"
                [value]="form().rateFrom"
                (valueChange)="patch({ rateFrom: $event })"
              />
            </div>
            <div>
              <label class="label" for="a-wh">{{ 'interest.withholding' | translate }}</label>
              <input
                id="a-wh"
                hlmInput
                class="num"
                inputmode="decimal"
                [value]="form().withholding"
                (input)="patch({ withholding: $any($event.target).value })"
              />
            </div>
            <div>
              <label class="label" for="a-payout">{{ 'interest.payout' | translate }}</label>
              <app-select
                inputId="a-payout"
                [options]="payoutOptions()"
                [value]="form().interestPayout"
                (valueChange)="patch({ interestPayout: $any($event) })"
              />
            </div>
            @if (editingId() && (rates.value() ?? []).length) {
              <div class="col-span-2">
                <p class="label">{{ 'interest.history' | translate }}</p>
                <ul class="divide-y rounded-md border text-sm">
                  @for (r of rates.value(); track r.id; let first = $first) {
                    <li class="flex items-center gap-3 px-3 py-1.5">
                      <span class="text-muted-foreground w-28">{{
                        r.effectiveFrom | day: 'short'
                      }}</span>
                      <span class="num flex-1 font-medium">
                        {{ r.annualRatePercent / 100 | pct: 2 }}
                        <span class="text-xs font-normal text-muted-foreground">
                          ·
                          {{
                            'interest.withheld'
                              | translate: { pct: (r.withholdingPercent / 100 | pct: 0) }
                          }}
                        </span>
                      </span>
                      @if (first) {
                        <button
                          type="button"
                          hlmBtn
                          variant="ghost"
                          size="icon-sm"
                          class="text-destructive hover:text-destructive"
                          (click)="removeRate(r)"
                          [attr.aria-label]="'interest.removeLatest' | translate"
                          [attr.title]="'interest.removeLatest' | translate"
                        >
                          <ng-icon name="lucideTrash2" />
                        </button>
                      }
                    </li>
                  }
                </ul>
              </div>
            }
          </fieldset>
        }
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
export class AccountsComponent {
  protected readonly icons = PAGE_ICONS;
  protected readonly kindIcon: Record<AccountKind, string> = {
    Bank: 'lucideLandmark',
    Cash: 'lucideBanknote',
    CreditCard: 'lucideCreditCard',
    Savings: 'lucidePiggyBank',
    Broker: 'lucideBriefcase',
    Loan: 'lucideHandCoins',
    Other: 'lucideWallet',
  };
  private readonly api = inject(Api);
  private readonly events = inject(DataEvents);
  private readonly toasts = inject(Toasts);
  private readonly i18n = inject(TranslateService);
  private readonly prefs = inject(Prefs);
  protected readonly kinds: AccountKind[] = [
    'Bank',
    'Cash',
    'CreditCard',
    'Savings',
    'Loan',
    'Other',
  ];
  protected readonly kindOptions = computed<SelectOption[]>(() => {
    this.prefs.translations();
    return this.kinds.map((k) => ({ value: k, label: this.i18n.instant(`accountKind.${k}`) }));
  });
  protected readonly showArchived = signal(false);
  protected readonly formOpen = signal(false);
  protected readonly editingId = signal<string | null>(null);
  protected readonly form = signal<AccountForm>(EMPTY);
  protected readonly showInterest = computed(() => supportsInterest(this.form().kind));
  protected readonly payoutOptions = computed<SelectOption[]>(() => {
    this.prefs.translations();
    return (['Monthly', 'Daily'] as InterestPayout[]).map((p) => ({
      value: p,
      label: this.i18n.instant(`interest.payouts.${p}`),
    }));
  });
  /** Rate history of the account being edited, newest first. */
  protected readonly rates = rxResource({
    params: () => {
      const id = this.editingId();
      return id ? { id, v: this.events.version() } : undefined;
    },
    stream: ({ params }) => this.api.interestRates(params.id),
  });
  protected readonly currentRate = computed(() => {
    const id = this.editingId();
    return (
      (this.accounts.value() ?? []).find((a) => a.id === id)?.interest?.annualRatePercent ?? null
    );
  });
  protected readonly accounts = liveResource({
    params: () => this.events.version(),
    stream: () => this.api.accounts(true),
  });
  private readonly portfolio = liveResource({
    params: () => this.events.version(),
    stream: () => this.api.portfolioSummary(),
  });

  /** Market value + cash of a synced broker account; null until the portfolio has loaded. */
  /** Which broker a synced account belongs to (its institution is the provider's name). */
  protected brokerOf(a: Account): Broker | null {
    if (a.kind !== 'Broker') return null;
    const institution = a.institution ?? '';
    if (institution.startsWith('Trading 212')) return 'Trading212';
    if (institution.startsWith('Interactive Brokers')) return 'InteractiveBrokers';
    return null;
  }

  protected brokerValue(accountId: string): number | null {
    const account = this.portfolio.value()?.accounts.find((x) => x.accountId === accountId);
    return account ? account.marketValue + account.cash : null;
  }
  protected readonly visible = computed(() =>
    (this.accounts.value() ?? []).filter((a) => this.showArchived() || !a.archived),
  );

  protected patch(p: Partial<AccountForm>) {
    this.form.update((f) => ({ ...f, ...p }));
  }

  protected openNew() {
    this.editingId.set(null);
    this.form.set({ ...EMPTY, openingBalanceOn: today(), rateFrom: today() });
    this.formOpen.set(true);
  }

  protected openEdit(a: Account) {
    this.editingId.set(a.id);
    this.form.set({
      name: a.name,
      kind: a.kind,
      currency: a.currency,
      openingBalance: String(a.openingBalance),
      openingBalanceOn: a.openingBalanceOn,
      institution: a.institution ?? '',
      identifier: '',
      interestPayout: a.interest?.payout ?? 'Monthly',
      rate: '',
      rateFrom: today(),
      withholding: String(a.interest?.withholdingPercent ?? DEFAULT_WITHHOLDING),
    });
    this.formOpen.set(true);
  }

  protected async save() {
    const f = this.form();
    const body = {
      name: f.name,
      kind: f.kind,
      currency: f.currency,
      openingBalance: parseAmount(f.openingBalance) ?? 0,
      openingBalanceOn: f.openingBalanceOn,
      institution: f.institution || null,
      identifier: f.identifier || null,
      interestPayout: supportsInterest(f.kind) ? f.interestPayout : null,
    };
    const rate = supportsInterest(f.kind) && f.rate.trim() ? parseAmount(f.rate) : null;
    if (f.rate.trim() && supportsInterest(f.kind) && (rate === null || rate < 0 || rate > 100)) {
      this.toasts.show(this.i18n.instant('interest.invalidRate'), 'error');
      return;
    }
    try {
      let id = this.editingId();
      if (id) await firstValueFrom(this.api.updateAccount(id, body));
      else id = (await firstValueFrom(this.api.createAccount(body))).id;
      if (rate !== null) {
        // A rate change adds a new period from its date; earlier periods are never rewritten.
        await firstValueFrom(
          this.api.addInterestRate(id, {
            annualRatePercent: rate,
            effectiveFrom: f.rateFrom || f.openingBalanceOn,
            withholdingPercent: parseAmount(f.withholding),
          }),
        );
      }
      this.formOpen.set(false);
      this.events.bump();
      this.toasts.show(this.i18n.instant('common.saved'));
    } catch (err) {
      this.toasts.error(err);
    }
  }

  protected async removeRate(r: InterestRate) {
    const id = this.editingId();
    if (!id) return;
    try {
      await firstValueFrom(this.api.deleteInterestRate(id, r.id));
      this.events.bump();
    } catch (err) {
      this.toasts.error(err);
    }
  }

  protected async toggleArchive(a: Account) {
    try {
      await firstValueFrom(
        a.archived ? this.api.restoreAccount(a.id) : this.api.archiveAccount(a.id),
      );
      this.events.bump();
    } catch (err) {
      this.toasts.error(err);
    }
  }
}
