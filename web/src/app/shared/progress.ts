import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { HlmProgressImports } from '@spartan-ng/helm/progress';

@Component({
  selector: 'app-progress',
  imports: [HlmProgressImports],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <hlm-progress class="h-2" [value]="width()">
      <hlm-progress-indicator class="bg-(--bar)" [style.--bar]="barColor()" />
    </hlm-progress>
  `,
})
export class ProgressComponent {
  /** Fraction, where 1 = 100%. Values above 1 are shown full and flagged. */
  readonly value = input(0);
  readonly tone = input<'brand' | 'auto'>('brand');

  readonly width = computed(() => Math.max(0, Math.min(100, this.value() * 100)));
  /** Over budget = rose, close to it = amber, otherwise the accent. */
  readonly barColor = computed(() => {
    if (this.tone() === 'brand') return 'var(--primary)';
    const v = this.value();
    return v > 1 ? 'var(--color-rose-500)' : v >= 0.9 ? 'var(--color-amber-500)' : 'var(--primary)';
  });
}
