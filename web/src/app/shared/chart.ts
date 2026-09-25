import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  OnDestroy,
  afterNextRender,
  effect,
  inject,
  input,
  viewChild,
} from '@angular/core';
import type { EChartsOption, ECharts } from 'echarts';
import { Prefs } from '../core/prefs';

/** Minimal ECharts host: lazy-loads the library, follows the theme and resizes with its container. */
@Component({
  selector: 'app-chart',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<div #host class="h-full w-full"></div>`,
  host: { class: 'block' },
})
export class ChartComponent implements OnDestroy {
  readonly option = input.required<EChartsOption>();
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
      this.chart = this.echarts.init(this.host().nativeElement, dark ? 'dark' : undefined, { renderer: 'svg' });
      (this.chart as unknown as { __theme?: string }).__theme = themeKey;
    }
    this.chart.setOption({ backgroundColor: 'transparent', ...this.option() }, true);
  }

  ngOnDestroy() {
    this.observer?.disconnect();
    this.chart?.dispose();
  }
}
