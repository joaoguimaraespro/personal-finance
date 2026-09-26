import { ChangeDetectionStrategy, Component, OnDestroy, computed, inject, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { Api } from '../../core/api';
import { DataEvents } from '../../core/data-events';
import { DayPipe } from '../../core/format';
import { Broker, Connection, ProviderInfo, SyncJob } from '../../core/models';
import { Toasts } from '../../core/toast';
import { ModalComponent } from '../../shared/modal';

/** Credentials are write-only: the form can set them, the page never displays them. */
@Component({
  selector: 'app-connections',
  imports: [TranslatePipe, DayPipe, ModalComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mb-6 flex flex-wrap items-center justify-between gap-3">
      <div>
        <h1 class="text-2xl font-semibold tracking-tight">{{ 'nav.connections' | translate }}</h1>
        <p class="text-sm text-slate-500">{{ 'connections.subtitle' | translate }}</p>
      </div>
      <div class="flex flex-wrap gap-2">
        @for (p of providers.value() ?? []; track p.kind) {
          <button class="btn" (click)="openNew(p)">＋ {{ p.name }}</button>
        }
      </div>
    </div>

    <div class="grid gap-4 lg:grid-cols-2">
      @for (c of connections.value() ?? []; track c.id) {
        <section class="card">
          <div class="flex items-start justify-between gap-3">
            <div>
              <p class="font-semibold">{{ c.displayName }}</p>
              <p class="text-xs text-slate-500">{{ 'source.' + c.kind | translate }} · {{ 'connections.readOnly' | translate }}</p>
            </div>
            <span class="badge" [class]="statusClass(c)">{{ 'connections.status.' + c.status | translate }}</span>
          </div>
          <dl class="mt-4 grid grid-cols-2 gap-2 text-sm">
            <div><dt class="text-xs text-slate-500">{{ 'connections.lastSync' | translate }}</dt><dd>{{ c.lastSuccessfulSyncUtc | day }}</dd></div>
            <div>
              <dt class="text-xs text-slate-500">{{ 'connections.lastRun' | translate }}</dt>
              <dd>
                @if (c.lastJob; as j) {
                  {{ 'connections.outcome.' + j.outcome | translate }}
                  @if (j.outcome !== 'Running') { · +{{ j.imported }} / ~{{ j.updated }} }
                } @else { — }
              </dd>
            </div>
            @if (c.credentialsExpireOn) {
              <div class="col-span-2" [class.text-amber-600]="expiresSoon(c)">
                <dt class="text-xs text-slate-500">{{ 'connections.expires' | translate }}</dt><dd>{{ c.credentialsExpireOn | day }}</dd>
              </div>
            }
          </dl>
          @if (c.lastError) {
            <p class="mt-3 rounded-lg bg-rose-50 p-2 text-xs text-rose-700 dark:bg-rose-500/10 dark:text-rose-300">{{ c.lastError }}</p>
          }
          <div class="mt-4 flex flex-wrap gap-2">
            <button class="btn btn-primary !py-1 text-xs" [disabled]="c.status === 'Disabled' || c.lastJob?.outcome === 'Running'" (click)="sync(c)">{{ 'connections.syncNow' | translate }}</button>
            @if (c.kind === 'Trading212') {
              <label class="btn !py-1 text-xs">
                {{ 'connections.importCsv' | translate }}
                <input type="file" accept=".csv,text/csv" class="hidden" (change)="importCsv(c, $any($event.target).files?.[0]); $any($event.target).value = ''" />
              </label>
            }
            <button class="btn btn-ghost !py-1 text-xs" (click)="showJobs(c)">{{ 'connections.history' | translate }}</button>
            <button class="btn btn-ghost !py-1 text-xs" (click)="openCredentials(c)">{{ 'connections.updateCredentials' | translate }}</button>
            <button class="btn btn-ghost !py-1 text-xs" (click)="setEnabled(c, c.status === 'Disabled')">{{ (c.status === 'Disabled' ? 'connections.enable' : 'connections.disable') | translate }}</button>
            <button class="btn btn-ghost !py-1 text-xs text-rose-600" (click)="remove(c)">{{ 'common.delete' | translate }}</button>
          </div>
        </section>
      } @empty {
        <section class="card col-span-full py-12 text-center text-slate-400">{{ 'connections.empty' | translate }}</section>
      }
    </div>

    <app-modal [open]="!!provider()" [title]="provider()?.name ?? ''" (closed)="provider.set(null)">
      @if (provider(); as p) {
        <form class="space-y-3" (submit)="$event.preventDefault(); save()" autocomplete="off">
          <p class="rounded-xl bg-slate-50 p-3 text-xs text-slate-600 dark:bg-slate-800 dark:text-slate-300">{{ p.setupHint }}</p>
          @if (!editingId()) {
            <div>
              <label class="label" for="c-name">{{ 'common.name' | translate }}</label>
              <input id="c-name" class="input" required maxlength="80" [value]="name()" (input)="name.set($any($event.target).value)" />
            </div>
          }
          @for (f of p.fields; track f.key) {
            <div>
              <label class="label" [for]="'c-' + f.key">{{ f.label }}{{ f.required ? ' *' : '' }}</label>
              <input [id]="'c-' + f.key" class="input font-mono" [type]="f.secret ? 'password' : 'text'" autocomplete="off" spellcheck="false" [required]="f.required" (input)="setField(f.key, $any($event.target).value)" />
              @if (f.hint) { <p class="mt-1 text-[11px] text-slate-400">{{ f.hint }}</p> }
            </div>
          }
          @if (p.kind === 'InteractiveBrokers') {
            <div>
              <label class="label" for="c-exp">{{ 'connections.expires' | translate }}</label>
              <input id="c-exp" class="input" type="date" (input)="expires.set($any($event.target).value)" />
            </div>
          }
          <p class="text-[11px] text-slate-400">{{ 'connections.credentialsNote' | translate }}</p>
          <div class="flex justify-end gap-2">
            <button type="button" class="btn" (click)="provider.set(null)">{{ 'common.cancel' | translate }}</button>
            <button class="btn btn-primary" [disabled]="busy()">{{ 'common.save' | translate }}</button>
          </div>
        </form>
      }
    </app-modal>

    <app-modal [open]="!!jobs()" [title]="'connections.history' | translate" width="40rem" (closed)="jobs.set(null)">
      <ul class="space-y-2">
        @for (j of jobs() ?? []; track j.id) {
          <li class="rounded-xl border border-slate-100 p-3 text-sm dark:border-slate-800">
            <div class="flex justify-between">
              <span class="font-medium">{{ 'connections.outcome.' + j.outcome | translate }} · {{ 'connections.trigger.' + j.trigger | translate }}</span>
              <span class="text-xs text-slate-400">{{ j.startedAtUtc | day }}</span>
            </div>
            <p class="num text-xs text-slate-500">{{ 'connections.counts' | translate: { imported: j.imported, updated: j.updated, ignored: j.ignored } }}</p>
            @for (e of j.errors.slice(0, 5); track $index) { <p class="text-xs text-rose-600">{{ e }}</p> }
          </li>
        }
      </ul>
    </app-modal>
  `,
})
export class ConnectionsComponent implements OnDestroy {
  private readonly api = inject(Api);
  private readonly events = inject(DataEvents);
  private readonly toasts = inject(Toasts);
  private readonly i18n = inject(TranslateService);

  private readonly refresh = signal(0);
  protected readonly providers = rxResource({ stream: () => this.api.providers() });
  protected readonly connections = rxResource({
    params: () => ({ v: this.events.version(), r: this.refresh() }),
    stream: () => this.api.connections(),
  });
  private readonly running = computed(() => (this.connections.value() ?? []).some((c) => c.lastJob?.outcome === 'Running'));
  // Syncs run in the background: poll while one is in progress, then refresh every view.
  private readonly poll = setInterval(() => {
    if (this.running() || this.pendingSince) {
      this.refresh.update((v) => v + 1);
      if (this.pendingSince && !this.running() && Date.now() - this.pendingSince > 4000) {
        this.pendingSince = 0;
        this.events.bump();
      }
    }
  }, 2000);
  private pendingSince = 0;

  protected readonly provider = signal<ProviderInfo | null>(null);
  protected readonly editingId = signal<string | null>(null);
  protected readonly name = signal('');
  protected readonly fields = signal<Record<string, string>>({});
  protected readonly expires = signal('');
  protected readonly busy = signal(false);
  protected readonly jobs = signal<SyncJob[] | null>(null);

  ngOnDestroy() {
    clearInterval(this.poll);
  }

  protected statusClass(c: Connection) {
    return c.status === 'Active'
      ? 'bg-emerald-100 text-emerald-700 dark:bg-emerald-500/15 dark:text-emerald-300'
      : c.status === 'NeedsAttention'
        ? 'bg-amber-100 text-amber-800 dark:bg-amber-500/15 dark:text-amber-300'
        : 'bg-slate-100 text-slate-500 dark:bg-slate-800';
  }

  protected expiresSoon(c: Connection) {
    return !!c.credentialsExpireOn && new Date(c.credentialsExpireOn).getTime() - Date.now() < 30 * 86_400_000;
  }

  protected openNew(p: ProviderInfo) {
    this.editingId.set(null);
    this.name.set(p.name);
    this.fields.set({});
    this.expires.set('');
    this.provider.set(p);
  }

  protected openCredentials(c: Connection) {
    const p = this.providers.value()?.find((x) => x.kind === c.kind);
    if (!p) return;
    this.editingId.set(c.id);
    this.fields.set({});
    this.expires.set(c.credentialsExpireOn ?? '');
    this.provider.set(p);
  }

  protected setField(key: string, value: string) {
    this.fields.update((f) => ({ ...f, [key]: value }));
  }

  protected async save() {
    const p = this.provider();
    if (!p) return;
    this.busy.set(true);
    try {
      const body = { credentials: this.fields(), credentialsExpireOn: this.expires() || null };
      const id = this.editingId();
      if (id) {
        await firstValueFrom(this.api.updateCredentials(id, body));
        await firstValueFrom(this.api.syncConnection(id));
      } else {
        await firstValueFrom(this.api.createConnection({ kind: p.kind as Broker, displayName: this.name(), ...body }));
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
      this.toasts.show(this.i18n.instant('connections.csvDone', { imported: job.imported, updated: job.updated }));
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
    if (!confirm(this.i18n.instant('connections.confirmDelete', { name: c.displayName }))) return;
    const purge = confirm(this.i18n.instant('connections.confirmPurge'));
    try {
      await firstValueFrom(this.api.deleteConnection(c.id, purge));
      this.events.bump();
    } catch (err) {
      this.toasts.error(err);
    }
  }
}
