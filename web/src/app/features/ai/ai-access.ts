import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { DayPipe } from '../../core/format';
import { Toasts } from '../../core/toast';
import { ModalComponent } from '../../shared/modal';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmTableImports } from '@spartan-ng/helm/table';
import { Confirm } from '../../core/confirm';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucidePlus } from '@ng-icons/lucide';

interface Scope {
  name: string;
  description: string;
  sensitive: boolean;
}
interface Tool {
  name: string;
  title: string;
  description: string;
  scope: string;
}
interface AiClient {
  id: string;
  name: string;
  tokenPrefix: string;
  scopes: string[];
  rateLimitPerMinute: number;
  createdAtUtc: string;
  expiresAtUtc: string | null;
  revokedAtUtc: string | null;
  lastUsedAtUtc: string | null;
  callsLast24h: number;
  deniedLast24h: number;
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
}

/** Owner-controlled AI access: every client gets its own revocable token and explicit read-only scopes. */
@Component({
  selector: 'app-ai-access',
  imports: [
    NgIcon,
    HlmTableImports,
    HlmInputImports,
    HlmButtonImports,
    TranslatePipe,
    DayPipe,
    ModalComponent,
  ],
  providers: [provideIcons({ lucidePlus })],
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

    <section class="card mb-6 !p-0 overflow-x-auto">
      <table hlmTable>
        <thead hlmTHead>
          <tr hlmTr>
            <th hlmTh>{{ 'common.name' | translate }}</th>
            <th hlmTh>{{ 'ai.scopes' | translate }}</th>
            <th hlmTh>{{ 'ai.lastUsed' | translate }}</th>
            <th hlmTh class="text-right">{{ 'ai.calls24h' | translate }}</th>
            <th hlmTh></th>
          </tr>
        </thead>
        <tbody hlmTBody>
          @for (c of clients.value() ?? []; track c.id) {
            <tr hlmTr [class.opacity-50]="!!c.revokedAtUtc">
              <td hlmTd>
                <div class="font-medium">
                  {{ c.name }}
                  @if (c.internal) {
                    <span class="badge ml-1 bg-primary/10 text-primary">{{
                      'ai.internal' | translate
                    }}</span>
                  }
                </div>
                <div class="font-mono text-xs text-muted-foreground">
                  {{ c.tokenPrefix }}_…
                  @if (c.expiresAtUtc) {
                    · {{ 'ai.expires' | translate }} {{ c.expiresAtUtc | day }}
                  }
                </div>
              </td>
              <td hlmTd class="max-w-md whitespace-normal">
                @for (s of c.scopes; track s) {
                  <span
                    class="badge mr-1 mb-1"
                    [class]="
                      isSensitive(s)
                        ? 'bg-rose-100 text-rose-700 dark:bg-rose-500/15 dark:text-rose-300'
                        : 'bg-muted'
                    "
                    >{{ s }}</span
                  >
                } @empty {
                  <span class="text-xs text-muted-foreground">{{ 'ai.noScopes' | translate }}</span>
                }
              </td>
              <td hlmTd class="text-sm text-muted-foreground">{{ c.lastUsedAtUtc | day }}</td>
              <td hlmTd class="num text-right text-sm">
                {{ c.callsLast24h }}
                @if (c.deniedLast24h) {
                  <span class="text-rose-600"> ({{ c.deniedLast24h }} denied)</span>
                }
              </td>
              <td hlmTd class="text-right whitespace-nowrap">
                @if (!c.revokedAtUtc) {
                  <button hlmBtn variant="ghost" size="sm" (click)="openEdit(c)">
                    {{ 'common.edit' | translate }}
                  </button>
                  <button
                    hlmBtn
                    variant="ghost"
                    size="sm"
                    class="text-destructive hover:text-destructive"
                    (click)="revoke(c)"
                  >
                    {{ 'ai.revoke' | translate }}
                  </button>
                } @else {
                  <span class="text-xs text-muted-foreground">{{ 'ai.revoked' | translate }}</span>
                }
              </td>
            </tr>
          } @empty {
            <tr hlmTr>
              <td hlmTd colspan="5" class="py-10 text-center text-muted-foreground">
                {{ 'ai.empty' | translate }}
              </td>
            </tr>
          }
        </tbody>
      </table>
    </section>

    <section class="card !p-0 overflow-x-auto">
      <div class="flex items-center justify-between px-5 pt-5">
        <h2 class="card-title">{{ 'ai.audit' | translate }}</h2>
        <p class="mb-4 text-xs text-muted-foreground">{{ 'ai.auditNote' | translate }}</p>
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
          @for (e of audit.value() ?? []; track e.id) {
            <tr hlmTr>
              <td hlmTd class="text-xs whitespace-nowrap text-muted-foreground">
                {{ e.atUtc | day }} {{ e.atUtc.slice(11, 19) }}
              </td>
              <td hlmTd class="text-sm">{{ e.clientName ?? '—' }}</td>
              <td hlmTd class="font-mono text-xs">{{ e.tool }}</td>
              <td hlmTd class="font-mono text-xs text-muted-foreground whitespace-normal break-all">{{ e.arguments }}</td>
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
          <legend class="label">{{ 'ai.scopes' | translate }}</legend>
          @for (s of normalScopes(); track s.name) {
            <label class="flex items-start gap-2 py-1 text-sm">
              <input
                type="checkbox"
                class="mt-1"
                [checked]="selected().has(s.name)"
                (change)="toggle(s.name)"
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
          @for (s of sensitiveScopes(); track s.name) {
            <label class="flex items-start gap-2 py-1 text-sm">
              <input
                type="checkbox"
                class="mt-1"
                [checked]="selected().has(s.name)"
                (change)="toggle(s.name)"
              />
              <span
                ><span class="font-mono text-xs">{{ s.name }}</span
                ><br /><span class="text-xs text-muted-foreground">{{ s.description }}</span></span
              >
            </label>
          }
        </fieldset>
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

  protected readonly catalog = rxResource({
    stream: () => this.http.get<{ scopes: Scope[]; tools: Tool[] }>('/api/ai-admin/catalog'),
  });
  protected readonly clients = rxResource({
    params: () => this.refresh(),
    stream: () => this.http.get<AiClient[]>('/api/ai-admin/clients'),
  });
  protected readonly audit = rxResource({
    params: () => this.refresh(),
    stream: () => this.http.get<AuditEvent[]>('/api/ai-admin/audit', { params: { limit: 100 } }),
  });

  protected readonly normalScopes = computed(() =>
    (this.catalog.value()?.scopes ?? []).filter((s) => !s.sensitive),
  );
  protected readonly sensitiveScopes = computed(() =>
    (this.catalog.value()?.scopes ?? []).filter((s) => s.sensitive),
  );

  protected readonly formOpen = signal(false);
  protected readonly editing = signal<AiClient | null>(null);
  protected readonly name = signal('');
  protected readonly selected = signal(new Set<string>());
  protected readonly expiresInDays = signal(180);
  protected readonly rateLimit = signal(60);
  protected readonly newToken = signal<string | null>(null);
  protected readonly claudeCommand = computed(
    () =>
      `claude mcp add --transport http personal-finance ${location.origin}/mcp --header "Authorization: Bearer ${this.newToken()}"`,
  );

  protected isSensitive = (scope: string) => this.sensitiveScopes().some((s) => s.name === scope);
  protected toolsFor = (scope: string) =>
    (this.catalog.value()?.tools ?? [])
      .filter((t) => t.scope === scope)
      .map((t) => t.name)
      .join(', ') || '—';

  protected openNew() {
    this.editing.set(null);
    this.name.set('');
    // Sensible starting point: summaries only. Nothing sensitive is pre-selected.
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
    this.formOpen.set(true);
  }

  protected openEdit(c: AiClient) {
    this.editing.set(c);
    this.selected.set(new Set(c.scopes));
    this.rateLimit.set(c.rateLimitPerMinute);
    this.formOpen.set(true);
  }

  protected toggle(scope: string) {
    this.selected.update((s) => {
      const next = new Set(s);
      if (next.has(scope)) next.delete(scope);
      else next.add(scope);
      return next;
    });
  }

  protected async save() {
    const scopes = [...this.selected()];
    try {
      const editing = this.editing();
      if (editing) {
        await firstValueFrom(
          this.http.put(`/api/ai-admin/clients/${editing.id}`, {
            scopes,
            rateLimitPerMinute: this.rateLimit(),
          }),
        );
      } else {
        const res = await firstValueFrom(
          this.http.post<{ id: string; token: string }>('/api/ai-admin/clients', {
            name: this.name(),
            scopes,
            expiresInDays: this.expiresInDays(),
            rateLimitPerMinute: this.rateLimit(),
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

  protected async copy(text: string) {
    try {
      await navigator.clipboard.writeText(text);
      this.toasts.show(this.i18n.instant('ai.copied'), 'info');
    } catch {
      this.toasts.show(this.i18n.instant('ai.copyFailed'), 'error');
    }
  }
}
