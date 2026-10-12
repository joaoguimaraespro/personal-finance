import { NgTemplateOutlet } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  afterNextRender,
  computed,
  inject,
  input,
  Injector,
  signal,
} from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideChevronDown, lucideChevronLeft, lucideChevronRight } from '@ng-icons/lucide';
import { BrnCalendar, BrnCalendarImports, injectBrnCalendarI18n } from '@spartan-ng/brain/calendar';
import { injectDateAdapter } from '@spartan-ng/brain/date-time';
import { buttonVariants, HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmSelectImports } from '@spartan-ng/helm/select';
import { classes, hlm } from '@spartan-ng/helm/utils';

@Component({
  selector: 'hlm-calendar',
  imports: [BrnCalendarImports, NgIcon, HlmSelectImports, NgTemplateOutlet, HlmButtonImports],
  viewProviders: [provideIcons({ lucideChevronDown, lucideChevronLeft, lucideChevronRight })],
  changeDetection: ChangeDetectionStrategy.OnPush,
  hostDirectives: [
    {
      directive: BrnCalendar,
      inputs: [
        'min',
        'max',
        'disabled',
        'date',
        'dateDisabled',
        'weekStartsOn',
        'highlightDays',
        'defaultFocusedDate',
      ],
      outputs: ['dateChange'],
    },
  ],
  host: { 'data-slot': 'calendar' },
  template: `
    <div class="inline-flex flex-col gap-4">
      <!-- Header -->
      <div class="flex w-full items-center justify-between gap-1.5">
        <ng-template #month>
          <hlm-select brnCalendarMonthSelect class="order-1">
            <hlm-select-trigger size="sm" [class]="_selectClass">
              <hlm-select-value />
            </hlm-select-trigger>
            <hlm-select-content *hlmSelectPortal class="max-h-80">
              <hlm-select-group>
                @for (month of _i18n.config().months(); track month) {
                  <hlm-select-item [value]="month">{{ month }}</hlm-select-item>
                }
              </hlm-select-group>
            </hlm-select-content>
          </hlm-select>
        </ng-template>
        <ng-template #year>
          <hlm-select brnCalendarYearSelect class="order-3">
            <hlm-select-trigger size="sm" [class]="_selectClass">
              <hlm-select-value />
            </hlm-select-trigger>
            <hlm-select-content *hlmSelectPortal class="max-h-80">
              <hlm-select-group>
                @for (year of _i18n.config().years(); track year) {
                  <hlm-select-item [value]="year">{{ year }}</hlm-select-item>
                }
              </hlm-select-group>
            </hlm-select-content>
          </hlm-select>
        </ng-template>
        @let heading = _heading();

        <button
          brnCalendarPreviousButton
          variant="ghost"
          hlmBtn
          class="order-first size-(--cell-size) p-0 select-none aria-disabled:opacity-50"
          [class.invisible]="_view() !== 'day'"
        >
          <ng-icon name="lucideChevronLeft" class="rtl:rotate-180" />
        </button>

        @switch (captionLayout()) {
          @case ('dropdown') {
            <ng-container [ngTemplateOutlet]="month" />
            <ng-container [ngTemplateOutlet]="year" />
          }
          @case ('dropdown-months') {
            <ng-container [ngTemplateOutlet]="month" />
            <div brnCalendarHeader class="order-4 text-sm font-medium">{{ heading.year }}</div>
          }
          @case ('dropdown-years') {
            <div brnCalendarHeader class="order-2 text-sm font-medium">{{ heading.month }}</div>
            <ng-container [ngTemplateOutlet]="year" />
          }
          @case ('drill') {
            <!-- Modern pickers: the heading opens the years, a year opens its months, a month its days. -->
            <button
              type="button"
              hlmBtn
              variant="ghost"
              size="sm"
              class="order-2 gap-1 font-medium"
              [attr.aria-expanded]="_view() !== 'day'"
              (click)="_toggleYears()"
            >
              {{ _view() === 'month' ? heading.year : heading.header }}
              <ng-icon
                name="lucideChevronDown"
                class="transition-transform"
                [class.rotate-180]="_view() !== 'day'"
              />
            </button>
          }
          @case ('label') {
            <div brnCalendarHeader class="order-5 text-sm font-medium">{{ heading.header }}</div>
          }
        }

        <button
          brnCalendarNextButton
          hlmBtn
          variant="ghost"
          class="order-last size-(--cell-size) p-0 select-none aria-disabled:opacity-50"
          [class.invisible]="_view() !== 'day'"
        >
          <ng-icon name="lucideChevronRight" class="rtl:rotate-180" />
        </button>
      </div>

      @if (_view() === 'year') {
        <div
          class="grid h-[17rem] w-[17rem] grid-cols-4 content-start gap-1 overflow-y-auto"
          role="listbox"
          data-slot="calendar-years"
        >
          @for (year of _i18n.config().years(); track year) {
            <button
              type="button"
              role="option"
              [attr.aria-selected]="year === _focusedYear()"
              [attr.data-current]="year === _focusedYear() ? '' : null"
              [class]="_pickClass(year === _focusedYear())"
              (click)="_pickYear(year)"
            >
              {{ year }}
            </button>
          }
        </div>
      } @else if (_view() === 'month') {
        <div class="grid w-[17rem] grid-cols-3 gap-2" role="listbox" data-slot="calendar-months">
          @for (m of _monthIndexes; track m) {
            <button
              type="button"
              role="option"
              [attr.aria-selected]="m === _focusedMonth()"
              [class]="_pickClass(m === _focusedMonth())"
              (click)="_pickMonth(m)"
            >
              {{ _i18n.config().formatMonth(m) }}
            </button>
          }
        </div>
      }
      <table
        class="w-full border-collapse space-y-1"
        brnCalendarGrid
        [class.hidden]="_view() !== 'day'"
      >
        <thead aria-hidden="true">
          <tr class="flex">
            <th
              *brnCalendarWeekday="let weekday"
              scope="col"
              class="text-muted-foreground flex-1 rounded-(--cell-radius) text-[0.8rem] font-normal select-none"
              [attr.aria-label]="_i18n.config().labelWeekday(weekday)"
            >
              {{ _i18n.config().formatWeekdayName(weekday) }}
            </th>
          </tr>
        </thead>

        <tbody role="rowgroup">
          <tr *brnCalendarWeek="let week" class="mt-2 flex w-full">
            @for (date of week; track _dateAdapter.getTime(date)) {
              <td
                brnCalendarCell
                class="group/day relative aspect-square h-full w-full rounded-(--cell-radius) p-0 text-center select-none [&:first-child[data-selected=true]_button]:rounded-s-(--cell-radius) [&:last-child[data-selected=true]_button]:rounded-e-(--cell-radius)"
              >
                <button brnCalendarCellButton [date]="date" [class]="_btnClass">
                  {{ _dateAdapter.getDate(date) }}
                </button>
              </td>
            }
          </tr>
        </tbody>
      </table>
    </div>
  `,
})
export class HlmCalendar<T> {
  /** Access the calendar i18n */
  protected readonly _i18n = injectBrnCalendarI18n();

  /** Access the date time adapter */
  protected readonly _dateAdapter = injectDateAdapter<T>();

  /** Show dropdowns to navigate between months or years. */
  public readonly captionLayout = input<
    'dropdown' | 'label' | 'dropdown-months' | 'dropdown-years' | 'drill'
  >('label');

  /** Access the calendar directive */
  private readonly _calendar = inject(BrnCalendar);

  private readonly _host = inject(ElementRef<HTMLElement>);
  private readonly _injector = inject(Injector);

  /** 'drill' layout: days, or the year / month grid opened from the heading. */
  protected readonly _view = signal<'day' | 'year' | 'month'>('day');
  protected readonly _monthIndexes = Array.from({ length: 12 }, (_, i) => i);
  protected readonly _focusedYear = computed(() =>
    this._dateAdapter.getYear(this._calendar.focusedDate()),
  );
  protected readonly _focusedMonth = computed(() =>
    this._dateAdapter.getMonth(this._calendar.focusedDate()),
  );

  protected _pickClass(selected: boolean): string {
    return hlm(
      buttonVariants({ variant: selected ? 'default' : 'ghost', size: 'sm' }),
      'h-9 w-full font-normal',
    );
  }

  protected _toggleYears() {
    if (this._view() !== 'day') {
      this._view.set('day');
      return;
    }
    this._view.set('year');
    // Open on the focused year, as native and Material pickers do.
    afterNextRender(
      () =>
        this._host.nativeElement
          .querySelector('[data-slot=calendar-years] [data-current]')
          ?.scrollIntoView({ block: 'center' }),
      { injector: this._injector },
    );
  }

  protected _pickYear(year: number) {
    this._focus({ year });
    this._view.set('month');
  }

  protected _pickMonth(month: number) {
    this._focus({ month });
    this._view.set('day');
  }

  /** Moves the focused date, keeping the day inside the target month (31 Jan → Feb gives the 28th/29th). */
  private _focus(change: { year?: number; month?: number }) {
    const a = this._dateAdapter;
    const focused = this._calendar.focusedDate();
    const target = a.set(a.startOfMonth(focused), change);
    const lastDay = a.getDate(a.endOfMonth(target));
    this._calendar.setFocusedDate(a.set(target, { day: Math.min(a.getDate(focused), lastDay) }));
  }

  /** Get the heading for the current month and year */
  protected readonly _heading = computed(() => {
    const config = this._i18n.config();
    const date = this._calendar.focusedDate();

    return {
      header: config.formatHeader(
        this._dateAdapter.getMonth(date),
        this._dateAdapter.getYear(date),
      ),
      month: config.formatMonth(this._dateAdapter.getMonth(date)),
      year: config.formatYear(this._dateAdapter.getYear(date)),
    };
  });

  protected readonly _btnClass = hlm(
    buttonVariants({ variant: 'ghost', size: 'icon' }),
    'data-[today=true]:bg-muted group-data-[focused=true]/day:border-ring group-data-[focused=true]/day:ring-ring/50 data-[range-end=true]:bg-primary data-[range-end=true]:text-primary-foreground data-[range-middle=true]:bg-muted data-[range-middle=true]:text-foreground data-[range-start=true]:bg-primary data-[range-start=true]:text-primary-foreground data-[selected-single=true]:bg-primary data-[selected-single=true]:text-primary-foreground dark:hover:bg-muted/50 dark:hover:text-foreground relative isolate z-10 flex aspect-square size-auto w-full min-w-(--cell-size) flex-col gap-1 border-0 leading-none font-normal group-data-[focused=true]/day:relative group-data-[focused=true]/day:z-10 group-data-[focused=true]/day:ring-[3px] data-[range-end=true]:rounded-(--cell-radius) data-[range-end=true]:rounded-e-(--cell-radius) data-[range-middle=true]:rounded-none data-[range-start=true]:rounded-(--cell-radius) data-[range-start=true]:rounded-s-(--cell-radius) [&>span]:text-xs [&>span]:opacity-70',
    'data-[outside=true]:opacity-50',
    "data-[highlighted]:before:content-['']",
    'data-[highlighted]:before:absolute',
    'data-[highlighted]:before:bottom-1',
    'data-[highlighted]:before:start-1/2',
    'data-[highlighted]:before:h-1',
    'data-[highlighted]:before:w-1',
    'data-[highlighted]:before:-translate-x-1/2',
    'data-[highlighted]:before:rounded-full',
    'data-[highlighted]:before:bg-destructive',
  );

  protected readonly _selectClass = 'gap-0 px-1.5 py-2 [&>ng-icon]:ms-1';

  constructor() {
    classes(
      () =>
        'p-3 [--cell-radius:var(--radius-md)] [--cell-size:--spacing(8)] group/calendar bg-background block in-data-[slot=card-content]:bg-transparent in-data-[slot=popover-content]:bg-transparent',
    );
  }
}
