import { ChangeDetectionStrategy, Component, computed, inject, model } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideChevronLeft, lucideChevronRight } from '@ng-icons/lucide';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmDatePickerImports } from '@spartan-ng/helm/date-picker';
import { shiftPeriod } from '../core/format';
import { Prefs } from '../core/prefs';

/** Previous / next arrows around a month-grid popover for jumping further. Value is "YYYY-MM". */
@Component({
  selector: 'app-month-picker',
  imports: [HlmButtonImports, HlmDatePickerImports, NgIcon],
  providers: [provideIcons({ lucideChevronLeft, lucideChevronRight })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="inline-flex items-center gap-1">
      <button hlmBtn variant="outline" size="icon" (click)="move(-1)" aria-label="Previous month">
        <ng-icon name="lucideChevronLeft" />
      </button>
      <hlm-month-year-picker
        [date]="asDate()"
        (dateChange)="pick($event)"
        [formatDate]="format()"
        [autoCloseOnSelect]="true"
      >
        <hlm-date-picker-trigger class="w-44 [&_button]:justify-center [&_button]:font-semibold" />
      </hlm-month-year-picker>
      <button hlmBtn variant="outline" size="icon" (click)="move(1)" aria-label="Next month">
        <ng-icon name="lucideChevronRight" />
      </button>
    </div>
  `,
})
export class MonthPickerComponent {
  readonly period = model.required<string>();
  private readonly prefs = inject(Prefs);

  year = () => Number(this.period().slice(0, 4));
  month = () => Number(this.period().slice(5, 7));
  protected readonly asDate = computed(() => new Date(this.year(), this.month() - 1, 1));
  protected readonly format = computed(() => {
    const fmt = new Intl.DateTimeFormat(this.prefs.locale(), { month: 'long', year: 'numeric' });
    return (d: Date) => fmt.format(d);
  });

  move(delta: number) {
    this.period.set(shiftPeriod(this.period(), delta));
  }

  protected pick(d: Date | null) {
    if (!d) return;
    this.period.set(`${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`);
  }
}
