import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { Prefs } from '../../core/prefs';
import { SelectComponent, SelectOption } from '../../shared/select';
import { liveResource } from '../../core/resource';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { Api } from '../../core/api';
import { DataEvents } from '../../core/data-events';
import { DayPipe, MoneyPipe, PercentPipe } from '../../core/format';
import { Goal } from '../../core/models';
import { Toasts } from '../../core/toast';
import { ModalComponent } from '../../shared/modal';
import { ProgressComponent } from '../../shared/progress';
import { parseAmount } from '../transactions/quick-add';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { DateFieldComponent } from '../../shared/date-field';
import { NgIcon } from '@ng-icons/core';
import { APP_ICONS, PAGE_ICONS } from '../../shared/icons';
import { PageHeaderComponent } from '../../shared/page-header';
import { EmptyStateComponent } from '../../shared/empty-state';
import { StatusBadgeComponent } from '../../shared/status-badge';

@Component({
  selector: 'app-goals',
  imports: [
    SelectComponent,
    PageHeaderComponent,
    EmptyStateComponent,
    StatusBadgeComponent,
    NgIcon,
    DateFieldComponent,
    HlmInputImports,
    HlmButtonImports,
    TranslatePipe,
    MoneyPipe,
    PercentPipe,
    DayPipe,
    ProgressComponent,
    ModalComponent,
  ],
  providers: [APP_ICONS],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-page-header
      [icon]="icons.goals"
      [title]="'nav.goals' | translate"
      [subtitle]="'goals.subtitle' | translate"
    >
      <button hlmBtn class="self-start sm:self-auto" (click)="open(null)">
        <ng-icon name="lucidePlus" />{{ 'goals.new' | translate }}
      </button>
    </app-page-header>

    <div class="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
      @for (g of goals.value() ?? []; track g.id) {
        <div class="card card-hover flex flex-col">
          <div class="flex items-start justify-between gap-3">
            <div class="flex min-w-0 items-center gap-3">
              <span
                class="flex size-10 shrink-0 items-center justify-center rounded-full"
                [class]="
                  g.achieved
                    ? 'bg-emerald-100 text-emerald-700 dark:bg-emerald-500/15 dark:text-emerald-300'
                    : 'bg-primary/10 text-primary dark:bg-primary/20'
                "
                aria-hidden="true"
              >
                <ng-icon [name]="g.achieved ? 'lucideTrophy' : 'lucideTarget'" class="text-lg" />
              </span>
              <div class="min-w-0">
                <p class="truncate font-semibold">{{ g.name }}</p>
                @if (g.achieved) {
                  <app-status-badge class="mt-1" tone="success" icon="lucideTrophy">{{
                    'goals.achieved' | translate
                  }}</app-status-badge>
                } @else {
                  <app-status-badge class="mt-1" icon="lucideHourglass">{{
                    'goals.inProgress' | translate
                  }}</app-status-badge>
                }
              </div>
            </div>
          </div>
          <p class="num mt-4 text-2xl font-semibold tracking-tight">{{ g.progress | pct: 0 }}</p>
          @if (g.accountName) {
            <p class="text-muted-foreground mt-0.5 flex items-center gap-1 text-xs">
              <ng-icon name="lucidePiggyBank" aria-hidden="true" />{{
                'goals.followsAccount' | translate: { account: g.accountName }
              }}
            </p>
          }
          <app-progress class="mt-2 block" [value]="g.progress" />
          <dl class="num mt-3 grid grid-cols-2 gap-2 text-xs text-muted-foreground">
            <div>
              <dt>{{ 'goals.current' | translate }}</dt>
              <dd class="font-medium text-foreground">{{ g.currentAmount | money }}</dd>
            </div>
            <div>
              <dt>{{ 'goals.target' | translate }}</dt>
              <dd class="font-medium text-foreground">{{ g.targetAmount | money }}</dd>
            </div>
            @if (g.targetDate) {
              <div>
                <dt>{{ 'goals.by' | translate }}</dt>
                <dd>{{ g.targetDate | day }}</dd>
              </div>
              <div>
                <dt>{{ 'goals.monthlyNeeded' | translate }}</dt>
                <dd>{{ g.monthlyNeeded | money }}</dd>
              </div>
            }
          </dl>
          <div class="flex-1"></div>
          <div class="mt-4 flex gap-2 border-t pt-4">
            <button hlmBtn variant="outline" size="sm" (click)="open(g)">
              <ng-icon name="lucidePencil" />{{ 'common.edit' | translate }}
            </button>
            <button hlmBtn variant="ghost" size="sm" (click)="archive(g)">
              <ng-icon name="lucideArchive" />{{ 'common.archive' | translate }}
            </button>
          </div>
        </div>
      } @empty {
        <div class="card col-span-full !p-0">
          <app-empty-state
            [icon]="icons.goals"
            [title]="'goals.empty' | translate"
            [text]="'goals.subtitle' | translate"
          >
            <button hlmBtn size="sm" (click)="open(null)">
              <ng-icon name="lucidePlus" />{{ 'goals.new' | translate }}
            </button>
          </app-empty-state>
        </div>
      }
    </div>

    <app-modal
      [open]="formOpen()"
      [title]="(editing() ? 'goals.edit' : 'goals.new') | translate"
      (closed)="formOpen.set(false)"
    >
      <form class="form-grid" (submit)="$event.preventDefault(); save()">
        <div class="col-span-2">
          <label class="label" for="g-name">{{ 'common.name' | translate }}</label>
          <input
            id="g-name"
            hlmInput
            required
            maxlength="80"
            [value]="name()"
            (input)="name.set($any($event.target).value)"
            placeholder="Emergency fund"
          />
        </div>
        <div>
          <label class="label" for="g-target">{{ 'goals.target' | translate }}</label>
          <input
            id="g-target"
            hlmInput
            class="num"
            inputmode="decimal"
            required
            [value]="target()"
            (input)="target.set($any($event.target).value)"
          />
        </div>
        <div>
          <label class="label" for="g-date">{{ 'goals.by' | translate }}</label>
          <app-date-field
            inputId="g-date"
            [value]="date()"
            (valueChange)="date.set($event)"
            clearable
          />
        </div>
        <div class="col-span-2">
          <label class="label" for="g-account">{{ 'goals.account' | translate }}</label>
          <app-select
            inputId="g-account"
            [options]="accountOptions()"
            [value]="accountId()"
            (valueChange)="accountId.set($event)"
          />
        </div>
        @if (!accountId()) {
          <div>
            <label class="label" for="g-start">{{ 'goals.starting' | translate }}</label>
            <input
              id="g-start"
              hlmInput
              class="num"
              inputmode="decimal"
              [value]="starting()"
              (input)="starting.set($any($event.target).value)"
            />
          </div>
          <div>
            <label class="label" for="g-manual">{{ 'goals.manual' | translate }}</label>
            <input
              id="g-manual"
              hlmInput
              class="num"
              inputmode="decimal"
              [value]="manual()"
              (input)="manual.set($any($event.target).value)"
            />
          </div>
        }
        <p class="col-span-2 text-xs text-muted-foreground">
          {{ (accountId() ? 'goals.howProgressAccount' : 'goals.howProgress') | translate }}
        </p>
        <div class="col-span-2 flex justify-end gap-2">
          <button type="button" hlmBtn variant="outline" (click)="formOpen.set(false)">
            {{ 'common.cancel' | translate }}
          </button>
          <button hlmBtn><ng-icon name="lucideSave" />{{ 'common.save' | translate }}</button>
        </div>
      </form>
    </app-modal>
  `,
})
export class GoalsComponent {
  protected readonly icons = PAGE_ICONS;
  private readonly api = inject(Api);
  private readonly events = inject(DataEvents);
  private readonly toasts = inject(Toasts);
  private readonly i18n = inject(TranslateService);
  private readonly prefs = inject(Prefs);
  protected readonly goals = liveResource({
    params: () => this.events.version(),
    stream: () => this.api.goals(),
  });
  protected readonly formOpen = signal(false);
  protected readonly editing = signal<Goal | null>(null);
  protected readonly name = signal('');
  protected readonly target = signal('');
  protected readonly date = signal('');
  protected readonly starting = signal('');
  protected readonly manual = signal('');
  protected readonly accountId = signal('');
  private readonly accounts = liveResource({ stream: () => this.api.accounts() });
  /** Only money set aside can be followed: savings, bank and cash accounts in EUR — never a broker or a card. */
  protected readonly accountOptions = computed<SelectOption[]>(() => {
    this.prefs.translations();
    return [
      { value: '', label: this.i18n.instant('goals.noAccount') },
      ...(this.accounts.value() ?? [])
        .filter(
          (a) =>
            ['Savings', 'Bank', 'Cash', 'Other'].includes(a.kind) &&
            a.currency === 'EUR' &&
            !a.archived,
        )
        .map((a) => ({ value: a.id, label: a.name })),
    ];
  });

  protected open(g: Goal | null) {
    this.editing.set(g);
    this.name.set(g?.name ?? '');
    this.target.set(g ? String(g.targetAmount) : '');
    this.date.set(g?.targetDate ?? '');
    this.starting.set(g ? String(g.startingAmount) : '');
    this.manual.set(g?.manualCurrentAmount != null ? String(g.manualCurrentAmount) : '');
    this.accountId.set(g?.accountId ?? '');
    this.formOpen.set(true);
  }

  protected async save() {
    const body = {
      name: this.name(),
      targetAmount: parseAmount(this.target()),
      targetDate: this.date() || null,
      startingAmount: parseAmount(this.starting()) ?? 0,
      manualCurrentAmount: parseAmount(this.manual()),
      icon: null,
      accountId: this.accountId() || null,
    };
    try {
      const g = this.editing();
      if (g) await firstValueFrom(this.api.updateGoal(g.id, body));
      else await firstValueFrom(this.api.createGoal(body));
      this.formOpen.set(false);
      this.events.bump();
      this.toasts.show(this.i18n.instant('common.saved'));
    } catch (err) {
      this.toasts.error(err);
    }
  }

  protected async archive(g: Goal) {
    try {
      await firstValueFrom(this.api.archiveGoal(g.id));
      this.events.bump();
    } catch (err) {
      this.toasts.error(err);
    }
  }
}
