import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { NgIcon } from '@ng-icons/core';
import { APP_ICONS } from './icons';

export type StatusTone = 'success' | 'warning' | 'danger' | 'neutral' | 'info';

const TONES: Record<StatusTone, { cls: string; icon: string }> = {
  success: {
    cls: 'bg-emerald-50 text-emerald-700 ring-emerald-600/15 dark:bg-emerald-500/10 dark:text-emerald-300 dark:ring-emerald-400/20',
    icon: 'lucideCircleCheck',
  },
  warning: {
    cls: 'bg-amber-50 text-amber-800 ring-amber-600/20 dark:bg-amber-500/10 dark:text-amber-300 dark:ring-amber-400/20',
    icon: 'lucideTriangleAlert',
  },
  danger: {
    cls: 'bg-rose-50 text-rose-700 ring-rose-600/15 dark:bg-rose-500/10 dark:text-rose-300 dark:ring-rose-400/20',
    icon: 'lucideCircleX',
  },
  neutral: {
    cls: 'bg-muted text-muted-foreground ring-border',
    icon: 'lucideCirclePause',
  },
  info: {
    cls: 'bg-primary/10 text-primary ring-primary/20 dark:bg-primary/15',
    icon: 'lucideInfo',
  },
};

/** Status pill: tone colour + icon + text, so state never relies on colour alone. */
@Component({
  selector: 'app-status-badge',
  imports: [NgIcon],
  providers: [APP_ICONS],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'inline-flex' },
  template: `
    <span
      class="inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-medium whitespace-nowrap ring-1 ring-inset"
      [class]="cls()"
    >
      <ng-icon [name]="iconName()" class="text-[13px]" aria-hidden="true" />
      <ng-content />
    </span>
  `,
})
export class StatusBadgeComponent {
  readonly tone = input<StatusTone>('neutral');
  /** Overrides the tone's default icon. */
  readonly icon = input('');

  protected readonly cls = computed(() => TONES[this.tone()].cls);
  protected readonly iconName = computed(() => this.icon() || TONES[this.tone()].icon);
}
