import { NgTemplateOutlet } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  Directive,
  ElementRef,
  afterNextRender,
  computed,
  inject,
  signal,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { NgIcon } from '@ng-icons/core';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmPopoverImports } from '@spartan-ng/helm/popover';
import { HlmSheetImports } from '@spartan-ng/helm/sheet';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';
import { DayPipe, MoneyPipe, PercentPipe } from '../core/format';
import { Prefs } from '../core/prefs';
import {
  NotificationAction,
  NotificationItem,
  NotificationKind,
  NotificationSeverity,
  Notifications,
} from '../core/notifications';
import { friendlyError } from '../features/connections/credentials';
import { categoryLabel } from '../shared/category-label';
import { APP_ICONS } from '../shared/icons';

const KIND_ICONS: Record<NotificationKind, string> = {
  RecurringDue: 'lucideRepeat',
  InterestToReconcile: 'lucidePercent',
  BrokerAttention: 'lucideUnplug',
  BudgetOver: 'lucideChartPie',
  BudgetNear: 'lucideChartPie',
  GoalReached: 'lucideTrophy',
  AiWrites: 'lucideBot',
  AllocationDue: 'lucideLayers',
  LoanInstalmentDue: 'lucideHandCoins',
  LoanRateRevision: 'lucidePercent',
};

const ACTION_ICONS: Record<NotificationAction, string> = {
  confirm: 'lucideCheck',
  record: 'lucidePlus',
  skip: 'lucideSkipForward',
};

const SEVERITY_TONES: Record<NotificationSeverity, string> = {
  Error: 'bg-rose-50 text-rose-600 dark:bg-rose-500/15 dark:text-rose-300',
  Warning: 'bg-amber-50 text-amber-700 dark:bg-amber-500/15 dark:text-amber-300',
  Info: 'bg-primary/10 text-primary dark:bg-primary/20',
};

/**
 * The popover and sheet render inside an overlay whose container carries role="dialog"; name that dialog after the
 * panel heading so screen readers announce "Notifications" (spartan has no input for it).
 */
@Directive({ selector: '[appLabelDialog]' })
export class LabelDialogDirective {
  constructor() {
    const host = inject(ElementRef<HTMLElement>);
    afterNextRender(() =>
      host.nativeElement
        .closest('[role=dialog]')
        ?.setAttribute('aria-labelledby', 'notifications-title'),
    );
  }
}

/** Phones get a full-width sheet; wider screens a popover anchored to the bell. */
const MOBILE_QUERY = '(max-width: 639.98px)';

/**
 * Header bell: a badge with how many items are new since the owner last opened it (red when something is broken),
 * and the list of what needs attention, each with a direct action or a link to the page that resolves it.
 */
@Component({
  selector: 'app-notification-bell',
  imports: [
    NgIcon,
    NgTemplateOutlet,
    RouterLink,
    TranslatePipe,
    MoneyPipe,
    DayPipe,
    PercentPipe,
    HlmButtonImports,
    HlmPopoverImports,
    HlmSheetImports,
    HlmTooltipImports,
    LabelDialogDirective,
  ],
  providers: [APP_ICONS],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ng-template #bell>
      <ng-icon name="lucideBell" />
      @if (n.unseenCount(); as count) {
        <span
          class="pointer-events-none absolute -top-0.5 -right-0.5 flex h-4 min-w-4 items-center justify-center rounded-full px-1 text-[10px] leading-none font-semibold tabular-nums ring-2 ring-background"
          [class]="
            n.hasError() ? 'bg-destructive text-white' : 'bg-primary text-primary-foreground'
          "
          aria-hidden="true"
          data-testid="notification-badge"
          >{{ count > 9 ? '9+' : count }}</span
        >
      } @else if (n.hasError()) {
        <span
          class="bg-destructive pointer-events-none absolute top-1 right-1 size-2 rounded-full ring-2 ring-background"
          aria-hidden="true"
          data-testid="notification-dot"
        ></span>
      }
    </ng-template>

    @if (mobile()) {
      <hlm-sheet side="right" [state]="state()" (stateChanged)="onState($event)">
        <button
          hlmSheetTrigger
          hlmBtn
          variant="ghost"
          size="icon"
          class="relative"
          [attr.aria-label]="label()"
          data-testid="notification-bell"
        >
          <ng-container [ngTemplateOutlet]="bell" />
        </button>
        <hlm-sheet-content
          *hlmSheetPortal="let ctx"
          class="!w-full gap-0 sm:!max-w-none"
          appLabelDialog
        >
          <ng-container [ngTemplateOutlet]="panel" />
        </hlm-sheet-content>
      </hlm-sheet>
    } @else {
      <hlm-popover align="end" sideOffset="6" [state]="state()" (stateChanged)="onState($event)">
        <button
          hlmPopoverTrigger
          hlmBtn
          variant="ghost"
          size="icon"
          class="relative"
          [attr.aria-label]="label()"
          data-testid="notification-bell"
          [hlmTooltip]="'notifications.title' | translate"
        >
          <ng-container [ngTemplateOutlet]="bell" />
        </button>
        <hlm-popover-content
          *hlmPopoverPortal="let ctx"
          class="w-[26rem] max-w-[calc(100vw-2rem)] gap-0 overflow-hidden p-0"
          appLabelDialog
        >
          <ng-container [ngTemplateOutlet]="panel" />
        </hlm-popover-content>
      </hlm-popover>
    }

    <ng-template #panel>
      <div class="flex items-center gap-2 border-b px-4 py-3 pr-12 sm:pr-4">
        <h2 id="notifications-title" class="text-sm font-semibold">
          {{ 'notifications.title' | translate }}
        </h2>
        @if (n.items().length) {
          <span
            class="bg-muted text-muted-foreground rounded-full px-2 py-0.5 text-xs tabular-nums"
            >{{ n.items().length }}</span
          >
        }
      </div>
      @if (!n.items().length) {
        <div class="flex flex-col items-center gap-2 px-6 py-10 text-center" role="status">
          <span
            class="flex size-10 items-center justify-center rounded-full bg-emerald-50 text-emerald-600 dark:bg-emerald-500/15 dark:text-emerald-300"
          >
            <ng-icon name="lucideCircleCheck" class="text-xl" aria-hidden="true" />
          </span>
          <p class="text-sm font-medium">{{ 'notifications.empty' | translate }}</p>
          <p class="text-muted-foreground text-xs">{{ 'notifications.emptyHint' | translate }}</p>
        </div>
      } @else {
        <ul
          class="max-h-[min(70vh,34rem)] divide-y overflow-y-auto overscroll-contain max-sm:max-h-none max-sm:flex-1"
          [attr.aria-label]="'notifications.title' | translate"
        >
          @for (item of n.items(); track item.id) {
            <li class="flex gap-3 px-4 py-3">
              <span
                class="mt-0.5 flex size-8 shrink-0 items-center justify-center rounded-lg"
                [class]="tone(item.severity)"
              >
                <ng-icon [name]="icon(item.kind)" class="text-base" aria-hidden="true" />
              </span>
              <div class="min-w-0 flex-1">
                <a
                  [routerLink]="item.link"
                  class="block rounded-sm outline-none focus-visible:ring-2 focus-visible:ring-ring"
                  (click)="close()"
                >
                  <span class="flex items-center gap-1.5">
                    <span class="truncate text-sm font-medium">{{ title(item) }}</span>
                    @if (fresh().has(item.id)) {
                      <span
                        class="bg-primary size-1.5 shrink-0 rounded-full"
                        aria-hidden="true"
                      ></span>
                      <span class="sr-only">{{ 'notifications.new' | translate }}</span>
                    }
                  </span>
                  <span class="text-muted-foreground mt-0.5 line-clamp-3 text-xs leading-relaxed">
                    @switch (item.kind) {
                      @case ('RecurringDue') {
                        <span class="text-foreground num font-medium">{{
                          $any(item.args['amount']) | money: $any(item.args['currency'])
                        }}</span>
                        ·
                        <span
                          [class]="item.args['overdue'] ? 'text-amber-700 dark:text-amber-300' : ''"
                          >{{
                            (item.args['overdue']
                              ? 'notifications.overdueSince'
                              : 'notifications.dueToday'
                            ) | translate: { date: (item.date | day) }
                          }}</span
                        >
                      }
                      @case ('InterestToReconcile') {
                        {{
                          'notifications.interestDetail'
                            | translate
                              : {
                                  month: monthLabel($any(item.args['month'])),
                                  amount:
                                    ($any(item.args['amount'])
                                    | money: $any(item.args['currency'])),
                                }
                        }}
                      }
                      @case ('BrokerAttention') {
                        @if (
                          item.args['reason'] === 'expired' || item.args['reason'] === 'expiring'
                        ) {
                          {{
                            'notifications.brokerExpires'
                              | translate: { date: ($any(item.args['expiresOn']) | day) }
                          }}
                        } @else if (friendly($any(item.args['error'])); as key) {
                          {{ key | translate }}
                        } @else {
                          {{ item.args['error'] }}
                        }
                      }
                      @case ('BudgetOver') {
                        {{
                          'notifications.budgetDetail'
                            | translate
                              : {
                                  spent: ($any(item.args['spent']) | money),
                                  budget: ($any(item.args['budget']) | money),
                                }
                        }}
                      }
                      @case ('BudgetNear') {
                        {{
                          'notifications.budgetNearDetail'
                            | translate
                              : {
                                  share: (ratio(item) | pct: 0),
                                  budget: ($any(item.args['budget']) | money),
                                }
                        }}
                      }
                      @case ('GoalReached') {
                        {{
                          'notifications.goalDetail'
                            | translate: { amount: ($any(item.args['amount']) | money) }
                        }}
                      }
                      @case ('LoanInstalmentDue') {
                        <span class="text-foreground num font-medium">{{
                          $any(item.args['amount']) | money
                        }}</span>
                        ·
                        {{
                          'notifications.loanSplit'
                            | translate
                              : {
                                  interest: ($any(item.args['interest']) | money),
                                  capital: ($any(item.args['capital']) | money),
                                }
                        }}
                        ·
                        <span
                          [class]="item.args['overdue'] ? 'text-amber-700 dark:text-amber-300' : ''"
                          >{{ item.date | day }}</span
                        >
                      }
                      @case ('LoanRateRevision') {
                        {{
                          'notifications.loanRevisionDetail'
                            | translate: { date: (item.date | day) }
                        }}
                      }
                      @case ('AllocationDue') {
                        {{
                          'notifications.allocationLeft'
                            | translate
                              : {
                                  remaining: ($any(item.args['remaining']) | money),
                                  target: ($any(item.args['target']) | money),
                                }
                        }}
                      }
                      @case ('AiWrites') {
                        {{
                          (item.args['deleted']
                            ? 'notifications.aiDetailDeleted'
                            : 'notifications.aiDetail'
                          ) | translate: { deleted: item.args['deleted'] }
                        }}
                      }
                    }
                  </span>
                </a>
                @if (item.actions.length) {
                  <div class="mt-2 flex flex-wrap gap-2">
                    @for (action of item.actions; track action) {
                      <button
                        hlmBtn
                        size="sm"
                        [variant]="action === 'skip' ? 'outline' : 'default'"
                        [disabled]="n.busy().has(item.id)"
                        (click)="n.act(item, action); action === 'record' && close()"
                        [attr.aria-label]="
                          (actionLabel(item, action) | translate) + ' · ' + title(item)
                        "
                      >
                        <ng-icon [name]="actionIcon[action]" />
                        {{ actionLabel(item, action) | translate }}
                      </button>
                    }
                  </div>
                }
              </div>
            </li>
          }
        </ul>
      }
    </ng-template>
  `,
})
export class NotificationBellComponent {
  protected readonly n = inject(Notifications);
  private readonly i18n = inject(TranslateService);
  private readonly prefs = inject(Prefs);
  protected readonly state = signal<'open' | 'closed'>('closed');
  /** Items that were new when the list was opened; highlighted until it closes. */
  protected readonly fresh = signal<ReadonlySet<string>>(new Set());
  protected readonly mobile = signal(false);

  /** "Notifications" or "Notifications, 3 new"; re-evaluated when a translation file loads. */
  protected readonly label = computed(() => {
    this.prefs.translations();
    const count = this.n.unseenCount();
    return count
      ? this.i18n.instant('notifications.bellNew', { count })
      : this.i18n.instant('notifications.title');
  });

  constructor() {
    const media = window.matchMedia?.(MOBILE_QUERY);
    if (media) {
      this.mobile.set(media.matches);
      const onChange = (e: MediaQueryListEvent) => {
        this.state.set('closed');
        this.mobile.set(e.matches);
      };
      media.addEventListener('change', onChange);
      inject(DestroyRef).onDestroy(() => media.removeEventListener('change', onChange));
    }
  }

  protected onState(state: 'open' | 'closed') {
    this.state.set(state);
    if (state === 'open') {
      this.fresh.set(new Set(this.n.unseen().map((i) => i.id)));
      this.n.markAllSeen();
      void this.n.refresh();
    } else {
      this.fresh.set(new Set());
    }
  }

  protected close() {
    this.onState('closed');
  }

  protected icon = (kind: NotificationKind) => KIND_ICONS[kind];
  protected tone = (severity: NotificationSeverity) => SEVERITY_TONES[severity];
  protected friendly = friendlyError;
  protected readonly actionIcon = ACTION_ICONS;
  protected actionLabel = (item: NotificationItem, action: NotificationAction) =>
    action === 'record'
      ? 'allocation.record'
      : action === 'confirm'
        ? 'recurring.confirm'
        : item.kind === 'AllocationDue'
          ? 'allocation.notThisMonth'
          : 'recurring.skip';

  protected title(item: NotificationItem): string {
    this.prefs.translations(); // a language switch re-renders the titles
    if (item.kind === 'BudgetOver' || item.kind === 'BudgetNear') {
      const category = categoryLabel(this.i18n, {
        key: item.args['categoryKey'] as string,
        name: item.args['category'] as string,
      });
      return this.i18n.instant(
        item.kind === 'BudgetOver' ? 'notifications.budgetOver' : 'notifications.budgetNear',
        { category },
      );
    }
    if (item.kind === 'InterestToReconcile') {
      return this.i18n.instant('notifications.interestTitle', { account: item.args['account'] });
    }
    return this.n.describe(item, (key, params) => this.i18n.instant(key, params))[0];
  }

  protected ratio(item: NotificationItem): number {
    const budget = Number(item.args['budget']);
    return budget > 0 ? Number(item.args['spent']) / budget : 0;
  }

  protected monthLabel(period: string): string {
    const [y, m] = period.split('-').map(Number);
    return new Intl.DateTimeFormat(this.prefs.locale(), { month: 'long', year: 'numeric' }).format(
      new Date(y, m - 1, 1),
    );
  }
}
