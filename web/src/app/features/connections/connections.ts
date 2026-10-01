import {
  ChangeDetectionStrategy,
  Component,
  OnDestroy,
  computed,
  inject,
  signal,
} from '@angular/core';
import { liveResource } from '../../core/resource';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { Api } from '../../core/api';
import { DataEvents } from '../../core/data-events';
import { DateTimePipe, DayPipe } from '../../core/format';
import { Broker, Connection, ProviderInfo, SyncJob } from '../../core/models';
import { Toasts } from '../../core/toast';
import { ModalComponent } from '../../shared/modal';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmSkeletonImports } from '@spartan-ng/helm/skeleton';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { Confirm } from '../../core/confirm';
import { DateFieldComponent } from '../../shared/date-field';
import { NgIcon } from '@ng-icons/core';
import { BrokerGuideComponent, GuideBroker } from './broker-guide';
import { FieldProblem, friendlyError, validateCredentials } from './credentials';
import { APP_ICONS, PAGE_ICONS } from '../../shared/icons';
import { PageHeaderComponent } from '../../shared/page-header';
import { EmptyStateComponent } from '../../shared/empty-state';
import { StatusBadgeComponent, StatusTone } from '../../shared/status-badge';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';

type CredentialField = ProviderInfo['fields'][number];

/** Credentials are write-only: the form can set them, the page never displays them. */
@Component({
  selector: 'app-connections',
  imports: [
    PageHeaderComponent,
    EmptyStateComponent,
    StatusBadgeComponent,
    HlmTooltipImports,
    NgIcon,
    DateFieldComponent,
    HlmInputImports,
    HlmButtonImports,
    TranslatePipe,
    DayPipe,
    DateTimePipe,
    ModalComponent,
    BrokerGuideComponent,
    HlmSkeletonImports,
  ],
  providers: [APP_ICONS],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-page-header
      [icon]="icons.connections"
      [title]="'nav.connections' | translate"
      [subtitle]="'connections.subtitle' | translate"
    >
      <div class="flex flex-wrap gap-2">
        @for (p of providers.value() ?? []; track p.kind) {
          <button hlmBtn variant="outline" (click)="openNew(p)">
            <ng-icon name="lucidePlus" />{{ p.name }}
          </button>
        }
      </div>
    </app-page-header>

    <div class="grid gap-4 lg:grid-cols-2">
      @for (c of connections.value() ?? []; track c.id) {
        <section
          class="card card-hover relative flex flex-col overflow-hidden"
          [attr.aria-busy]="isRunning(c)"
        >
          @if (isRunning(c)) {
            <div class="loading-strip" aria-hidden="true"></div>
          }
          <div class="flex items-start justify-between gap-3">
            <div class="flex min-w-0 items-center gap-3">
              <span
                class="bg-muted text-muted-foreground flex size-10 shrink-0 items-center justify-center rounded-full"
                aria-hidden="true"
              >
                <ng-icon name="lucideLandmark" class="text-lg" />
              </span>
              <div class="min-w-0">
                <p class="truncate font-semibold">{{ c.displayName }}</p>
                <p class="text-muted-foreground flex items-center gap-1 text-xs">
                  {{ 'source.' + c.kind | translate }} ·
                  <ng-icon name="lucideLock" class="text-[11px]" aria-hidden="true" />{{
                    'connections.readOnly' | translate
                  }}
                </p>
              </div>
            </div>
            <app-status-badge [tone]="statusTone(c)">{{
              'connections.status.' + c.status | translate
            }}</app-status-badge>
          </div>
          <dl class="mt-4 grid grid-cols-2 gap-2 text-sm">
            <div>
              <dt class="text-muted-foreground flex items-center gap-1 text-xs">
                <ng-icon name="lucideClock" aria-hidden="true" />{{
                  'connections.lastSync' | translate
                }}
              </dt>
              <dd class="mt-0.5">{{ c.lastSuccessfulSyncUtc | dateTime }}</dd>
            </div>
            <div>
              <dt class="text-muted-foreground flex items-center gap-1 text-xs">
                <ng-icon name="lucideActivity" aria-hidden="true" />{{
                  'connections.lastRun' | translate
                }}
              </dt>
              <dd class="mt-0.5">
                @if (c.lastJob; as j) {
                  <ng-icon
                    [name]="outcomeIcon[j.outcome]"
                    class="mr-1 align-[-2px]"
                    [class]="outcomeTone[j.outcome]"
                    aria-hidden="true"
                  />{{ 'connections.outcome.' + j.outcome | translate }}
                  @if (j.outcome !== 'Running') {
                    · +{{ j.imported }} / ~{{ j.updated }}
                  }
                } @else if (c.status !== 'Disabled') {
                  <span class="inline-flex items-center gap-1.5 text-muted-foreground">
                    <ng-icon
                      name="lucideLoaderCircle"
                      class="motion-safe:animate-spin"
                      aria-hidden="true"
                    />{{ 'connections.checking' | translate }}
                  </span>
                } @else {
                  —
                }
              </dd>
            </div>
            @if (c.credentialsExpireOn) {
              <div
                class="col-span-2"
                [class]="expiresSoon(c) ? 'text-amber-700 dark:text-amber-300' : ''"
              >
                <dt class="text-muted-foreground flex items-center gap-1 text-xs">
                  <ng-icon name="lucideCalendarClock" aria-hidden="true" />{{
                    'connections.expires' | translate
                  }}
                </dt>
                <dd class="mt-0.5 flex items-center gap-1">
                  @if (expiresSoon(c)) {
                    <ng-icon name="lucideTriangleAlert" aria-hidden="true" />
                  }
                  {{ c.credentialsExpireOn | day }}
                </dd>
              </div>
            }
          </dl>
          @if (isRunning(c)) {
            <div class="mt-3 flex gap-2 rounded-lg bg-primary/10 p-2.5 text-xs" role="status">
              <ng-icon
                name="lucideLoaderCircle"
                class="mt-px shrink-0 text-primary motion-safe:animate-spin"
                aria-hidden="true"
              />
              <div>
                <p class="font-medium">
                  {{ 'connections.syncing' | translate }}
                  <span class="font-normal text-muted-foreground">
                    · {{ 'connections.syncingFor' | translate: { time: elapsed(c) } }}
                  </span>
                </p>
                @if (!c.lastSuccessfulSyncUtc) {
                  <p class="mt-0.5 text-muted-foreground">
                    {{ 'connections.firstSyncHint' | translate }}
                  </p>
                }
              </div>
            </div>
          }
          @if (c.lastError) {
            <div
              class="mt-3 flex gap-2 rounded-lg bg-rose-50 p-3 text-xs text-rose-700 ring-1 ring-rose-600/10 ring-inset dark:bg-rose-500/10 dark:text-rose-300 dark:ring-rose-400/20"
            >
              <ng-icon name="lucideCircleAlert" class="mt-px shrink-0 text-sm" aria-hidden="true" />
              <div class="min-w-0">
                @if (friendly(c.lastError); as key) {
                  <!-- The broker's original message stays available on hover for troubleshooting. -->
                  <p [title]="c.lastError">{{ key | translate }}</p>
                } @else {
                  <p>{{ c.lastError }}</p>
                }
                @if (guideFor(c.kind); as g) {
                  <button
                    type="button"
                    class="mt-1 font-medium underline underline-offset-2"
                    (click)="showGuide(g)"
                  >
                    {{ 'connections.fixHint' | translate }}
                  </button>
                }
              </div>
            </div>
          }
          <div class="flex-1"></div>
          <div class="mt-4 flex flex-wrap items-center gap-2 border-t pt-4">
            <button
              hlmBtn
              size="sm"
              [disabled]="c.status === 'Disabled' || isRunning(c)"
              (click)="sync(c)"
            >
              @if (isRunning(c)) {
                <ng-icon
                  name="lucideLoaderCircle"
                  class="motion-safe:animate-spin"
                  aria-hidden="true"
                />
                {{ 'connections.syncing' | translate }}
              } @else {
                <ng-icon name="lucideRefreshCw" aria-hidden="true" />{{
                  'connections.syncNow' | translate
                }}
              }
            </button>
            @if (c.kind === 'Trading212') {
              <button hlmBtn variant="outline" size="sm" (click)="csv.click()">
                <ng-icon name="lucideFileUp" />{{ 'connections.importCsv' | translate }}
              </button>
              <input
                #csv
                type="file"
                accept=".csv,text/csv"
                class="hidden"
                (change)="
                  importCsv(c, $any($event.target).files?.[0]); $any($event.target).value = ''
                "
              />
            }
            <div class="ml-auto flex items-center gap-1">
              <button
                hlmBtn
                variant="ghost"
                size="icon-sm"
                (click)="showJobs(c)"
                [attr.aria-label]="'connections.history' | translate"
                [hlmTooltip]="'connections.history' | translate"
              >
                <ng-icon name="lucideHistory" />
              </button>
              <button
                hlmBtn
                variant="ghost"
                size="icon-sm"
                (click)="openCredentials(c)"
                [attr.aria-label]="'connections.updateCredentials' | translate"
                [hlmTooltip]="'connections.updateCredentials' | translate"
              >
                <ng-icon name="lucideKeyRound" />
              </button>
              <button
                hlmBtn
                variant="ghost"
                size="icon-sm"
                (click)="setEnabled(c, c.status === 'Disabled')"
                [attr.aria-label]="
                  (c.status === 'Disabled' ? 'connections.enable' : 'connections.disable')
                    | translate
                "
                [hlmTooltip]="
                  (c.status === 'Disabled' ? 'connections.enable' : 'connections.disable')
                    | translate
                "
              >
                <ng-icon
                  [name]="c.status === 'Disabled' ? 'lucideCirclePlay' : 'lucideCirclePause'"
                />
              </button>
              <button
                hlmBtn
                variant="ghost"
                size="icon-sm"
                class="text-destructive hover:text-destructive"
                (click)="remove(c)"
                [attr.aria-label]="'common.delete' | translate"
                [hlmTooltip]="'common.delete' | translate"
              >
                <ng-icon name="lucideTrash2" />
              </button>
            </div>
          </div>
        </section>
      } @empty {
        @if (!connections.hasValue() && connections.isLoading()) {
          @for (i of [1, 2]; track i) {
            <section class="card space-y-3" aria-hidden="true">
              <div class="flex justify-between">
                <div class="space-y-2">
                  <hlm-skeleton class="h-4 w-36" />
                  <hlm-skeleton class="h-3 w-48" />
                </div>
                <hlm-skeleton class="h-5 w-16 rounded-full" />
              </div>
              <div class="grid grid-cols-2 gap-2">
                <hlm-skeleton class="h-8" />
                <hlm-skeleton class="h-8" />
              </div>
              <div class="flex gap-2">
                <hlm-skeleton class="h-8 w-28" />
                <hlm-skeleton class="h-8 w-20" />
              </div>
            </section>
          }
        } @else if (connections.hasValue()) {
          <section class="card col-span-full !p-0">
            <app-empty-state
              icon="lucidePlug"
              [title]="'connections.emptyTitle' | translate"
              [text]="'connections.emptyBody' | translate"
            >
              <div class="flex flex-wrap justify-center gap-2">
                @for (p of providers.value() ?? []; track p.kind) {
                  <button hlmBtn size="sm" (click)="openNew(p)">
                    <ng-icon name="lucidePlus" />{{ p.name }}
                  </button>
                }
              </div>
            </app-empty-state>
          </section>
        }
      }
    </div>

    @if (guideBrokers().length) {
      <details
        id="connection-guide"
        class="card group mt-6"
        [open]="guideOpen()"
        (toggle)="guideToggled.set($any($event.target).open)"
      >
        <summary
          class="-m-5 flex cursor-pointer list-none items-center gap-3 rounded-xl p-5 focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none"
        >
          <span
            class="bg-primary/10 text-primary dark:bg-primary/20 flex size-9 shrink-0 items-center justify-center rounded-lg"
            aria-hidden="true"
          >
            <ng-icon name="lucideBookOpen" class="text-lg" />
          </span>
          <span class="min-w-0 flex-1">
            <span class="block font-semibold">{{ 'connections.guide.title' | translate }}</span>
            <span class="block text-sm text-muted-foreground">{{
              'connections.guide.subtitle' | translate
            }}</span>
          </span>
          <ng-icon
            name="lucideChevronDown"
            class="text-muted-foreground shrink-0 transition-transform group-open:rotate-180"
            aria-hidden="true"
          />
        </summary>
        <div class="mt-8">
          @if (guideBrokers().length > 1) {
            <div class="segmented mb-4" role="tablist">
              @for (b of guideBrokers(); track b) {
                <button
                  type="button"
                  role="tab"
                  [class.active]="guideKind() === b"
                  [attr.aria-selected]="guideKind() === b"
                  (click)="pickedGuide.set(b)"
                >
                  {{ 'source.' + b | translate }}
                </button>
              }
            </div>
          }
          <app-broker-guide [kind]="guideKind()" />
        </div>
      </details>
    }

    <app-modal
      [open]="!!provider()"
      [title]="provider()?.name ?? ''"
      width="42rem"
      (closed)="provider.set(null)"
    >
      @if (provider(); as p) {
        <form
          class="space-y-3"
          (submit)="$event.preventDefault(); save()"
          autocomplete="off"
          novalidate
        >
          @if (guideFor(p.kind); as g) {
            <details class="rounded-xl border border-border p-3">
              <summary class="flex cursor-pointer items-center gap-2 text-sm font-medium">
                <ng-icon name="lucideBookOpen" class="text-primary" aria-hidden="true" />
                {{ 'connections.setupGuide' | translate }}
              </summary>
              <div class="mt-3">
                <app-broker-guide [kind]="g" compact />
              </div>
            </details>
          } @else {
            <p class="bg-muted text-muted-foreground flex gap-2 rounded-xl p-3 text-xs">
              <ng-icon name="lucideInfo" class="mt-px shrink-0" aria-hidden="true" />{{
                p.setupHint
              }}
            </p>
          }
          @if (!editingId()) {
            <div>
              <label class="label" for="c-name">{{ 'common.name' | translate }}</label>
              <input
                id="c-name"
                hlmInput
                required
                maxlength="80"
                [value]="name()"
                (input)="name.set($any($event.target).value)"
              />
            </div>
          }
          @for (f of p.fields; track f.key) {
            @let problem = problemFor(f.key);
            @if (f.key === 'environment') {
              <div role="radiogroup" [attr.aria-labelledby]="'c-' + f.key + '-label'">
                <p class="label" [id]="'c-' + f.key + '-label'">{{ fieldLabel(f) }}</p>
                <div class="segmented">
                  @for (env of environments; track env) {
                    <button
                      type="button"
                      role="radio"
                      [class.active]="(fields()[f.key] || 'live') === env"
                      [attr.aria-checked]="(fields()[f.key] || 'live') === env"
                      (click)="setField(f.key, env)"
                    >
                      {{ 'connections.env.' + env | translate }}
                    </button>
                  }
                </div>
                <p class="mt-1 text-[11px] text-muted-foreground">{{ fieldHint(f) }}</p>
              </div>
            } @else {
              <div>
                <label class="label" [for]="'c-' + f.key"
                  >{{ fieldLabel(f) }}{{ f.required ? ' *' : '' }}</label
                >
                <input
                  [id]="'c-' + f.key"
                  hlmInput
                  class="font-mono"
                  [type]="f.secret ? 'password' : 'text'"
                  autocomplete="off"
                  spellcheck="false"
                  [required]="f.required"
                  [attr.aria-invalid]="problem ? true : null"
                  [attr.aria-describedby]="'c-' + f.key + '-hint'"
                  (input)="setField(f.key, $any($event.target).value)"
                />
                @if (problem) {
                  <p
                    [id]="'c-' + f.key + '-hint'"
                    class="mt-1 text-[11px] text-destructive"
                    role="alert"
                  >
                    {{ problem.message | translate }}
                  </p>
                } @else if (fieldHint(f); as hint) {
                  <p [id]="'c-' + f.key + '-hint'" class="mt-1 text-[11px] text-muted-foreground">
                    {{ hint }}
                  </p>
                }
              </div>
            }
          }
          @if (p.kind === 'InteractiveBrokers') {
            <div>
              <label class="label" for="c-exp">{{ 'connections.expires' | translate }}</label>
              <app-date-field
                inputId="c-exp"
                [value]="expires()"
                (valueChange)="expires.set($event)"
                clearable
              />
              <p class="mt-1 text-[11px] text-muted-foreground">
                {{ 'connections.expiresHint' | translate }}
              </p>
            </div>
          }
          <p class="text-muted-foreground flex gap-1.5 text-[11px]">
            <ng-icon
              name="lucideShieldCheck"
              class="mt-px shrink-0 text-primary"
              aria-hidden="true"
            />
            {{ 'connections.credentialsNote' | translate }}
          </p>
          <div class="flex justify-end gap-2">
            <button type="button" hlmBtn variant="outline" (click)="provider.set(null)">
              {{ 'common.cancel' | translate }}
            </button>
            <button hlmBtn [disabled]="busy()">
              <ng-icon
                [name]="busy() ? 'lucideLoaderCircle' : 'lucideSave'"
                [class]="busy() ? 'motion-safe:animate-spin' : ''"
              />{{ 'common.save' | translate }}
            </button>
          </div>
        </form>
      }
    </app-modal>

    <app-modal
      [open]="!!jobs()"
      [title]="'connections.history' | translate"
      width="40rem"
      (closed)="jobs.set(null)"
    >
      <ul class="space-y-2">
        @for (j of jobs() ?? []; track j.id) {
          <li class="rounded-xl border border-border p-3 text-sm">
            <div class="flex justify-between">
              <span class="inline-flex items-center gap-1.5 font-medium"
                ><ng-icon
                  [name]="outcomeIcon[j.outcome]"
                  [class]="outcomeTone[j.outcome]"
                  aria-hidden="true"
                />{{ 'connections.outcome.' + j.outcome | translate }} ·
                {{ 'connections.trigger.' + j.trigger | translate }}</span
              >
              <span class="text-xs text-muted-foreground">{{ j.startedAtUtc | dateTime }}</span>
            </div>
            <p class="num text-xs text-muted-foreground">
              {{
                'connections.counts'
                  | translate: { imported: j.imported, updated: j.updated, ignored: j.ignored }
              }}
            </p>
            @for (e of j.errors.slice(0, 5); track $index) {
              <p class="tone-neg text-xs">{{ e }}</p>
            }
          </li>
        }
      </ul>
    </app-modal>
  `,
})
export class ConnectionsComponent implements OnDestroy {
  protected readonly icons = PAGE_ICONS;
  protected readonly outcomeIcon: Record<SyncJob['outcome'], string> = {
    Running: 'lucideLoaderCircle',
    Succeeded: 'lucideCircleCheck',
    PartiallySucceeded: 'lucideTriangleAlert',
    Failed: 'lucideCircleX',
  };
  protected readonly outcomeTone: Record<SyncJob['outcome'], string> = {
    Running: 'text-muted-foreground motion-safe:animate-spin',
    Succeeded: 'tone-pos',
    PartiallySucceeded: 'text-amber-600 dark:text-amber-400',
    Failed: 'tone-neg',
  };
  private readonly api = inject(Api);
  private readonly events = inject(DataEvents);
  private readonly toasts = inject(Toasts);
  private readonly i18n = inject(TranslateService);
  private readonly confirm = inject(Confirm);

  private readonly refresh = signal(0);
  protected readonly providers = liveResource({ stream: () => this.api.providers() });
  protected readonly connections = liveResource({
    params: () => ({ v: this.events.version(), r: this.refresh() }),
    // Poll refreshes are background requests: the card shows progress, the global bar stays quiet.
    stream: ({ params }) => this.api.connections(params.r > 0),
  });
  private readonly running = computed(() =>
    (this.connections.value() ?? []).some((c) => c.lastJob?.outcome === 'Running'),
  );
  /** A new connection's first sync is queued behind other syncs and has no job yet. */
  private readonly awaitingFirstSync = computed(() =>
    (this.connections.value() ?? []).some((c) => c.status !== 'Disabled' && !c.lastJob),
  );
  // Syncs run in the background: poll while one is running or queued, then refresh every view.
  private readonly poll = setInterval(() => {
    this.now.set(Date.now());
    const waiting = this.awaitingFirstSync() && Date.now() - this.pendingSince < 120_000;
    if (this.running() || waiting || this.pendingSince) {
      this.refresh.update((v) => v + 1);
      if (
        this.pendingSince &&
        !this.running() &&
        !waiting &&
        Date.now() - this.pendingSince > 4000
      ) {
        this.pendingSince = 0;
        this.events.bump();
      }
    }
  }, 2000);
  private pendingSince = 0;
  private readonly now = signal(Date.now());

  protected isRunning = (c: Connection) => c.lastJob?.outcome === 'Running';

  protected elapsed(c: Connection): string {
    const started = c.lastJob ? Date.parse(c.lastJob.startedAtUtc) : this.now();
    const minutes = Math.floor((this.now() - started) / 60_000);
    return minutes < 1
      ? this.i18n.instant('connections.lessThanMinute')
      : this.i18n.instant('connections.minutes', { n: minutes });
  }

  protected readonly provider = signal<ProviderInfo | null>(null);
  protected readonly editingId = signal<string | null>(null);
  protected readonly name = signal('');
  protected readonly fields = signal<Record<string, string>>({});
  protected readonly expires = signal('');
  protected readonly busy = signal(false);
  protected readonly jobs = signal<SyncJob[] | null>(null);
  protected readonly problems = signal<FieldProblem[]>([]);
  protected readonly environments = ['live', 'demo'];
  protected readonly friendly = friendlyError;

  /** Brokers with a step-by-step guide, in the order the server lists them. */
  protected readonly guideBrokers = computed(() =>
    (this.providers.value() ?? []).map((p) => p.kind).filter((k) => this.isGuided(k)),
  );
  protected readonly pickedGuide = signal<GuideBroker | null>(null);
  protected readonly guideKind = computed(
    () => this.pickedGuide() ?? this.guideBrokers()[0] ?? 'Trading212',
  );
  /** The guide is open until the first broker is connected; afterwards the user decides. */
  protected readonly guideToggled = signal<boolean | null>(null);
  protected readonly guideOpen = computed(
    () => this.guideToggled() ?? (this.connections.value()?.length ?? 0) === 0,
  );

  ngOnDestroy() {
    clearInterval(this.poll);
  }

  protected statusTone(c: Connection): StatusTone {
    return c.status === 'Active'
      ? 'success'
      : c.status === 'NeedsAttention'
        ? 'warning'
        : 'neutral';
  }

  protected expiresSoon(c: Connection) {
    return (
      !!c.credentialsExpireOn &&
      new Date(c.credentialsExpireOn).getTime() - Date.now() < 30 * 86_400_000
    );
  }

  protected isGuided(kind: Broker): kind is GuideBroker {
    return kind === 'Trading212' || kind === 'InteractiveBrokers';
  }

  protected guideFor(kind: Broker): GuideBroker | null {
    return this.isGuided(kind) ? kind : null;
  }

  /** Opens the page guide on a broker's tab and scrolls to it (from a connection's error). */
  protected showGuide(kind: GuideBroker) {
    this.pickedGuide.set(kind);
    this.guideToggled.set(true);
    setTimeout(() =>
      document.getElementById('connection-guide')?.scrollIntoView({ behavior: 'smooth' }),
    );
  }

  /** Field labels and hints are translated here; the server's English text is the fallback. */
  protected fieldLabel(f: CredentialField) {
    return this.translated(`connections.fields.${f.key}.label`) ?? f.label;
  }

  protected fieldHint(f: CredentialField) {
    return this.translated(`connections.fields.${f.key}.hint`) ?? f.hint;
  }

  private translated(key: string): string | null {
    const text: unknown = this.i18n.instant(key);
    return typeof text === 'string' && text !== key ? text : null;
  }

  protected problemFor(key: string) {
    return this.problems().find((p) => p.key === key) ?? null;
  }

  protected openNew(p: ProviderInfo) {
    this.editingId.set(null);
    this.name.set(p.name);
    this.fields.set({});
    this.problems.set([]);
    this.expires.set('');
    this.provider.set(p);
  }

  protected openCredentials(c: Connection) {
    const p = this.providers.value()?.find((x) => x.kind === c.kind);
    if (!p) return;
    this.editingId.set(c.id);
    this.fields.set({});
    this.problems.set([]);
    this.expires.set(c.credentialsExpireOn ?? '');
    this.provider.set(p);
  }

  protected setField(key: string, value: string) {
    this.fields.update((f) => ({ ...f, [key]: value }));
    this.problems.update((list) => list.filter((p) => p.key !== key));
  }

  protected async save() {
    const p = this.provider();
    if (!p) return;
    const problems = validateCredentials(p.kind, this.fields());
    this.problems.set(problems);
    if (problems.length) {
      document.getElementById('c-' + problems[0].key)?.focus();
      return;
    }
    this.busy.set(true);
    try {
      const body = { credentials: this.fields(), credentialsExpireOn: this.expires() || null };
      const id = this.editingId();
      if (id) {
        await firstValueFrom(this.api.updateCredentials(id, body));
        await firstValueFrom(this.api.syncConnection(id));
      } else {
        await firstValueFrom(
          this.api.createConnection({ kind: p.kind as Broker, displayName: this.name(), ...body }),
        );
      }
      this.fields.set({});
      this.provider.set(null);
      this.pendingSince = Date.now();
      this.refresh.update((v) => v + 1);
      this.toasts.show(this.i18n.instant('connections.syncStarted'));
    } catch (err) {
      this.toasts.error(err);
    } finally {
      this.busy.set(false);
    }
  }

  protected async sync(c: Connection) {
    try {
      await firstValueFrom(this.api.syncConnection(c.id));
      this.pendingSince = Date.now();
      this.refresh.update((v) => v + 1);
      this.toasts.show(this.i18n.instant('connections.syncStarted'), 'info');
    } catch (err) {
      this.toasts.error(err);
    }
  }

  protected async importCsv(c: Connection, file: File | undefined) {
    if (!file) return;
    try {
      const job = await firstValueFrom(this.api.importBrokerCsv(c.id, file));
      this.events.bump();
      this.toasts.show(
        this.i18n.instant('connections.csvDone', { imported: job.imported, updated: job.updated }),
      );
    } catch (err) {
      this.toasts.error(err);
    }
  }

  protected async showJobs(c: Connection) {
    this.jobs.set(await firstValueFrom(this.api.syncJobs(c.id)));
  }

  protected async setEnabled(c: Connection, enabled: boolean) {
    await firstValueFrom(this.api.setConnectionEnabled(c.id, enabled));
    this.refresh.update((v) => v + 1);
  }

  protected async remove(c: Connection) {
    if (
      !(await this.confirm.ask(
        this.i18n.instant('connections.confirmDelete', { name: c.displayName }),
        { destructive: true },
      ))
    )
      return;
    const purge = await this.confirm.ask(this.i18n.instant('connections.confirmPurge'), {
      destructive: true,
    });
    try {
      await firstValueFrom(this.api.deleteConnection(c.id, purge));
      this.events.bump();
    } catch (err) {
      this.toasts.error(err);
    }
  }
}
