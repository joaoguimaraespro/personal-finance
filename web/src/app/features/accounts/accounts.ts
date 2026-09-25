import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { Api } from '../../core/api';
import { DataEvents } from '../../core/data-events';
import { MoneyPipe, today } from '../../core/format';
import { Account, AccountKind } from '../../core/models';
import { Toasts } from '../../core/toast';
import { ModalComponent } from '../../shared/modal';
import { parseAmount } from '../transactions/quick-add';

interface AccountForm {
  name: string;
  kind: AccountKind;
  currency: string;
  openingBalance: string;
  openingBalanceOn: string;
  institution: string;
  identifier: string;
}

const EMPTY: AccountForm = { name: '', kind: 'Bank', currency: 'EUR', openingBalance: '0', openingBalanceOn: today(), institution: '', identifier: '' };

@Component({
  selector: 'app-accounts',
  imports: [TranslatePipe, MoneyPipe, ModalComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mb-6 flex flex-wrap items-center justify-between gap-3">
      <h1 class="text-2xl font-semibold tracking-tight">{{ 'nav.accounts' | translate }}</h1>
      <div class="flex gap-2">
        <label class="flex items-center gap-2 text-sm text-slate-500">
          <input type="checkbox" [checked]="showArchived()" (change)="showArchived.set($any($event.target).checked)" /> {{ 'common.showArchived' | translate }}
        </label>
        <button class="btn btn-primary" (click)="openNew()">＋ {{ 'accounts.new' | translate }}</button>
      </div>
    </div>

    <div class="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
      @for (a of visible(); track a.id) {
        <div class="card" [class.opacity-60]="a.archived">
          <div class="flex items-start justify-between">
            <div>
              <p class="font-semibold">{{ a.name }}</p>
              <p class="text-xs text-slate-500">
                {{ 'accountKind.' + a.kind | translate }}@if (a.institution) { · {{ a.institution }} }@if (a.identifierMasked) { · {{ a.identifierMasked }} }
              </p>
            </div>
            @if (!a.isManual) { <span class="badge bg-violet-100 text-violet-700 dark:bg-violet-500/15 dark:text-violet-300">{{ 'accounts.readOnly' | translate }}</span> }
          </div>
          <p class="num mt-4 text-2xl font-semibold" [class.text-rose-600]="a.balance < 0">{{ a.balance | money: a.currency }}</p>
          @if (a.isManual) {
            <div class="mt-4 flex gap-2">
              <button class="btn !py-1 text-xs" (click)="openEdit(a)">{{ 'common.edit' | translate }}</button>
              <button class="btn btn-ghost !py-1 text-xs" (click)="toggleArchive(a)">{{ (a.archived ? 'common.restore' : 'common.archive') | translate }}</button>
            </div>
          }
        </div>
      } @empty {
        <div class="card col-span-full py-12 text-center text-slate-400">{{ 'accounts.empty' | translate }}</div>
      }
    </div>

    <app-modal [open]="formOpen()" [title]="(editingId() ? 'accounts.edit' : 'accounts.new') | translate" (closed)="formOpen.set(false)">
      <form class="grid grid-cols-2 gap-3" (submit)="$event.preventDefault(); save()">
        <div class="col-span-2">
          <label class="label" for="a-name">{{ 'common.name' | translate }}</label>
          <input id="a-name" class="input" required maxlength="80" [value]="form().name" (input)="patch({ name: $any($event.target).value })" />
        </div>
        <div>
          <label class="label" for="a-kind">{{ 'accounts.kind' | translate }}</label>
          <select id="a-kind" class="input" [disabled]="!!editingId()" [value]="form().kind" (change)="patch({ kind: $any($event.target).value })">
            @for (k of kinds; track k) { <option [value]="k">{{ 'accountKind.' + k | translate }}</option> }
          </select>
        </div>
        <div>
          <label class="label" for="a-cur">{{ 'tx.currency' | translate }}</label>
          <input id="a-cur" class="input uppercase" maxlength="3" [disabled]="!!editingId()" [value]="form().currency" (input)="patch({ currency: $any($event.target).value.toUpperCase() })" />
        </div>
        <div>
          <label class="label" for="a-ob">{{ 'accounts.openingBalance' | translate }}</label>
          <input id="a-ob" class="input num" inputmode="decimal" [value]="form().openingBalance" (input)="patch({ openingBalance: $any($event.target).value })" />
        </div>
        <div>
          <label class="label" for="a-obd">{{ 'accounts.openingBalanceOn' | translate }}</label>
          <input id="a-obd" class="input" type="date" [value]="form().openingBalanceOn" (input)="patch({ openingBalanceOn: $any($event.target).value })" />
        </div>
        <div>
          <label class="label" for="a-inst">{{ 'accounts.institution' | translate }}</label>
          <input id="a-inst" class="input" maxlength="80" [value]="form().institution" (input)="patch({ institution: $any($event.target).value })" />
        </div>
        <div>
          <label class="label" for="a-id">{{ 'accounts.identifier' | translate }}</label>
          <input id="a-id" class="input font-mono" maxlength="64" autocomplete="off" [placeholder]="editingId() ? ('accounts.identifierKeep' | translate) : 'PT50…'" (input)="patch({ identifier: $any($event.target).value })" />
          <p class="mt-1 text-[11px] text-slate-400">{{ 'accounts.identifierNote' | translate }}</p>
        </div>
        <div class="col-span-2 flex justify-end gap-2 pt-2">
          <button type="button" class="btn" (click)="formOpen.set(false)">{{ 'common.cancel' | translate }}</button>
          <button class="btn btn-primary">{{ 'common.save' | translate }}</button>
        </div>
      </form>
    </app-modal>
  `,
})
export class AccountsComponent {
  private readonly api = inject(Api);
  private readonly events = inject(DataEvents);
  private readonly toasts = inject(Toasts);
  private readonly i18n = inject(TranslateService);
  protected readonly kinds: AccountKind[] = ['Bank', 'Cash', 'CreditCard', 'Savings', 'Loan', 'Other'];
  protected readonly showArchived = signal(false);
  protected readonly formOpen = signal(false);
  protected readonly editingId = signal<string | null>(null);
  protected readonly form = signal<AccountForm>(EMPTY);
  protected readonly accounts = rxResource({ params: () => this.events.version(), stream: () => this.api.accounts(true) });
  protected readonly visible = computed(() => (this.accounts.value() ?? []).filter((a) => this.showArchived() || !a.archived));

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
      await firstValueFrom(a.archived ? this.api.restoreAccount(a.id) : this.api.archiveAccount(a.id));
      this.events.bump();
    } catch (err) {
      this.toasts.error(err);
    }
  }
}
