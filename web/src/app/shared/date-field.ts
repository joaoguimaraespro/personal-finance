import {
  ChangeDetectionStrategy,
  Component,
  booleanAttribute,
  computed,
  inject,
  input,
  output,
} from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCalendar, lucideX } from '@ng-icons/lucide';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmDatePickerImports } from '@spartan-ng/helm/date-picker';
import { Prefs } from '../core/prefs';

let nextId = 0;

/** Calendar popover for an ISO date string ("YYYY-MM-DD", "" = none). Displayed in the user's locale. */
@Component({
  selector: 'app-date-field',
  imports: [HlmDatePickerImports, HlmButtonImports, NgIcon],
  providers: [provideIcons({ lucideCalendar, lucideX })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'block' },
  template: `
    <div class="flex items-center gap-1">
      <hlm-date-picker
        class="min-w-0 flex-1"
        [date]="asDate()"
        (dateChange)="pick($event)"
        [formatDate]="format()"
        [autoCloseOnSelect]="true"
      >
        <hlm-date-picker-trigger
          class="w-full [&_button]:w-full [&_button]:justify-between [&_button]:font-normal"
          [buttonId]="inputId()"
          [attr.aria-label]="ariaLabel()"
        >
          <span class="text-muted-foreground">{{ placeholder() }}</span>
        </hlm-date-picker-trigger>
      </hlm-date-picker>
      @if (clearable() && value()) {
        <button
          hlmBtn
          variant="ghost"
          size="icon-sm"
          type="button"
          (click)="valueChange.emit('')"
          aria-label="Clear date"
        >
          <ng-icon name="lucideX" />
        </button>
      }
    </div>
  `,
})
export class DateFieldComponent {
  readonly value = input<string | null | undefined>('');
  readonly placeholder = input('');
  readonly clearable = input(false, { transform: booleanAttribute });
  readonly inputId = input(`date-field-${++nextId}`);
  readonly ariaLabel = input<string | null>(null);
  readonly valueChange = output<string>();

  private readonly prefs = inject(Prefs);

  protected readonly asDate = computed(() => {
    const v = this.value();
    if (!v) return undefined;
    const [y, m, d] = v.split('-').map(Number);
    return new Date(y, m - 1, d);
  });

  protected readonly format = computed(() => {
    const fmt = new Intl.DateTimeFormat(this.prefs.locale(), {
      day: '2-digit',
      month: 'short',
      year: 'numeric',
    });
    return (d: Date) => fmt.format(d);
  });

  protected pick(d: Date | null) {
    if (!d) return this.valueChange.emit('');
    const iso = `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
    this.valueChange.emit(iso);
  }
}
