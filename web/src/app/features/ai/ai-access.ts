import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { liveResource } from '../../core/resource';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { DateTimePipe, DayPipe } from '../../core/format';
import { Toasts } from '../../core/toast';
import { ModalComponent } from '../../shared/modal';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { Confirm } from '../../core/confirm';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideBan,
  lucideBot,
  lucideHistory,
  lucideMessageSquare,
  lucidePencil,
  lucidePlus,
  lucideRotateCcw,
  lucideTrash2,
} from '@ng-icons/lucide';
import {
  allSelected,
  daysLeft,
  groupScopes,
  isWriteScope,
  newlyGrantedWrites,
  restorableItemId,
  Scope,
  toggleGroup,
  toggleScope,
} from './scope-selection';

interface Tool {
  name: string;
  title: string;
  description: string;
  scope: string;
  write?: boolean;
}
interface AiClient {
  id: string;
  name: string;
  tokenPrefix: string;
  scopes: string[];
  rateLimitPerMinute: number;
  writesPerHour: number;
  createdAtUtc: string;
  expiresAtUtc: string | null;
  revokedAtUtc: string | null;
  lastUsedAtUtc: string | null;
  callsLast24h: number;
  deniedLast24h: number;
  writesLast24h: number;
  internal: boolean;
}
interface AuditEvent {
  id: number;
  clientName: string | null;
  tool: string;
  scope: string | null;
  decision: string;
  arguments: string;
  recordCount: number;
  responseBytes: number;
  durationMs: number;
  atUtc: string;
  reason: string | null;
  write: boolean;
  recordId: string | null;
  changes: string | null;
}
type Tab = 'clients' | 'recycle' | 'activity';
type AuditFilter = 'all' | 'writes' | 'denied';

interface RecycledItem {
  id: string;
  kind: string;
  recordId: string;
  clientName: string;
  summary: string;
  deletedAtUtc: string;
  purgeAfterUtc: string;
}

/**
 * Owner-controlled AI access: every client gets its own revocable token and explicit scopes. Reading is the
 * default; write scopes are opt-in, flagged and confirmed, and AI deletions land in a 30-day recycle bin.
 */
@Component({
  selector: 'app-ai-access',
  imports: [
    NgIcon,
    HlmTableImports,
    HlmInputImports,
    HlmButtonImports,
    TranslatePipe,
    DayPipe,
    DateTimePipe,
    ModalComponent,
  ],
  providers: [
    provideIcons({
      lucideBan,
      lucideBot,
      lucideHistory,
      lucideMessageSquare,
      lucidePencil,
      lucidePlus,
      lucideRotateCcw,
      lucideTrash2,
    }),
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mb-6 flex flex-wrap items-center justify-between gap-3">
      <div>
        <h1 class="text-2xl font-semibold tracking-tight">{{ 'nav.ai' | translate }}</h1>
        <p class="text-sm text-muted-foreground">{{ 'ai.subtitle' | translate }}</p>
      </div>
      <button hlmBtn (click)="openNew()">
        <ng-icon name="lucidePlus" />{{ 'ai.newClient' | translate }}
      </button>
    </div>

    <!-- At a glance: what the AI clients did in the last 24 hours. -->
    <div class="mb-6 grid grid-cols-2 gap-3 lg:grid-cols-4">
      @for (k of kpis(); track k.label) {
        <div class="card !p-4">
          <div class="text-xs font-medium text-muted-foreground">{{ k.label | translate }}</div>
          <div class="num mt-1 text-2xl font-semibold" [class]="k.tone">{{ k.value }}</div>
        </div>
      }
    </div>

    <div class="-mx-4 mb-4 overflow-x-auto px-4 sm:mx-0 sm:px-0">
      <div class="segmented" role="tablist">
        @for (t of tabs; track t) {
          <button
            type="button"
            role="tab"
            [attr.aria-selected]="tab() === t"
            [class.active]="tab() === t"
            (click)="tab.set(t)"
          >
            <ng-icon [name]="tabIcon[t]" aria-hidden="true" class="mr-1.5" />{{
              'ai.tab.' + t | translate
            }}
            @if (tabCount(t); as n) {
              <span
                class="ml-1.5 rounded-full bg-muted-foreground/15 px-1.5 text-[11px] leading-4"
                >{{ n }}</span
              >
            }
          </button>
        }
      </div>
    </div>

    @switch (tab()) {
      @case ('clients') {
        <div class="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-3">
          @for (c of clients.value() ?? []; track c.id) {
            <section
              class="card card-hover flex flex-col !p-0"
              [class.opacity-60]="!!c.revokedAtUtc"
            >
              <div class="flex items-start gap-3 p-5 pb-3">
                <div
                  class="flex size-10 shrink-0 items-center justify-center rounded-lg"
                  [class]="
                    canWrite(c)
                      ? 'bg-rose-100 text-rose-700 dark:bg-rose-500/15 dark:text-rose-300'
                      : 'bg-primary/10 text-primary'
                  "
                >
                  <ng-icon
                    [name]="c.internal ? 'lucideMessageSquare' : 'lucideBot'"
                    class="text-lg"
                    aria-hidden="true"
                  />
                </div>
                <div class="min-w-0 flex-1">
                  <h2 class="truncate font-semibold">{{ c.name }}</h2>
                  <div class="font-mono text-xs text-muted-foreground">
                    {{ c.tokenPrefix }}_…
                    @if (c.expiresAtUtc) {
                      · {{ 'ai.expires' | translate }} {{ c.expiresAtUtc | day }}
                    }
                  </div>
                </div>
                <div class="flex shrink-0 flex-col items-end gap-1">
                  @if (c.revokedAtUtc) {
                    <span class="badge bg-muted">{{ 'ai.revoked' | translate }}</span>
                  } @else {
                    <span
                      class="badge bg-emerald-100 text-emerald-700 dark:bg-emerald-500/15 dark:text-emerald-300"
                      >{{ 'ai.active' | translate }}</span
                    >
                  }
                  @if (c.internal) {
                    <span class="badge bg-primary/10 text-primary">{{
                      'ai.internal' | translate
                    }}</span>
                  }
                  @if (canWrite(c)) {
                    <span
                      class="badge bg-rose-100 text-rose-700 dark:bg-rose-500/15 dark:text-rose-300"
                      >{{ 'ai.canWrite' | translate }}</span
                    >
                  }
                </div>
              </div>

              <div class="flex-1 px-5 pb-4">
                <div class="label">{{ 'ai.scopes' | translate }}</div>
                <div class="flex flex-wrap gap-1">
                  @for (s of c.scopes; track s) {
                    <span
                      class="badge"
                      [class]="
                        isSensitive(s)
                          ? 'bg-rose-100 text-rose-700 dark:bg-rose-500/15 dark:text-rose-300'
                          : 'bg-muted'
                      "
                      [class.font-semibold]="isWrite(s)"
                      >{{ s }}</span
                    >
                  } @empty {
                    <span class="text-xs text-muted-foreground">{{
                      'ai.noScopes' | translate
                    }}</span>
                  }
                </div>
              </div>

              <dl class="grid grid-cols-3 border-t text-center">
                <div class="px-2 py-3">
                  <dt class="text-[11px] text-muted-foreground">{{ 'ai.calls24h' | translate }}</dt>
                  <dd class="num font-medium">{{ c.callsLast24h }}</dd>
                </div>
                <div class="border-x px-2 py-3">
                  <dt class="text-[11px] text-muted-foreground">
                    {{ 'ai.denied24h' | translate }}
                  </dt>
                  <dd class="num font-medium" [class.tone-neg]="c.deniedLast24h > 0">
                    {{ c.deniedLast24h }}
                  </dd>
                </div>
                <div class="px-2 py-3">
                  <dt class="text-[11px] text-muted-foreground">
                    {{ 'ai.writesShort' | translate }}
                  </dt>
                  <dd class="num font-medium" [class.tone-neg]="c.writesLast24h > 0">
                    {{ c.writesLast24h }}
                  </dd>
                </div>
              </dl>

              <div class="flex flex-wrap items-center justify-between gap-2 border-t px-3 py-2">
                <span class="px-2 text-xs text-muted-foreground"
                  >{{ 'ai.lastUsed' | translate }}:
                  {{ c.lastUsedAtUtc ? (c.lastUsedAtUtc | dateTime) : '—' }}</span
                >
                <div class="flex">
                  @if (!c.revokedAtUtc) {
                    <button hlmBtn variant="ghost" size="sm" (click)="openEdit(c)">
                      <ng-icon name="lucidePencil" aria-hidden="true" />{{
                        'common.edit' | translate
                      }}
                    </button>
                    <button
                      hlmBtn
                      variant="ghost"
                      size="sm"
                      class="text-destructive hover:text-destructive"
                      (click)="revoke(c)"
                    >
                      <ng-icon name="lucideBan" aria-hidden="true" />{{ 'ai.revoke' | translate }}
                    </button>
                  }
                  @if (!c.internal) {
                    <button
                      hlmBtn
                      variant="ghost"
                      size="sm"
                      class="text-destructive hover:text-destructive"
                      [attr.aria-label]="('common.delete' | translate) + ' ' + c.name"
                      (click)="remove(c)"
                    >
                      <ng-icon name="lucideTrash2" aria-hidden="true" />{{
                        'common.delete' | translate
                      }}
                    </button>
                  }
                </div>
              </div>
            </section>
          } @empty {
            <div class="card col-span-full py-10 text-center text-muted-foreground">
              <ng-icon name="lucideBot" class="mb-2 text-3xl text-primary" aria-hidden="true" />
              <p>{{ 'ai.empty' | translate }}</p>
            </div>
          }
        </div>
      }
      @case ('recycle') {
        <section class="card !p-0 overflow-x-auto" aria-labelledby="ai-recycle-title">
          <div class="px-5 pt-5">
            <h2 id="ai-recycle-title" class="sr-only">{{ 'ai.recycleBin' | translate }}</h2>
            <p class="mb-4 text-xs text-muted-foreground">{{ 'ai.recycleNote' | translate }}</p>
          </div>
          <table hlmTable>
            <thead hlmTHead>
              <tr hlmTr>
                <th hlmTh>{{ 'ai.deletedAt' | translate }}</th>
                <th hlmTh>{{ 'ai.client' | translate }}</th>
                <th hlmTh>{{ 'ai.item' | translate }}</th>
                <th hlmTh class="text-right">{{ 'ai.purgeIn' | translate }}</th>
                <th hlmTh></th>
              </tr>
            </thead>
            <tbody hlmTBody>
              @for (b of bin.value() ?? []; track b.id) {
                <tr hlmTr>
                  <td hlmTd class="text-xs whitespace-nowrap text-muted-foreground">
                    {{ b.deletedAtUtc | dateTime }}
                  </td>
                  <td hlmTd class="text-sm">{{ b.clientName }}</td>
                  <td hlmTd class="text-sm whitespace-normal">{{ b.summary }}</td>
                  <td hlmTd class="num text-right text-sm">
                    {{ 'ai.days' | translate: { count: daysLeft(b.purgeAfterUtc) } }}
                  </td>
                  <td hlmTd class="text-right">
                    <button
                      hlmBtn
                      variant="outline"
                      size="sm"
                      [attr.aria-label]="('ai.restore' | translate) + ': ' + b.summary"
                      (click)="restore(b.id)"
                    >
                      <ng-icon name="lucideRotateCcw" aria-hidden="true" />{{
                        'ai.restore' | translate
                      }}
                    </button>
                  </td>
                </tr>
              } @empty {
                <tr hlmTr>
                  <td hlmTd colspan="5" class="py-6 text-center text-sm text-muted-foreground">
                    {{ 'ai.recycleEmpty' | translate }}
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </section>
      }
      @case ('activity') {
        <section class="card !p-0 overflow-x-auto">
          <div class="flex flex-wrap items-center justify-between gap-3 px-5 pt-5 pb-4">
            <div
              class="flex flex-wrap gap-2"
              role="group"
              [attr.aria-label]="'ai.audit' | translate"
            >
              @for (f of auditFilters; track f) {
                <button
                  type="button"
                  class="chip"
                  [class.chip-active]="auditFilter() === f"
                  (click)="auditFilter.set(f)"
                >
                  {{ 'ai.filter.' + f | translate }}
                </button>
              }
            </div>
            <p class="text-xs text-muted-foreground">{{ 'ai.auditNote' | translate }}</p>
          </div>
          <table hlmTable>
            <thead hlmTHead>
              <tr hlmTr>
                <th hlmTh>{{ 'tx.date' | translate }}</th>
                <th hlmTh>{{ 'ai.client' | translate }}</th>
                <th hlmTh>{{ 'ai.tool' | translate }}</th>
                <th hlmTh>{{ 'ai.arguments' | translate }}</th>
                <th hlmTh>{{ 'ai.decision' | translate }}</th>
                <th hlmTh class="text-right">{{ 'ai.records' | translate }}</th>
              </tr>
            </thead>
            <tbody hlmTBody>
              @for (e of auditShown(); track e.id) {
                <tr hlmTr [class.font-medium]="e.write">
                  <td hlmTd class="text-xs whitespace-nowrap text-muted-foreground">
                    {{ e.atUtc | day }} {{ e.atUtc.slice(11, 19) }}
                  </td>
                  <td hlmTd class="text-sm">{{ e.clientName ?? '—' }}</td>
                  <td hlmTd class="font-mono text-xs">
                    @if (e.write) {
                      <span
                        class="badge mr-1 bg-rose-100 text-rose-700 dark:bg-rose-500/15 dark:text-rose-300"
                        >{{ 'ai.writeBadge' | translate }}</span
                      >
                    }
                    {{ e.tool }}
                  </td>
                  <td
                    hlmTd
                    class="font-mono text-xs text-muted-foreground whitespace-normal break-all"
                  >
                    {{ e.arguments }}
                    @if (e.changes) {
                      <div class="mt-1 text-[11px]" [title]="e.changes">
                        {{ 'ai.changes' | translate }}: {{ shorten(e.changes) }}
                      </div>
                    }
                    @if (restorable(e); as itemId) {
                      <button hlmBtn variant="link" size="sm" (click)="restore(itemId)">
                        <ng-icon name="lucideRotateCcw" aria-hidden="true" />{{
                          'ai.restore' | translate
                        }}
                      </button>
                    }
                  </td>
                  <td hlmTd>
                    <span
                      class="badge"
                      [class]="
                        e.decision === 'Allowed'
                          ? 'bg-emerald-100 text-emerald-700 dark:bg-emerald-500/15 dark:text-emerald-300'
                          : 'bg-rose-100 text-rose-700 dark:bg-rose-500/15 dark:text-rose-300'
                      "
                      [title]="e.reason ?? ''"
                      >{{ e.decision }}</span
                    >
                  </td>
                  <td hlmTd class="num text-right text-sm">{{ e.recordCount }}</td>
                </tr>
              } @empty {
                <tr hlmTr>
                  <td hlmTd colspan="6" class="py-8 text-center text-muted-foreground">
                    {{ 'ai.noAudit' | translate }}
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </section>
      }
    }

    <app-modal
      [open]="formOpen()"
      [title]="(editing() ? 'ai.editClient' : 'ai.newClient') | translate"
      width="40rem"
      (closed)="formOpen.set(false)"
    >
      <form class="space-y-4" (submit)="$event.preventDefault(); save()">
        @if (!editing()) {
          <div>
            <label class="label" for="ai-name">{{ 'common.name' | translate }}</label>
            <input
              id="ai-name"
              hlmInput
              required
              maxlength="60"
              placeholder="Claude Code"
              [value]="name()"
              (input)="name.set($any($event.target).value)"
            />
          </div>
        }
        <fieldset>
          <legend class="label flex w-full flex-wrap items-center justify-between gap-2">
            <span>{{ 'ai.readGroup' | translate }}</span>
            <button
              type="button"
              hlmBtn
              variant="outline"
              size="sm"
              [attr.aria-pressed]="groupSelected(groups().read)"
              (click)="selectGroup(groups().read)"
            >
              {{ 'ai.selectAllRead' | translate }}
            </button>
          </legend>
          @for (s of groups().read; track s.name) {
            <label class="flex items-start gap-2 py-1 text-sm">
              <input
                type="checkbox"
                class="mt-1"
                [checked]="selected().has(s.name)"
                (change)="toggle(s.name, $event)"
              />
              <span
                ><span class="font-mono text-xs">{{ s.name }}</span
                ><br /><span class="text-xs text-muted-foreground">{{ s.description }}</span>
                <span class="block text-[11px] text-muted-foreground"
                  >{{ 'ai.tools' | translate }}: {{ toolsFor(s.name) }}</span
                ></span
              >
            </label>
          }
        </fieldset>
        <fieldset class="rounded-xl border border-rose-200 p-3 dark:border-rose-500/30">
          <legend class="px-1 text-xs font-semibold text-rose-700 dark:text-rose-300">
            {{ 'ai.sensitive' | translate }}
          </legend>
          @for (s of groups().sensitiveRead; track s.name) {
            <label class="flex items-start gap-2 py-1 text-sm">
              <input
                type="checkbox"
                class="mt-1"
                [checked]="selected().has(s.name)"
                (change)="toggle(s.name, $event)"
              />
              <span
                ><span class="font-mono text-xs">{{ s.name }}</span
                ><br /><span class="text-xs text-muted-foreground">{{ s.description }}</span></span
              >
            </label>
          }
        </fieldset>
        @if (groups().write.length) {
          <fieldset class="rounded-xl border border-rose-200 p-3 dark:border-rose-500/30">
            <legend
              class="flex w-full flex-wrap items-center justify-between gap-2 px-1 text-sm font-semibold text-rose-700 dark:text-rose-300"
            >
              <span>{{ 'ai.writeGroup' | translate }}</span>
              <button
                type="button"
                hlmBtn
                variant="outline"
                size="sm"
                class="text-rose-700 dark:text-rose-300"
                [attr.aria-pressed]="groupSelected(groups().write)"
                (click)="selectGroup(groups().write)"
              >
                {{ 'ai.selectAllWrite' | translate }}
              </button>
            </legend>
            <p class="mb-2 text-xs text-rose-700 dark:text-rose-300">
              {{ 'ai.writeNote' | translate }}
            </p>
            @for (s of groups().write; track s.name) {
              <label class="flex items-start gap-2 py-1 text-sm">
                <input
                  type="checkbox"
                  class="mt-1"
                  [checked]="selected().has(s.name)"
                  (change)="toggle(s.name, $event)"
                />
                <span
                  ><span
                    class="badge mr-1 bg-rose-100 text-rose-700 dark:bg-rose-500/15 dark:text-rose-300"
                    >{{ 'ai.writeBadge' | translate }}</span
                  ><span class="font-mono text-xs">{{ s.name }}</span
                  ><br /><span class="text-xs text-muted-foreground">{{ s.description }}</span>
                  <span class="block text-[11px] text-muted-foreground"
                    >{{ 'ai.tools' | translate }}: {{ toolsFor(s.name) }}</span
                  ></span
                >
              </label>
            }
          </fieldset>
        }
        <div class="form-grid">
          @if (!editing()) {
            <div>
              <label class="label" for="ai-exp">{{ 'ai.expiresInDays' | translate }}</label>
              <input
                id="ai-exp"
                hlmInput
                class="num"
                type="number"
                min="1"
                max="730"
                [value]="expiresInDays()"
                (input)="expiresInDays.set(+$any($event.target).value)"
              />
            </div>
          }
          <div>
            <label class="label" for="ai-rate">{{ 'ai.rateLimit' | translate }}</label>
            <input
              id="ai-rate"
              hlmInput
              class="num"
              type="number"
              min="1"
              max="600"
              [value]="rateLimit()"
              (input)="rateLimit.set(+$any($event.target).value)"
            />
          </div>
          @if (hasWrite()) {
            <div>
              <label class="label" for="ai-writes">{{ 'ai.writesPerHour' | translate }}</label>
              <input
                id="ai-writes"
                hlmInput
                class="num"
                type="number"
                min="1"
                max="200"
                [value]="writesPerHour()"
                (input)="writesPerHour.set(+$any($event.target).value)"
              />
            </div>
          }
        </div>
        <div class="flex justify-end gap-2">
          <button type="button" hlmBtn variant="outline" (click)="formOpen.set(false)">
            {{ 'common.cancel' | translate }}
          </button>
          <button hlmBtn>{{ 'common.save' | translate }}</button>
        </div>
      </form>
    </app-modal>

    <app-modal
      [open]="!!newToken()"
      [title]="'ai.tokenTitle' | translate"
      width="44rem"
      (closed)="newToken.set(null)"
    >
      <div class="space-y-3">
        <p class="text-sm text-amber-700 dark:text-amber-300">{{ 'ai.tokenOnce' | translate }}</p>
        <pre class="overflow-x-auto rounded-lg bg-muted p-3 font-mono text-xs">{{
          newToken()
        }}</pre>
        <p class="text-sm text-muted-foreground">{{ 'ai.claudeCode' | translate }}</p>
        <pre class="overflow-x-auto rounded-lg bg-muted p-3 font-mono text-xs">{{
          claudeCommand()
        }}</pre>
        <div class="flex justify-end gap-2">
          <button hlmBtn variant="outline" (click)="copy(claudeCommand())">
            {{ 'ai.copyCommand' | translate }}
          </button>
          <button hlmBtn (click)="copy(newToken() ?? '')">{{ 'ai.copyToken' | translate }}</button>
        </div>
      </div>
    </app-modal>
  `,
})
export class AiAccessComponent {
  private readonly http = inject(HttpClient);
  private readonly toasts = inject(Toasts);
  private readonly i18n = inject(TranslateService);
  private readonly confirm = inject(Confirm);
  private readonly refresh = signal(0);

  protected readonly catalog = liveResource({
    stream: () => this.http.get<{ scopes: Scope[]; tools: Tool[] }>('/api/ai-admin/catalog'),
  });
  protected readonly clients = liveResource({
    params: () => this.refresh(),
    stream: () => this.http.get<AiClient[]>('/api/ai-admin/clients'),
  });
  protected readonly audit = liveResource({
    params: () => this.refresh(),
    stream: () => this.http.get<AuditEvent[]>('/api/ai-admin/audit', { params: { limit: 100 } }),
  });
  protected readonly bin = liveResource({
    params: () => this.refresh(),
    stream: () => this.http.get<RecycledItem[]>('/api/ai-admin/recycle-bin'),
  });

  protected readonly tabs: Tab[] = ['clients', 'recycle', 'activity'];
  protected readonly tabIcon: Record<Tab, string> = {
    clients: 'lucideBot',
    recycle: 'lucideTrash2',
    activity: 'lucideHistory',
  };
  protected readonly tab = signal<Tab>('clients');
  protected readonly auditFilters: AuditFilter[] = ['all', 'writes', 'denied'];
  protected readonly auditFilter = signal<AuditFilter>('all');
  protected readonly auditShown = computed(() => {
    const events = this.audit.value() ?? [];
    switch (this.auditFilter()) {
      case 'writes':
        return events.filter((e) => e.write);
      case 'denied':
        return events.filter((e) => e.decision !== 'Allowed');
      default:
        return events;
    }
  });
  protected readonly kpis = computed(() => {
    const clients = this.clients.value() ?? [];
    const sum = (f: (c: AiClient) => number) => clients.reduce((n, c) => n + f(c), 0);
    const denied = sum((c) => c.deniedLast24h);
    const writes = sum((c) => c.writesLast24h);
    return [
      { label: 'ai.activeClients', value: clients.filter((c) => !c.revokedAtUtc).length, tone: '' },
      { label: 'ai.calls24h', value: sum((c) => c.callsLast24h), tone: '' },
      { label: 'ai.denied24h', value: denied, tone: denied ? 'tone-neg' : '' },
      { label: 'ai.writes24hTotal', value: writes, tone: writes ? 'tone-neg' : '' },
    ];
  });
  protected tabCount(t: Tab): number {
    if (t === 'clients') return (this.clients.value() ?? []).filter((c) => !c.revokedAtUtc).length;
    if (t === 'recycle') return (this.bin.value() ?? []).length;
    return 0;
  }

  private readonly scopes = computed(() => this.catalog.value()?.scopes ?? []);
  protected readonly groups = computed(() => groupScopes(this.scopes()));

  protected readonly formOpen = signal(false);
  protected readonly editing = signal<AiClient | null>(null);
  protected readonly name = signal('');
  protected readonly selected = signal(new Set<string>());
  protected readonly expiresInDays = signal(180);
  protected readonly rateLimit = signal(60);
  protected readonly writesPerHour = signal(20);
  protected readonly newToken = signal<string | null>(null);
  protected readonly hasWrite = computed(() =>
    [...this.selected()].some((s) => isWriteScope(this.scopes(), s)),
  );
  protected readonly claudeCommand = computed(
    () =>
      `claude mcp add --transport http personal-finance ${location.origin}/mcp --header "Authorization: Bearer ${this.newToken()}"`,
  );

  protected readonly daysLeft = daysLeft;
  protected isSensitive = (scope: string) =>
    this.scopes().some((s) => s.name === scope && s.sensitive);
  protected isWrite = (scope: string) => isWriteScope(this.scopes(), scope);
  protected canWrite = (c: AiClient) => !c.revokedAtUtc && c.scopes.some((s) => this.isWrite(s));
  protected groupSelected = (group: Scope[]) => allSelected(this.selected(), group);
  protected restorable = (e: AuditEvent) => restorableItemId(e, this.bin.value() ?? []);
  protected shorten = (text: string) => (text.length > 160 ? text.slice(0, 157) + '…' : text);
  protected toolsFor = (scope: string) =>
    (this.catalog.value()?.tools ?? [])
      .filter((t) => t.scope === scope)
      .map((t) => t.name)
      .join(', ') || '—';

  protected openNew() {
    this.editing.set(null);
    this.name.set('');
    // Sensible starting point: summaries only. Nothing sensitive — and nothing that writes — is pre-selected.
    this.selected.set(
      new Set([
        'overview.read',
        'expenses.summary.read',
        'income.summary.read',
        'budget.read',
        'goals.read',
      ]),
    );
    this.rateLimit.set(60);
    this.writesPerHour.set(20);
    this.formOpen.set(true);
  }

  protected openEdit(c: AiClient) {
    this.editing.set(c);
    this.selected.set(new Set(c.scopes));
    this.rateLimit.set(c.rateLimitPerMinute);
    this.writesPerHour.set(c.writesPerHour ?? 20);
    this.formOpen.set(true);
  }

  /** Granting a write scope is a deliberate act: the owner confirms it before the box is ticked. */
  private async apply(next: Set<string>): Promise<boolean> {
    const granted = newlyGrantedWrites(this.selected(), next, this.scopes());
    if (
      granted.length &&
      !(await this.confirm.ask(
        this.i18n.instant('ai.confirmWrite', { scopes: granted.join(', ') }),
        {
          destructive: true,
          confirmLabel: this.i18n.instant('ai.allowWrite'),
        },
      ))
    ) {
      return false;
    }
    this.selected.set(next);
    return true;
  }

  protected async toggle(scope: string, event: Event) {
    const box = event.target as HTMLInputElement;
    if (!(await this.apply(toggleScope(this.selected(), scope)))) box.checked = false;
  }

  protected async selectGroup(group: Scope[]) {
    await this.apply(toggleGroup(this.selected(), group));
  }

  protected async save() {
    const scopes = [...this.selected()];
    const writesPerHour = this.hasWrite() ? this.writesPerHour() : undefined;
    try {
      const editing = this.editing();
      if (editing) {
        await firstValueFrom(
          this.http.put(`/api/ai-admin/clients/${editing.id}`, {
            scopes,
            rateLimitPerMinute: this.rateLimit(),
            writesPerHour,
          }),
        );
      } else {
        const res = await firstValueFrom(
          this.http.post<{ id: string; token: string }>('/api/ai-admin/clients', {
            name: this.name(),
            scopes,
            expiresInDays: this.expiresInDays(),
            rateLimitPerMinute: this.rateLimit(),
            writesPerHour,
          }),
        );
        this.newToken.set(res.token);
      }
      this.formOpen.set(false);
      this.refresh.update((v) => v + 1);
    } catch (err) {
      this.toasts.error(err);
    }
  }

  protected async restore(itemId: string) {
    try {
      await firstValueFrom(this.http.post(`/api/ai-admin/recycle-bin/${itemId}/restore`, {}));
      this.toasts.show(this.i18n.instant('ai.restored'), 'info');
    } catch (err) {
      this.toasts.error(err);
    }
    this.refresh.update((v) => v + 1);
  }

  protected async revoke(c: AiClient) {
    if (
      !(await this.confirm.ask(this.i18n.instant('ai.confirmRevoke', { name: c.name }), {
        destructive: true,
      }))
    )
      return;
    await firstValueFrom(this.http.post(`/api/ai-admin/clients/${c.id}/revoke`, {}));
    this.refresh.update((v) => v + 1);
  }

  protected async remove(c: AiClient) {
    if (
      !(await this.confirm.ask(this.i18n.instant('ai.confirmDelete', { name: c.name }), {
        destructive: true,
      }))
    )
      return;
    try {
      await firstValueFrom(this.http.delete(`/api/ai-admin/clients/${c.id}`));
      this.toasts.show(this.i18n.instant('ai.deleted', { name: c.name }), 'info');
    } catch (err) {
      this.toasts.error(err);
    }
    this.refresh.update((v) => v + 1);
  }

  protected async copy(text: string) {
    try {
      await navigator.clipboard.writeText(text);
      this.toasts.show(this.i18n.instant('ai.copied'), 'info');
    } catch {
      this.toasts.show(this.i18n.instant('ai.copyFailed'), 'error');
    }
  }
}
