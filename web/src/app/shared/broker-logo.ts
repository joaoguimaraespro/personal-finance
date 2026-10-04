import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { NgIcon } from '@ng-icons/core';
import { Broker } from '../core/models';
import { APP_ICONS } from './icons';

/**
 * Official broker symbol, used only to identify the broker (nominative use; trademarks belong to their
 * owners — see README). Brokers without a logo (e.g. Demo) fall back to a neutral icon.
 */
const LOGOS: Partial<Record<Broker, string>> = {
  Trading212: 'brokers/trading212.png',
  InteractiveBrokers: 'brokers/interactive-brokers.png',
};

@Component({
  selector: 'app-broker-logo',
  imports: [NgIcon],
  providers: [APP_ICONS],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    class: 'inline-flex shrink-0 items-center justify-center overflow-hidden',
    '[style.width.px]': 'size()',
    '[style.height.px]': 'size()',
    '[style.border-radius]': 'size() * 0.24 + "px"',
  },
  template: `
    @if (src(); as s) {
      <img [src]="s" alt="" class="size-full object-cover" loading="lazy" />
    } @else {
      <span class="bg-muted text-muted-foreground flex size-full items-center justify-center">
        <ng-icon
          [name]="broker() === 'Manual' ? 'lucideWallet' : 'lucideLandmark'"
          [style.font-size.px]="size() * 0.5"
        />
      </span>
    }
  `,
})
export class BrokerLogoComponent {
  readonly broker = input.required<Broker | string>();
  readonly size = input(40);
  protected readonly src = computed(() => LOGOS[this.broker() as Broker] ?? null);
}
