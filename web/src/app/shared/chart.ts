import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  OnDestroy,
  afterNextRender,
  computed,
  effect,
  inject,
  input,
  viewChild,
} from '@angular/core';
import type { EChartsOption, ECharts } from 'echarts';
import { NgIcon } from '@ng-icons/core';
import { TranslatePipe } from '@ngx-translate/core';
import { Prefs } from '../core/prefs';
import { APP_ICONS } from './icons';
import { CHART_FONT } from './chart-options';
import { displayFx } from '../core/display-currency';

/** True when no series has a single non-zero value: nothing worth drawing (e.g. a year without movements). */
export function chartIsEmpty(option: EChartsOption): boolean {
  const series = option.series
    ? Array.isArray(option.series)
      ? option.series
      : [option.series]
    : [];
  const valueOf = (d: unknown): unknown =>
    Array.isArray(d)
      ? d[d.length - 1]
      : d !== null && typeof d === 'object'
        ? (d as { value?: unknown }).value
        : d;
  const hasValue = (v: unknown): boolean =>
    Array.isArray(v)
      ? hasValue(v[v.length - 1])
      : typeof v === 'number'
        ? v !== 0 && Number.isFinite(v)
        : false;
  return !series.some((s) =>
    ((s as { data?: unknown[] }).data ?? []).some((d) => hasValue(valueOf(d))),
  );
}

/**
 * Minimal ECharts host: lazy-loads the library, follows the theme and resizes with its container.
 * With nothing to draw it shows a "no data" message instead of empty axes.
 */
@Component({
  selector: 'app-chart',
  imports: [NgIcon, TranslatePipe],
  providers: [APP_ICONS],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="relative h-full w-full">
      <div #host class="h-full w-full" [class.invisible]="empty()"></div>
      @if (empty()) {
        <div
          class="absolute inset-0 flex flex-col items-center justify-center gap-2 text-center text-sm text-muted-foreground"
          role="status"
        >
          <ng-icon name="lucideChartColumn" class="text-2xl opacity-50" aria-hidden="true" />
          {{ emptyText() || ('common.noData' | translate) }}
        </div>
      }
    </div>
  `,
  host: { class: 'block' },
})
export class ChartComponent implements OnDestroy {
  readonly option = input.required<EChartsOption>();
  /** Overrides the default "no data for this period" message. */
  readonly emptyText = input('');
  protected readonly empty = computed(() => chartIsEmpty(this.option()));
  private readonly host = viewChild.required<ElementRef<HTMLDivElement>>('host');
  private readonly prefs = inject(Prefs);
  private chart?: ECharts;
  private observer?: ResizeObserver;
  private echarts?: typeof import('echarts');

  constructor() {
    afterNextRender(async () => {
      this.echarts = await import('echarts');
      this.render();
      this.observer = new ResizeObserver(() => this.chart?.resize());
      this.observer.observe(this.host().nativeElement);
    });
    effect(() => {
      this.option();
      this.prefs.theme();
      displayFx(); // money labels follow the display currency
      this.render();
    });
  }

  private render() {
    if (!this.echarts) return;
    const dark = document.documentElement.classList.contains('dark');
    const themeKey = dark ? 'dark' : 'light';
    if (this.chart && (this.chart as unknown as { __theme?: string }).__theme !== themeKey) {
      this.chart.dispose();
      this.chart = undefined;
    }
    if (!this.chart) {
      this.chart = this.echarts.init(this.host().nativeElement, dark ? 'dark' : undefined, {
        renderer: 'svg',
      });
      (this.chart as unknown as { __theme?: string }).__theme = themeKey;
    }
    // The app's font (ECharts draws its own text and does not inherit CSS).
    this.chart.setOption(
      { backgroundColor: 'transparent', textStyle: { fontFamily: CHART_FONT }, ...this.option() },
      true,
    );
  }

  ngOnDestroy() {
    this.observer?.disconnect();
    this.chart?.dispose();
  }
}
