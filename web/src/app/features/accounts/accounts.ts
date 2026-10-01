import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { liveResource } from '../../core/resource';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { Api } from '../../core/api';
import { DataEvents } from '../../core/data-events';
import { MoneyPipe, today } from '../../core/format';
import { Account, AccountKind } from '../../core/models';
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

interface AccountForm {
  name: string;
  kind: AccountKind;
  currency: string;
  openingBalance: string;
  openingBalanceOn: string;
  institution: string;
  identifier: string;
}

const EMPTY: AccountForm = {
  name: '',
  kind: 'Bank',
  currency: 'EUR',
  openingBalance: '0',
  openingBalanceOn: today(),
  institution: '',
  identifier: '',
};

@Component({
  selector: 'app-accounts',
  imports: [
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
    ModalComponent,
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

    <div class="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
      @for (a of visible(); track a.id) {
        <div class="card card-hover flex flex-col" [class.opacity-60]="a.archived">
          <div class="flex items-start justify-between gap-3">
            <div class="flex min-w-0 items-center gap-3">
              <span
                class="bg-primary/10 text-primary dark:bg-primary/20 flex size-10 shrink-0 items-center justify-center rounded-full"
                aria-hidden="true"
              >
                <ng-icon [name]="kindIcon[a.kind] ?? 'lucideWallet'" class="text-lg" />
              </span>
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
          <p
            class="num mt-5 text-2xl font-semibold tracking-tight"
            [class.tone-neg]="a.balance < 0"
          >
            {{ a.balance | money: a.currency }}
          </p>
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
  protected readonly accounts = liveResource({
    params: () => this.events.version(),
    stream: () => this.api.accounts(true),
  });
  protected readonly visible = computed(() =>
    (this.accounts.value() ?? []).filter((a) => this.showArchived() || !a.archived),
  );

  protected patch(p: Partial<AccountForm>) {
    this.form.update((f) => ({ ...f, ...p }));
  }

  protected openNew() {
    this.editingId.set(null);
    this.form.set({ ...EMPTY, openingBalanceOn: today() });
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
    };
    try {
      const id = this.editingId();
      if (id) await firstValueFrom(this.api.updateAccount(id, body));
      else await firstValueFrom(this.api.createAccount(body));
      this.formOpen.set(false);
      this.events.bump();
      this.toasts.show(this.i18n.instant('common.saved'));
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
