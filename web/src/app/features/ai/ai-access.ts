import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { DayPipe } from '../../core/format';
import { Toasts } from '../../core/toast';
import { ModalComponent } from '../../shared/modal';

interface Scope { name: string; description: string; sensitive: boolean }
interface Tool { name: string; title: string; description: string; scope: string }
interface AiClient {
  id: string; name: string; tokenPrefix: string; scopes: string[]; rateLimitPerMinute: number;
  createdAtUtc: string; expiresAtUtc: string | null; revokedAtUtc: string | null; lastUsedAtUtc: string | null;
  callsLast24h: number; deniedLast24h: number;
}
interface AuditEvent {
  id: number; clientName: string | null; tool: string; scope: string | null; decision: string; arguments: string;
  recordCount: number; responseBytes: number; durationMs: number; atUtc: string; reason: string | null;
}

/** Owner-controlled AI access: every client gets its own revocable token and explicit read-only scopes. */
@Component({
  selector: 'app-ai-access',
  imports: [TranslatePipe, DayPipe, ModalComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mb-6 flex flex-wrap items-center justify-between gap-3">
      <div>
        <h1 class="text-2xl font-semibold tracking-tight">{{ 'nav.ai' | translate }}</h1>
        <p class="text-sm text-slate-500">{{ 'ai.subtitle' | translate }}</p>
      </div>
      <button class="btn btn-primary" (click)="openNew()">＋ {{ 'ai.newClient' | translate }}</button>
    </div>

    <section class="card mb-6 !p-0 overflow-x-auto">
      <table class="table">
        <thead><tr><th>{{ 'common.name' | translate }}</th><th>{{ 'ai.scopes' | translate }}</th><th>{{ 'ai.lastUsed' | translate }}</th><th class="text-right">{{ 'ai.calls24h' | translate }}</th><th></th></tr></thead>
        <tbody>
          @for (c of clients.value() ?? []; track c.id) {
            <tr [class.opacity-50]="!!c.revokedAtUtc">
              <td>
                <div class="font-medium">{{ c.name }}</div>
                <div class="font-mono text-xs text-slate-400">{{ c.tokenPrefix }}_…@if (c.expiresAtUtc) { · {{ 'ai.expires' | translate }} {{ c.expiresAtUtc | day }} }</div>
              </td>
              <td class="max-w-md">
                @for (s of c.scopes; track s) {
                  <span class="badge mr-1 mb-1" [class]="isSensitive(s) ? 'bg-rose-100 text-rose-700 dark:bg-rose-500/15 dark:text-rose-300' : 'bg-slate-100 dark:bg-slate-800'">{{ s }}</span>
                } @empty { <span class="text-xs text-slate-400">{{ 'ai.noScopes' | translate }}</span> }
              </td>
              <td class="text-sm text-slate-500">{{ c.lastUsedAtUtc | day }}</td>
              <td class="num text-right text-sm">{{ c.callsLast24h }}@if (c.deniedLast24h) { <span class="text-rose-600"> ({{ c.deniedLast24h }} ✕)</span> }</td>
              <td class="text-right whitespace-nowrap">
                @if (!c.revokedAtUtc) {
                  <button class="btn btn-ghost !px-2 !py-1 text-xs" (click)="openEdit(c)">{{ 'common.edit' | translate }}</button>
                  <button class="btn btn-ghost !px-2 !py-1 text-xs text-rose-600" (click)="revoke(c)">{{ 'ai.revoke' | translate }}</button>
                } @else {
                  <span class="text-xs text-slate-400">{{ 'ai.revoked' | translate }}</span>
                }
              </td>
            </tr>
          } @empty {
            <tr><td colspan="5" class="py-10 text-center text-slate-400">{{ 'ai.empty' | translate }}</td></tr>
          }
        </tbody>
      </table>
    </section>

    <section class="card !p-0 overflow-x-auto">
      <div class="flex items-center justify-between px-5 pt-5">
        <h2 class="card-title">{{ 'ai.audit' | translate }}</h2>
        <p class="mb-4 text-xs text-slate-400">{{ 'ai.auditNote' | translate }}</p>
      </div>
      <table class="table">
        <thead><tr><th>{{ 'tx.date' | translate }}</th><th>{{ 'ai.client' | translate }}</th><th>{{ 'ai.tool' | translate }}</th><th>{{ 'ai.arguments' | translate }}</th><th>{{ 'ai.decision' | translate }}</th><th class="text-right">{{ 'ai.records' | translate }}</th></tr></thead>
        <tbody>
          @for (e of audit.value() ?? []; track e.id) {
            <tr>
              <td class="text-xs whitespace-nowrap text-slate-500">{{ e.atUtc | day }} {{ e.atUtc.slice(11, 19) }}</td>
              <td class="text-sm">{{ e.clientName ?? '—' }}</td>
              <td class="font-mono text-xs">{{ e.tool }}</td>
              <td class="font-mono text-xs text-slate-500">{{ e.arguments }}</td>
              <td>
                <span class="badge" [class]="e.decision === 'Allowed' ? 'bg-emerald-100 text-emerald-700 dark:bg-emerald-500/15 dark:text-emerald-300' : 'bg-rose-100 text-rose-700 dark:bg-rose-500/15 dark:text-rose-300'" [title]="e.reason ?? ''">{{ e.decision }}</span>
              </td>
              <td class="num text-right text-sm">{{ e.recordCount }}</td>
            </tr>
          } @empty {
            <tr><td colspan="6" class="py-8 text-center text-slate-400">{{ 'ai.noAudit' | translate }}</td></tr>
          }
        </tbody>
      </table>
    </section>

    <app-modal [open]="formOpen()" [title]="(editing() ? 'ai.editClient' : 'ai.newClient') | translate" width="40rem" (closed)="formOpen.set(false)">
      <form class="space-y-4" (submit)="$event.preventDefault(); save()">
        @if (!editing()) {
          <div>
            <label class="label" for="ai-name">{{ 'common.name' | translate }}</label>
            <input id="ai-name" class="input" required maxlength="60" placeholder="Claude Code" [value]="name()" (input)="name.set($any($event.target).value)" />
          </div>
        }
        <fieldset>
          <legend class="label">{{ 'ai.scopes' | translate }}</legend>
          @for (s of normalScopes(); track s.name) {
            <label class="flex items-start gap-2 py-1 text-sm">
              <input type="checkbox" class="mt-1" [checked]="selected().has(s.name)" (change)="toggle(s.name)" />
              <span><span class="font-mono text-xs">{{ s.name }}</span><br /><span class="text-xs text-slate-500">{{ s.description }}</span>
                <span class="block text-[11px] text-slate-400">{{ 'ai.tools' | translate }}: {{ toolsFor(s.name) }}</span></span>
            </label>
          }
        </fieldset>
        <fieldset class="rounded-xl border border-rose-200 p-3 dark:border-rose-500/30">
          <legend class="px-1 text-xs font-semibold text-rose-700 dark:text-rose-300">{{ 'ai.sensitive' | translate }}</legend>
          @for (s of sensitiveScopes(); track s.name) {
            <label class="flex items-start gap-2 py-1 text-sm">
              <input type="checkbox" class="mt-1" [checked]="selected().has(s.name)" (change)="toggle(s.name)" />
              <span><span class="font-mono text-xs">{{ s.name }}</span><br /><span class="text-xs text-slate-500">{{ s.description }}</span></span>
            </label>
          }
        </fieldset>
        <div class="grid grid-cols-2 gap-3">
          @if (!editing()) {
            <div>
              <label class="label" for="ai-exp">{{ 'ai.expiresInDays' | translate }}</label>
              <input id="ai-exp" class="input num" type="number" min="1" max="730" [value]="expiresInDays()" (input)="expiresInDays.set(+$any($event.target).value)" />
            </div>
          }
          <div>
            <label class="label" for="ai-rate">{{ 'ai.rateLimit' | translate }}</label>
            <input id="ai-rate" class="input num" type="number" min="1" max="600" [value]="rateLimit()" (input)="rateLimit.set(+$any($event.target).value)" />
          </div>
        </div>
        <div class="flex justify-end gap-2">
          <button type="button" class="btn" (click)="formOpen.set(false)">{{ 'common.cancel' | translate }}</button>
          <button class="btn btn-primary">{{ 'common.save' | translate }}</button>
        </div>
      </form>
    </app-modal>

    <app-modal [open]="!!newToken()" [title]="'ai.tokenTitle' | translate" width="44rem" (closed)="newToken.set(null)">
      <div class="space-y-3">
        <p class="text-sm text-amber-700 dark:text-amber-300">{{ 'ai.tokenOnce' | translate }}</p>
        <pre class="overflow-x-auto rounded-lg bg-slate-100 p-3 font-mono text-xs dark:bg-slate-800">{{ newToken() }}</pre>
        <p class="text-sm text-slate-500">{{ 'ai.claudeCode' | translate }}</p>
        <pre class="overflow-x-auto rounded-lg bg-slate-100 p-3 font-mono text-xs dark:bg-slate-800">{{ claudeCommand() }}</pre>
        <div class="flex justify-end gap-2">
          <button class="btn" (click)="copy(claudeCommand())">{{ 'ai.copyCommand' | translate }}</button>
          <button class="btn btn-primary" (click)="copy(newToken() ?? '')">{{ 'ai.copyToken' | translate }}</button>
        </div>
      </div>
    </app-modal>
  `,
})
export class AiAccessComponent {
  private readonly http = inject(HttpClient);
  private readonly toasts = inject(Toasts);
  private readonly i18n = inject(TranslateService);
  private readonly refresh = signal(0);

  protected readonly catalog = rxResource({ stream: () => this.http.get<{ scopes: Scope[]; tools: Tool[] }>('/api/ai-admin/catalog') });
  protected readonly clients = rxResource({ params: () => this.refresh(), stream: () => this.http.get<AiClient[]>('/api/ai-admin/clients') });
  protected readonly audit = rxResource({ params: () => this.refresh(), stream: () => this.http.get<AuditEvent[]>('/api/ai-admin/audit', { params: { limit: 100 } }) });

  protected readonly normalScopes = computed(() => (this.catalog.value()?.scopes ?? []).filter((s) => !s.sensitive));
  protected readonly sensitiveScopes = computed(() => (this.catalog.value()?.scopes ?? []).filter((s) => s.sensitive));

  protected readonly formOpen = signal(false);
  protected readonly editing = signal<AiClient | null>(null);
  protected readonly name = signal('');
  protected readonly selected = signal(new Set<string>());
  protected readonly expiresInDays = signal(180);
  protected readonly rateLimit = signal(60);
  protected readonly newToken = signal<string | null>(null);
  protected readonly claudeCommand = computed(() =>
    `claude mcp add --transport http personal-finance ${location.origin}/mcp --header "Authorization: Bearer ${this.newToken()}"`,
  );

  protected isSensitive = (scope: string) => this.sensitiveScopes().some((s) => s.name === scope);
  protected toolsFor = (scope: string) => (this.catalog.value()?.tools ?? []).filter((t) => t.scope === scope).map((t) => t.name).join(', ') || '—';

  protected openNew() {
    this.editing.set(null);
    this.name.set('');
    // Sensible starting point: summaries only. Nothing sensitive is pre-selected.
    this.selected.set(new Set(['overview.read', 'expenses.summary.read', 'income.summary.read', 'budget.read', 'goals.read']));
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
        await firstValueFrom(this.http.put(`/api/ai-admin/clients/${editing.id}`, { scopes, rateLimitPerMinute: this.rateLimit() }));
      } else {
        const res = await firstValueFrom(this.http.post<{ id: string; token: string }>('/api/ai-admin/clients', {
          name: this.name(), scopes, expiresInDays: this.expiresInDays(), rateLimitPerMinute: this.rateLimit(),
        }));
        this.newToken.set(res.token);
      }
      this.formOpen.set(false);
      this.refresh.update((v) => v + 1);
    } catch (err) {
      this.toasts.error(err);
    }
  }

  protected async revoke(c: AiClient) {
    if (!confirm(this.i18n.instant('ai.confirmRevoke', { name: c.name }))) return;
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
