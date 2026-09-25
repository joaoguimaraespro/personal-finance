import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
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

@Component({
  selector: 'app-goals',
  imports: [TranslatePipe, MoneyPipe, PercentPipe, DayPipe, ProgressComponent, ModalComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mb-6 flex flex-wrap items-center justify-between gap-3">
      <div>
        <h1 class="text-2xl font-semibold tracking-tight">{{ 'nav.goals' | translate }}</h1>
        <p class="text-sm text-slate-500">{{ 'goals.subtitle' | translate }}</p>
      </div>
      <button class="btn btn-primary" (click)="open(null)">＋ {{ 'goals.new' | translate }}</button>
    </div>

    <div class="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
      @for (g of goals.value() ?? []; track g.id) {
        <div class="card">
          <div class="flex items-start justify-between">
            <p class="font-semibold">{{ g.name }}</p>
            @if (g.achieved) { <span class="badge bg-emerald-100 text-emerald-700 dark:bg-emerald-500/15 dark:text-emerald-300">{{ 'goals.achieved' | translate }}</span> }
          </div>
          <p class="num mt-3 text-2xl font-semibold">{{ g.progress | pct: 0 }}</p>
          <app-progress class="mt-2 block" [value]="g.progress" />
          <dl class="num mt-3 grid grid-cols-2 gap-2 text-xs text-slate-500">
            <div><dt>{{ 'goals.current' | translate }}</dt><dd class="font-medium text-slate-800 dark:text-slate-200">{{ g.currentAmount | money }}</dd></div>
            <div><dt>{{ 'goals.target' | translate }}</dt><dd class="font-medium text-slate-800 dark:text-slate-200">{{ g.targetAmount | money }}</dd></div>
            @if (g.targetDate) {
              <div><dt>{{ 'goals.by' | translate }}</dt><dd>{{ g.targetDate | day }}</dd></div>
              <div><dt>{{ 'goals.monthlyNeeded' | translate }}</dt><dd>{{ g.monthlyNeeded | money }}</dd></div>
            }
          </dl>
          <div class="mt-4 flex gap-2">
            <button class="btn !py-1 text-xs" (click)="open(g)">{{ 'common.edit' | translate }}</button>
            <button class="btn btn-ghost !py-1 text-xs" (click)="archive(g)">{{ 'common.archive' | translate }}</button>
          </div>
        </div>
      } @empty {
        <div class="card col-span-full py-12 text-center text-slate-400">{{ 'goals.empty' | translate }}</div>
      }
    </div>

    <app-modal [open]="formOpen()" [title]="(editing() ? 'goals.edit' : 'goals.new') | translate" (closed)="formOpen.set(false)">
      <form class="grid grid-cols-2 gap-3" (submit)="$event.preventDefault(); save()">
        <div class="col-span-2">
          <label class="label" for="g-name">{{ 'common.name' | translate }}</label>
          <input id="g-name" class="input" required maxlength="80" [value]="name()" (input)="name.set($any($event.target).value)" placeholder="Emergency fund" />
        </div>
        <div>
          <label class="label" for="g-target">{{ 'goals.target' | translate }}</label>
          <input id="g-target" class="input num" inputmode="decimal" required [value]="target()" (input)="target.set($any($event.target).value)" />
        </div>
        <div>
          <label class="label" for="g-date">{{ 'goals.by' | translate }}</label>
          <input id="g-date" class="input" type="date" [value]="date()" (input)="date.set($any($event.target).value)" />
        </div>
        <div>
          <label class="label" for="g-start">{{ 'goals.starting' | translate }}</label>
          <input id="g-start" class="input num" inputmode="decimal" [value]="starting()" (input)="starting.set($any($event.target).value)" />
        </div>
        <div>
          <label class="label" for="g-manual">{{ 'goals.manual' | translate }}</label>
          <input id="g-manual" class="input num" inputmode="decimal" [value]="manual()" (input)="manual.set($any($event.target).value)" />
        </div>
        <p class="col-span-2 text-xs text-slate-400">{{ 'goals.howProgress' | translate }}</p>
        <div class="col-span-2 flex justify-end gap-2">
          <button type="button" class="btn" (click)="formOpen.set(false)">{{ 'common.cancel' | translate }}</button>
          <button class="btn btn-primary">{{ 'common.save' | translate }}</button>
        </div>
      </form>
    </app-modal>
  `,
})
export class GoalsComponent {
  private readonly api = inject(Api);
  private readonly events = inject(DataEvents);
  private readonly toasts = inject(Toasts);
  private readonly i18n = inject(TranslateService);
  protected readonly goals = rxResource({ params: () => this.events.version(), stream: () => this.api.goals() });
  protected readonly formOpen = signal(false);
  protected readonly editing = signal<Goal | null>(null);
  protected readonly name = signal('');
  protected readonly target = signal('');
  protected readonly date = signal('');
  protected readonly starting = signal('');
  protected readonly manual = signal('');

  protected open(g: Goal | null) {
    this.editing.set(g);
    this.name.set(g?.name ?? '');
    this.target.set(g ? String(g.targetAmount) : '');
    this.date.set(g?.targetDate ?? '');
    this.starting.set(g ? String(g.startingAmount) : '');
    this.manual.set(g?.manualCurrentAmount != null ? String(g.manualCurrentAmount) : '');
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
