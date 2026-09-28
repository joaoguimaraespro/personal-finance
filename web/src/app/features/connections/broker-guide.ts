import {
  ChangeDetectionStrategy,
  Component,
  booleanAttribute,
  computed,
  input,
} from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCheck, lucideFileSpreadsheet, lucideShieldCheck, lucideX } from '@ng-icons/lucide';

export type GuideBroker = 'Trading212' | 'InteractiveBrokers';

interface GuideContent {
  prefix: string;
  steps: number;
  /** Permissions / sections to switch on. */
  on: number;
  /** Permissions that must stay off (read-only guarantee). */
  off: number;
}

const CONTENT: Record<GuideBroker, GuideContent> = {
  Trading212: { prefix: 'connections.guide.t212', steps: 6, on: 4, off: 2 },
  InteractiveBrokers: { prefix: 'connections.guide.ibkr', steps: 7, on: 7, off: 0 },
};

/**
 * Step-by-step instructions for creating read-only broker credentials. All copy lives in the i18n
 * files (connections.guide.*) and mirrors docs/broker-integrations.md.
 */
@Component({
  selector: 'app-broker-guide',
  imports: [TranslatePipe, NgIcon],
  providers: [provideIcons({ lucideCheck, lucideX, lucideShieldCheck, lucideFileSpreadsheet })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @let c = content();
    <p class="text-sm text-muted-foreground">{{ c.prefix + '.intro' | translate }}</p>

    <ol class="mt-4 space-y-4" [attr.aria-label]="'connections.guide.stepsLabel' | translate">
      @for (n of range(c.steps); track n) {
        <li class="flex gap-3">
          <span
            class="flex size-6 shrink-0 items-center justify-center rounded-full bg-primary/10 text-xs font-semibold text-primary"
            aria-hidden="true"
            >{{ n }}</span
          >
          <div class="min-w-0 text-sm">
            <p class="font-medium">{{ c.prefix + '.steps.' + n + '.title' | translate }}</p>
            <p
              class="guide-body mt-0.5 text-muted-foreground"
              [innerHTML]="c.prefix + '.steps.' + n + '.body' | translate"
            ></p>
            @if (n === permissionsStep()) {
              <div class="mt-3 grid gap-3" [class.sm:grid-cols-2]="c.off > 0">
                <div class="rounded-xl border border-emerald-500/30 bg-emerald-500/5 p-3">
                  <p class="text-xs font-semibold text-emerald-700 dark:text-emerald-300">
                    {{ c.prefix + '.onTitle' | translate }}
                  </p>
                  <ul class="mt-2 space-y-1.5">
                    @for (i of range(c.on); track i) {
                      <li class="flex items-start gap-2 text-xs">
                        <ng-icon
                          name="lucideCheck"
                          class="mt-px shrink-0 text-emerald-600 dark:text-emerald-400"
                          aria-hidden="true"
                        />
                        <span [innerHTML]="c.prefix + '.on.' + i | translate"></span>
                      </li>
                    }
                  </ul>
                </div>
                @if (c.off > 0) {
                  <div class="rounded-xl border border-rose-500/30 bg-rose-500/5 p-3">
                    <p class="text-xs font-semibold text-rose-700 dark:text-rose-300">
                      {{ c.prefix + '.offTitle' | translate }}
                    </p>
                    <ul class="mt-2 space-y-1.5">
                      @for (i of range(c.off); track i) {
                        <li class="flex items-start gap-2 text-xs">
                          <ng-icon
                            name="lucideX"
                            class="mt-px shrink-0 text-rose-600 dark:text-rose-400"
                            aria-hidden="true"
                          />
                          <span [innerHTML]="c.prefix + '.off.' + i | translate"></span>
                        </li>
                      }
                    </ul>
                  </div>
                }
              </div>
            }
          </div>
        </li>
      }
    </ol>

    <div class="mt-5 grid gap-3" [class.md:grid-cols-2]="!compact()">
      <div class="flex gap-3 rounded-xl bg-muted p-3 text-xs">
        <ng-icon
          name="lucideShieldCheck"
          class="mt-px shrink-0 text-base text-primary"
          aria-hidden="true"
        />
        <div>
          <p class="font-semibold">{{ 'connections.guide.securityTitle' | translate }}</p>
          <p class="mt-1 text-muted-foreground">{{ 'connections.guide.security' | translate }}</p>
          <p class="mt-1 text-muted-foreground">{{ c.prefix + '.revoke' | translate }}</p>
        </div>
      </div>
      <div class="flex gap-3 rounded-xl bg-muted p-3 text-xs">
        <ng-icon
          name="lucideFileSpreadsheet"
          class="mt-px shrink-0 text-base text-muted-foreground"
          aria-hidden="true"
        />
        <div>
          <p class="font-semibold">{{ 'connections.guide.csvTitle' | translate }}</p>
          <p class="mt-1 text-muted-foreground">{{ c.prefix + '.csv' | translate }}</p>
        </div>
      </div>
    </div>
  `,
  styles: `
    .guide-body ::ng-deep strong {
      color: var(--foreground);
      font-weight: 600;
    }
    .guide-body ::ng-deep code {
      border-radius: 0.25rem;
      background: var(--muted);
      padding: 0 0.25rem;
      font-size: 0.85em;
    }
  `,
})
export class BrokerGuideComponent {
  readonly kind = input.required<GuideBroker>();
  /** Narrow layout for dialogs. */
  readonly compact = input(false, { transform: booleanAttribute });

  protected readonly content = computed(() => CONTENT[this.kind()]);
  /** The step (for both brokers) that shows which permissions / sections to tick. */
  protected readonly permissionsStep = () => 3;

  protected range(n: number) {
    return Array.from({ length: n }, (_, i) => i + 1);
  }
}
