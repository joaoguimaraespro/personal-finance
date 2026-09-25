import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { MoneyPipe, PercentPipe } from '../core/format';

/** A headline figure with an optional delta against a reference (previous month, average or budget). */
@Component({
  selector: 'app-kpi',
  imports: [MoneyPipe, PercentPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="card h-full !p-4">
      <div class="flex items-center gap-2 text-xs font-medium text-slate-500 dark:text-slate-400">
        <span class="h-2 w-2 rounded-full" [style.background]="color()"></span>{{ label() }}
      </div>
      <div class="num mt-2 text-2xl font-semibold tracking-tight">
        @if (kind() === 'percent') { {{ value() | pct }} } @else { {{ value() | money }} }
      </div>
      @if (deltaText()) {
        <div class="num mt-1 text-xs" [class]="deltaClass()">{{ deltaText() }}</div>
      }
    </div>
  `,
})
export class KpiComponent {
  readonly label = input.required<string>();
  readonly value = input<number | null>(null);
  readonly kind = input<'money' | 'percent'>('money');
  readonly color = input('#64748b');
  /** Reference value and its caption, e.g. previous month. */
  readonly reference = input<number | null>(null);
  readonly referenceLabel = input('');
  /** Whether an increase is good (income, savings) or bad (expenses). */
  readonly higherIsBetter = input(true);

  private readonly delta = computed(() => {
    const v = this.value();
    const r = this.reference();
    return v === null || r === null ? null : v - r;
  });

  readonly deltaText = computed(() => {
    const d = this.delta();
    if (d === null || !this.referenceLabel()) return '';
    const arrow = d > 0 ? '▲' : d < 0 ? '▼' : '•';
    const magnitude =
      this.kind() === 'percent'
        ? `${(Math.abs(d) * 100).toFixed(1)} pp`
        : new Intl.NumberFormat(undefined, { maximumFractionDigits: 0 }).format(Math.abs(d)) + ' €';
    return `${arrow} ${magnitude} ${this.referenceLabel()}`;
  });

  readonly deltaClass = computed(() => {
    const d = this.delta();
    if (!d) return 'text-slate-400';
    const good = d > 0 === this.higherIsBetter();
    return good ? 'text-emerald-600 dark:text-emerald-400' : 'text-rose-600 dark:text-rose-400';
  });
}
