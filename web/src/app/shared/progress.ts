import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

@Component({
  selector: 'app-progress',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="h-2 w-full overflow-hidden rounded-full bg-slate-100 dark:bg-slate-800">
      <div class="h-full rounded-full transition-all" [style.width.%]="width()" [class]="barClass()"></div>
    </div>
  `,
})
export class ProgressComponent {
  /** Fraction, where 1 = 100%. Values above 1 are shown full and flagged. */
  readonly value = input(0);
  readonly tone = input<'brand' | 'auto'>('brand');

  readonly width = computed(() => Math.max(0, Math.min(100, this.value() * 100)));
  readonly barClass = computed(() => {
    if (this.tone() === 'brand') return 'bg-brand-500';
    const v = this.value();
    return v > 1 ? 'bg-rose-500' : v >= 0.9 ? 'bg-amber-500' : 'bg-emerald-500';
  });
}
