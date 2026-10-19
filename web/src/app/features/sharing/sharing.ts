import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { accountOption } from '../../shared/account-option';
import { NgIcon } from '@ng-icons/core';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { firstValueFrom } from 'rxjs';
import { Api } from '../../core/api';
import { Confirm } from '../../core/confirm';
import { deleteAccountConfirmed } from '../../shared/account-deletion';
import { DataEvents } from '../../core/data-events';
import { DayPipe, MoneyPipe, today } from '../../core/format';
import { SharedPerson } from '../../core/models';
import { liveResource } from '../../core/resource';
import { Toasts } from '../../core/toast';
import { DateFieldComponent } from '../../shared/date-field';
import { EmptyStateComponent } from '../../shared/empty-state';
import { APP_ICONS, PAGE_ICONS } from '../../shared/icons';
import { ModalComponent } from '../../shared/modal';
import { PageHeaderComponent } from '../../shared/page-header';
import { parseAmount } from '../../shared/parse-amount';
import { SelectComponent, SelectOption } from '../../shared/select';
import { categoryLabel } from '../../shared/category-label';

/**
 * People the owner shares costs with (ADR-0011): who owes what since when, which shared expenses are still open,
 * and recording being paid back (or paying back). Sharing an expense happens where it is recorded.
 */
@Component({
  selector: 'app-sharing',
  imports: [
    NgIcon,
    TranslatePipe,
    HlmButtonImports,
    HlmInputImports,
    MoneyPipe,
    DayPipe,
    DateFieldComponent,
    EmptyStateComponent,
    ModalComponent,
    PageHeaderComponent,
    SelectComponent,
  ],
  providers: [APP_ICONS],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-page-header
      [icon]="icons.sharing"
      [title]="'nav.sharing' | translate"
      [subtitle]="'sharing.subtitle' | translate"
    >
      <button hlmBtn (click)="openPerson()">
        <ng-icon name="lucidePlus" />{{ 'sharing.addPerson' | translate }}
      </button>
    </app-page-header>

    @let o = overview.value();
    @if (o && o.people.length) {
      <section class="mb-4 grid grid-cols-2 gap-3">
        <div class="card !p-4">
          <p class="text-muted-foreground text-xs">{{ 'sharing.owedToYou' | translate }}</p>
          <p class="num tone-pos mt-1 text-2xl font-semibold">{{ o.owedToYou | money }}</p>
        </div>
        <div class="card !p-4">
          <p class="text-muted-foreground text-xs">{{ 'sharing.youOwe' | translate }}</p>
          <p class="num mt-1 text-2xl font-semibold" [class.tone-neg]="o.youOwe > 0">
            {{ o.youOwe | money }}
          </p>
        </div>
      </section>

      <section class="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
        @for (p of o.people; track p.id) {
          <div class="card flex flex-col" data-testid="person-card">
            <div class="flex items-start justify-between gap-3">
              <div class="flex min-w-0 items-center gap-3">
                <span
                  class="flex size-10 shrink-0 items-center justify-center rounded-full text-sm font-semibold text-white"
                  [style.background-color]="color(p.name)"
                  aria-hidden="true"
                  >{{ initials(p.name) }}</span
                >
                <div class="min-w-0">
                  <p class="truncate font-semibold">{{ p.name }}</p>
                  <p class="text-muted-foreground text-xs">
                    @if (p.balance > 0) {
                      {{ 'sharing.owesYou' | translate }}
                      @if (p.owedSince) {
                        · {{ 'sharing.since' | translate: { date: (p.owedSince | day) } }}
                      }
                    } @else if (p.balance < 0) {
                      {{ 'sharing.youOweThem' | translate }}
                    } @else {
                      {{ 'sharing.settled' | translate }}
                    }
                  </p>
                </div>
              </div>
              <div class="flex gap-2">
                <button
                  type="button"
                  class="text-muted-foreground hover:text-foreground"
                  [attr.aria-label]="('common.edit' | translate) + ' · ' + p.name"
                  [title]="'common.edit' | translate"
                  (click)="editPerson(p)"
                >
                  <ng-icon name="lucidePencil" />
                </button>
                <button
                  type="button"
                  class="text-muted-foreground hover:text-foreground"
                  [attr.aria-label]="'common.archive' | translate"
                  [title]="'common.archive' | translate"
                  (click)="archive(p)"
                >
                  <ng-icon name="lucideArchive" />
                </button>
                <button
                  type="button"
                  class="text-muted-foreground hover:text-destructive"
                  [attr.aria-label]="('common.delete' | translate) + ' · ' + p.name"
                  [title]="'common.delete' | translate"
                  (click)="remove(p)"
                >
                  <ng-icon name="lucideTrash2" />
                </button>
              </div>
            </div>

            <p
              class="num mt-5 text-2xl font-semibold tracking-tight"
              [class.tone-pos]="p.balance > 0"
              [class.tone-neg]="p.balance < 0"
            >
              {{ abs(p.balance) | money }}
            </p>

            @if (p.open.length) {
              <ul class="mt-4 space-y-2 border-t pt-3 text-sm">
                @for (s of p.open; track s.shareId) {
                  <li class="flex items-baseline justify-between gap-3">
                    <span class="min-w-0">
                      <span class="block truncate">{{
                        s.description || s.categoryName || ('sharing.expense' | translate)
                      }}</span>
                      <span class="text-muted-foreground text-xs"
                        >{{ s.on | day }} ·
                        {{
                          'sharing.ofTotal' | translate: { total: (s.expenseAmount | money) }
                        }}</span
                      >
                    </span>
                    <span class="num shrink-0 font-medium">
                      {{ s.outstanding | money }}
                      @if (s.outstanding < s.amount) {
                        <span class="text-muted-foreground text-xs font-normal"
                          >/ {{ s.amount | money }}</span
                        >
                      }
                    </span>
                  </li>
                }
              </ul>
            }

            @if (p.recent.length) {
              <details class="mt-3 text-sm">
                <summary class="text-muted-foreground cursor-pointer text-xs">
                  {{ 'sharing.history' | translate }}
                </summary>
                <ul class="mt-2 space-y-1.5">
                  @for (m of p.recent; track m.transactionId) {
                    <li class="flex items-baseline justify-between gap-3 text-xs">
                      <span class="min-w-0 truncate">
                        <span class="text-muted-foreground">{{ m.on | day }}</span>
                        · {{ 'sharing.kind.' + m.kind | translate }}
                        @if (m.description) {
                          · {{ m.description }}
                        }
                      </span>
                      <span class="num shrink-0" [class.tone-pos]="m.amount < 0">{{
                        m.amount | money
                      }}</span>
                    </li>
                  }
                </ul>
              </details>
            }

            <div class="mt-auto flex flex-wrap gap-2 pt-4">
              <button
                hlmBtn
                size="sm"
                [variant]="p.balance > 0 ? 'default' : 'outline'"
                (click)="openSettle(p, 'received')"
              >
                <ng-icon name="lucideArrowDownLeft" />{{ 'sharing.received' | translate }}
              </button>
              <button
                hlmBtn
                size="sm"
                [variant]="p.balance < 0 ? 'default' : 'outline'"
                (click)="openSettle(p, 'paid')"
              >
                <ng-icon name="lucideArrowUpRight" />{{ 'sharing.paid' | translate }}
              </button>
              <button hlmBtn size="sm" variant="ghost" (click)="openPaidForMe(p)">
                <ng-icon name="lucideReceipt" />{{ 'sharing.paidForMe' | translate }}
              </button>
            </div>
          </div>
        }
      </section>
      <p class="text-muted-foreground mt-6 flex gap-1.5 text-xs">
        <ng-icon name="lucideInfo" class="mt-px shrink-0" aria-hidden="true" />{{
          'sharing.howTo' | translate
        }}
      </p>
    } @else if (o) {
      <section class="card !p-0">
        <app-empty-state
          [icon]="icons.sharing"
          [title]="'sharing.emptyTitle' | translate"
          [text]="'sharing.emptyText' | translate"
        >
          <button hlmBtn (click)="openPerson()">
            <ng-icon name="lucidePlus" />{{ 'sharing.addPerson' | translate }}
          </button>
        </app-empty-state>
      </section>
    }

    <app-modal
      [open]="personOpen()"
      [title]="(editingPerson() ? 'sharing.editPerson' : 'sharing.addPerson') | translate"
      (closed)="personOpen.set(false)"
    >
      <form class="space-y-3" (submit)="$event.preventDefault(); savePerson()">
        <div>
          <label class="label" for="person-name">{{ 'common.name' | translate }}</label>
          <input
            id="person-name"
            hlmInput
            class="w-full"
            maxlength="80"
            required
            [placeholder]="'sharing.namePlaceholder' | translate"
            [value]="personName()"
            (input)="personName.set($any($event.target).value)"
          />
        </div>
        <!-- Owed before the app: the person's opening balance (paying it later is a normal Received / Paid). -->
        <div>
          <p class="label">{{ 'sharing.before' | translate }}</p>
          <div
            class="segmented text-xs"
            role="group"
            [attr.aria-label]="'sharing.before' | translate"
          >
            <button
              type="button"
              [class.active]="openingSide() === 'none'"
              (click)="openingSide.set('none')"
            >
              {{ 'sharing.beforeNone' | translate }}
            </button>
            <button
              type="button"
              [class.active]="openingSide() === 'owesMe'"
              (click)="openingSide.set('owesMe')"
            >
              {{ 'sharing.beforeOwesMe' | translate }}
            </button>
            <button
              type="button"
              [class.active]="openingSide() === 'iOwe'"
              (click)="openingSide.set('iOwe')"
            >
              {{ 'sharing.beforeIOwe' | translate }}
            </button>
          </div>
          @if (openingSide() !== 'none') {
            <div class="mt-3 grid grid-cols-1 gap-3 sm:grid-cols-2">
              <div>
                <label class="label" for="person-opening">{{ 'tx.amount' | translate }}</label>
                <input
                  id="person-opening"
                  hlmInput
                  class="num w-full"
                  inputmode="decimal"
                  [value]="openingAmount()"
                  (input)="openingAmount.set($any($event.target).value)"
                />
              </div>
              <div>
                <label class="label" for="person-opening-on">{{
                  'sharing.sinceDate' | translate
                }}</label>
                <app-date-field
                  inputId="person-opening-on"
                  [value]="openingOn()"
                  (valueChange)="openingOn.set($event)"
                />
              </div>
            </div>
            <p class="text-muted-foreground mt-2 text-xs">{{ 'sharing.beforeHint' | translate }}</p>
          }
        </div>
        <div class="flex justify-end gap-2">
          <button type="button" hlmBtn variant="outline" (click)="personOpen.set(false)">
            {{ 'common.cancel' | translate }}
          </button>
          <button hlmBtn [disabled]="!personName().trim() || busy()">
            {{ 'common.save' | translate }}
          </button>
        </div>
      </form>
    </app-modal>

    <app-modal
      [open]="!!settling()"
      [title]="
        (settleDirection() === 'received' ? 'sharing.receivedFrom' : 'sharing.paidTo')
          | translate: { name: settling()?.name }
      "
      (closed)="settling.set(null)"
    >
      <form class="space-y-3" (submit)="$event.preventDefault(); settle()">
        <div class="grid grid-cols-1 gap-3 sm:grid-cols-2">
          <div>
            <label class="label" for="settle-amount">{{ 'tx.amount' | translate }}</label>
            <input
              id="settle-amount"
              hlmInput
              class="num w-full"
              inputmode="decimal"
              [value]="settleAmount()"
              (input)="settleAmount.set($any($event.target).value)"
            />
          </div>
          <div>
            <label class="label" for="settle-date">{{ 'tx.date' | translate }}</label>
            <app-date-field
              inputId="settle-date"
              [value]="settleOn()"
              (valueChange)="settleOn.set($event)"
            />
          </div>
        </div>
        <div>
          <label class="label" for="settle-account">{{
            (settleDirection() === 'received' ? 'sharing.intoAccount' : 'sharing.fromAccount')
              | translate
          }}</label>
          <app-select
            inputId="settle-account"
            [options]="accountOptions()"
            [value]="settleAccount() ?? ''"
            (valueChange)="settleAccount.set($event || null)"
          />
        </div>
        <p class="text-muted-foreground text-xs">
          {{ (settleAccount() ? 'sharing.settleHint' : 'sharing.settleOutsideHint') | translate }}
        </p>
        <div class="flex justify-end gap-2">
          <button type="button" hlmBtn variant="outline" (click)="settling.set(null)">
            {{ 'common.cancel' | translate }}
          </button>
          <button hlmBtn [disabled]="!canSettle() || busy()">
            {{ 'common.save' | translate }}
          </button>
        </div>
      </form>
    </app-modal>

    <app-modal
      [open]="!!paidForMe()"
      [title]="'sharing.paidForMeTitle' | translate: { name: paidForMe()?.name }"
      (closed)="paidForMe.set(null)"
    >
      <form class="space-y-3" (submit)="$event.preventDefault(); savePaidForMe()">
        <div class="grid grid-cols-1 gap-3 sm:grid-cols-2">
          <div>
            <label class="label" for="pfm-amount">{{ 'tx.amount' | translate }}</label>
            <input
              id="pfm-amount"
              hlmInput
              class="num w-full"
              inputmode="decimal"
              [value]="pfmAmount()"
              (input)="pfmAmount.set($any($event.target).value)"
            />
          </div>
          <div>
            <label class="label" for="pfm-date">{{ 'tx.date' | translate }}</label>
            <app-date-field
              inputId="pfm-date"
              [value]="pfmOn()"
              (valueChange)="pfmOn.set($event)"
            />
          </div>
        </div>
        <div>
          <label class="label" for="pfm-category">{{ 'tx.category' | translate }}</label>
          <app-select
            inputId="pfm-category"
            [options]="categoryOptions()"
            [value]="pfmCategory() ?? ''"
            (valueChange)="pfmCategory.set($event || null)"
          />
        </div>
        <div>
          <label class="label" for="pfm-desc">{{ 'tx.description' | translate }}</label>
          <input
            id="pfm-desc"
            hlmInput
            class="w-full"
            maxlength="200"
            [value]="pfmDescription()"
            (input)="pfmDescription.set($any($event.target).value)"
          />
        </div>
        <p class="text-muted-foreground text-xs">{{ 'sharing.paidForMeHint' | translate }}</p>
        <div class="flex justify-end gap-2">
          <button type="button" hlmBtn variant="outline" (click)="paidForMe.set(null)">
            {{ 'common.cancel' | translate }}
          </button>
          <button hlmBtn [disabled]="!(parse(pfmAmount()) ?? 0) || !pfmCategory() || busy()">
            {{ 'common.save' | translate }}
          </button>
        </div>
      </form>
    </app-modal>
  `,
})
export class SharingComponent {
  protected readonly icons = PAGE_ICONS;
  private readonly api = inject(Api);
  private readonly events = inject(DataEvents);
  private readonly toasts = inject(Toasts);
  private readonly i18n = inject(TranslateService);
  private readonly confirm = inject(Confirm);

  protected readonly overview = liveResource({
    params: () => this.events.version(),
    stream: () => this.api.sharing(),
  });
  private readonly accounts = liveResource({
    params: () => this.events.version(),
    stream: () => this.api.accounts(),
  });
  private readonly categories = liveResource({ stream: () => this.api.categories() });
  /** Being paid back lands in, or paying back leaves from, one of the owner's own accounts — or none. */
  protected readonly accountOptions = computed<SelectOption[]>(() => [
    ...(this.accounts.value() ?? [])
      .filter((a) => a.isManual && !a.archived && a.kind !== 'Loan')
      .map(accountOption),
    { value: '', label: this.i18n.instant('sharing.noAccount') },
  ]);
  protected readonly categoryOptions = computed<SelectOption[]>(() => [
    { value: '', label: '—' },
    ...(this.categories.value() ?? [])
      .filter((c) => c.type === 'Expense' && !c.archived)
      .map((c) => ({ value: c.id, label: categoryLabel(this.i18n, c) })),
  ]);
  protected readonly parse = parseAmount;

  protected readonly busy = signal(false);
  protected readonly personOpen = signal(false);
  protected readonly personName = signal('');
  protected readonly settling = signal<SharedPerson | null>(null);
  protected readonly settleDirection = signal<'received' | 'paid'>('received');
  protected readonly settleAmount = signal('');
  protected readonly settleOn = signal(today());
  protected readonly settleAccount = signal<string | null>(null);
  protected readonly canSettle = computed(() => (parseAmount(this.settleAmount()) ?? 0) > 0);

  /** Someone who paid for the owner: an expense of the owner's, paid from that person's account. */
  protected readonly paidForMe = signal<SharedPerson | null>(null);
  protected readonly pfmAmount = signal('');
  protected readonly pfmOn = signal(today());
  protected readonly pfmCategory = signal<string | null>(null);
  protected readonly pfmDescription = signal('');

  protected openPaidForMe(person: SharedPerson) {
    this.paidForMe.set(person);
    this.pfmAmount.set('');
    this.pfmOn.set(today());
    this.pfmCategory.set(null);
    this.pfmDescription.set('');
  }

  protected async savePaidForMe() {
    const person = this.paidForMe();
    const amount = parseAmount(this.pfmAmount());
    if (!person || !amount || !this.pfmCategory()) return;
    this.busy.set(true);
    try {
      await firstValueFrom(
        this.api.createTransaction({
          type: 'Expense',
          occurredOn: this.pfmOn(),
          amount,
          accountId: person.id,
          categoryId: this.pfmCategory(),
          description: this.pfmDescription() || null,
        }),
      );
      this.paidForMe.set(null);
      this.events.bump();
      this.toasts.show(this.i18n.instant('sharing.recorded'));
    } catch (err) {
      this.toasts.error(err);
    } finally {
      this.busy.set(false);
    }
  }

  protected abs = Math.abs;

  protected initials(name: string): string {
    return name
      .trim()
      .split(/\s+/)
      .slice(0, 2)
      .map((w) => w[0]?.toUpperCase() ?? '')
      .join('');
  }

  /** A stable colour per person, readable with white text in both themes. */
  protected color(name: string): string {
    let hash = 0;
    for (const c of name.trim().toLowerCase()) hash = (hash * 31 + c.charCodeAt(0)) >>> 0;
    return `oklch(0.58 0.13 ${hash % 360})`;
  }

  protected readonly editingPerson = signal<SharedPerson | null>(null);
  protected readonly openingSide = signal<'none' | 'owesMe' | 'iOwe'>('none');
  protected readonly openingAmount = signal('');
  protected readonly openingOn = signal(today());

  protected openPerson() {
    this.editingPerson.set(null);
    this.personName.set('');
    this.openingSide.set('none');
    this.openingAmount.set('');
    this.openingOn.set(today());
    this.personOpen.set(true);
  }

  protected editPerson(p: SharedPerson) {
    const opening = p.openingBalance ?? 0;
    this.editingPerson.set(p);
    this.personName.set(p.name);
    this.openingSide.set(opening > 0 ? 'owesMe' : opening < 0 ? 'iOwe' : 'none');
    this.openingAmount.set(opening ? String(Math.abs(opening)).replace('.', ',') : '');
    this.openingOn.set(p.openingBalanceOn ?? today());
    this.personOpen.set(true);
  }

  /** + they owed the owner before the app, − the owner owed them. */
  private openingBalance(): number {
    const amount = parseAmount(this.openingAmount()) ?? 0;
    const side = this.openingSide();
    return side === 'owesMe' ? amount : side === 'iOwe' ? -amount : 0;
  }

  protected async savePerson() {
    this.busy.set(true);
    const body = {
      name: this.personName().trim(),
      openingBalance: this.openingBalance(),
      openingBalanceOn: this.openingOn(),
      institution: null,
      identifier: null,
    };
    try {
      const editing = this.editingPerson();
      if (editing) await firstValueFrom(this.api.updateAccount(editing.id, body));
      else
        await firstValueFrom(this.api.createAccount({ ...body, kind: 'Person', currency: 'EUR' }));
      this.personOpen.set(false);
      this.events.bump();
    } catch (err) {
      this.toasts.error(err);
    } finally {
      this.busy.set(false);
    }
  }

  protected openSettle(person: SharedPerson, direction: 'received' | 'paid') {
    this.settling.set(person);
    this.settleDirection.set(direction);
    this.settleAmount.set(person.balance ? String(Math.abs(person.balance)).replace('.', ',') : '');
    this.settleOn.set(today());
    this.settleAccount.set(this.accountOptions()[0]?.value || null);
  }

  protected async settle() {
    const person = this.settling();
    const amount = parseAmount(this.settleAmount());
    if (!person || !amount) return;
    this.busy.set(true);
    try {
      await firstValueFrom(
        this.api.settle(person.id, {
          direction: this.settleDirection(),
          accountId: this.settleAccount(),
          amount,
          on: this.settleOn(),
        }),
      );
      this.settling.set(null);
      this.events.bump();
      this.toasts.show(this.i18n.instant('sharing.recorded'));
    } catch (err) {
      this.toasts.error(err);
    } finally {
      this.busy.set(false);
    }
  }

  protected async remove(person: SharedPerson) {
    try {
      if (await deleteAccountConfirmed(this.api, this.confirm, this.i18n, person.id)) {
        this.events.bump();
        this.toasts.show(this.i18n.instant('deletion.done'));
      }
    } catch (err) {
      this.toasts.error(err);
    }
  }

  protected async archive(person: SharedPerson) {
    if (person.balance !== 0 && !(await this.confirm.ask(this.i18n.instant('sharing.archiveOpen'))))
      return;
    try {
      await firstValueFrom(this.api.archiveAccount(person.id));
      this.events.bump();
    } catch (err) {
      this.toasts.error(err);
    }
  }
}
