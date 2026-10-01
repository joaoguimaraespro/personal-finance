import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { NgIcon } from '@ng-icons/core';
import { MoneyPipe, PercentPipe } from '../core/format';
import { APP_ICONS } from './icons';

/** A headline figure with an optional delta against a reference (previous month, average or budget). */
@Component({
  selector: 'app-kpi',
  imports: [NgIcon, MoneyPipe, PercentPipe],
  providers: [APP_ICONS],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'block' },
  template: `
    <div class="card @container h-full !p-4">
      <div
        class="text-muted-foreground flex flex-col items-start gap-2 text-xs font-medium @[8rem]:flex-row @[8rem]:items-center"
      >
        @if (icon()) {
          <span
            class="flex size-7 shrink-0 items-center justify-center rounded-full"
            [style.color]="color()"
            [style.background]="tint()"
            aria-hidden="true"
          >
            <ng-icon [name]="icon()" class="text-sm" />
          </span>
        } @else {
          <span class="h-2 w-2 shrink-0 rounded-full" [style.background]="color()"></span>
        }
        <span class="min-w-0">{{ label() }}</span>
      </div>
      <div
        class="num mt-2 text-lg font-semibold tracking-tight whitespace-nowrap @[11rem]:text-xl @[14rem]:text-2xl"
      >
        @if (kind() === 'percent') {
          {{ value() | pct }}
        } @else {
          {{ value() | money }}
        }
      </div>
      @if (deltaText()) {
        <div class="num mt-1 flex items-center gap-1 text-xs" [class]="deltaClass()">
          <ng-icon [name]="deltaIcon()" class="shrink-0" aria-hidden="true" />{{ deltaText() }}
        </div>
      }
    </div>
  `,
})
export class KpiComponent {
  readonly label = input.required<string>();
  readonly value = input<number | null>(null);
  readonly kind = input<'money' | 'percent'>('money');
  readonly color = input('#71717a');
  /** Optional Lucide icon, shown in a circle tinted with {@link color}. */
  readonly icon = input('');
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
    const magnitude =
      this.kind() === 'percent'
        ? `${(Math.abs(d) * 100).toFixed(1)} pp`
        : new Intl.NumberFormat(undefined, { maximumFractionDigits: 0 }).format(Math.abs(d)) + ' €';
    const sign = d > 0 ? '+' : d < 0 ? '−' : '';
    return `${sign}${magnitude} ${this.referenceLabel()}`;
  });

  readonly tint = computed(() => `color-mix(in oklab, ${this.color()} 14%, transparent)`);

  readonly deltaIcon = computed(() => {
    const d = this.delta() ?? 0;
    return d > 0 ? 'lucideTrendingUp' : d < 0 ? 'lucideTrendingDown' : 'lucideArrowRight';
  });

  readonly deltaClass = computed(() => {
    const d = this.delta();
    if (!d) return 'text-muted-foreground';
    const good = d > 0 === this.higherIsBetter();
    return good ? 'text-emerald-700 dark:text-emerald-400' : 'text-rose-600 dark:text-rose-400';
  });
}
