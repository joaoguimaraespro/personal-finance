import { ChangeDetectionStrategy, Component, model } from '@angular/core';
import { MonthNamePipe, shiftPeriod } from '../core/format';

@Component({
  selector: 'app-month-picker',
  imports: [MonthNamePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="inline-flex items-center gap-1 rounded-xl border border-slate-200 bg-white p-1 dark:border-slate-700 dark:bg-slate-900">
      <button class="btn btn-ghost !px-2 !py-1" (click)="move(-1)" aria-label="Previous month">‹</button>
      <span class="min-w-36 text-center text-sm font-semibold">
        {{ month() | monthName }} {{ year() }}
      </span>
      <button class="btn btn-ghost !px-2 !py-1" (click)="move(1)" aria-label="Next month">›</button>
    </div>
  `,
})
export class MonthPickerComponent {
  readonly period = model.required<string>();

  year = () => Number(this.period().slice(0, 4));
  month = () => Number(this.period().slice(5, 7));

  move(delta: number) {
    this.period.set(shiftPeriod(this.period(), delta));
  }
}
