import { ChangeDetectionStrategy, Component, computed, input, linkedSignal } from '@angular/core';
import { AssetClass } from '../core/models';
import { ASSET_CLASS_COLORS } from './chart-options';

/**
 * A share's or ETF's logo, served by this app (the server fetches it once from a public logo source and keeps it;
 * the browser never contacts anyone else). Without one — most European ETFs — the ticker's first letters in the
 * asset class colour.
 */
@Component({
  selector: 'app-security-icon',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    class: 'inline-flex shrink-0 items-center justify-center',
    '[style.width.px]': 'size()',
    '[style.height.px]': 'size()',
    'aria-hidden': 'true',
  },
  template: `
    @if (!failed()) {
      <img
        [src]="'/api/securities/' + securityId() + '/logo'"
        alt=""
        class="bg-white size-full rounded-full object-contain"
        loading="lazy"
        decoding="async"
        [width]="size()"
        [height]="size()"
        (error)="failed.set(true)"
      />
    } @else {
      <span
        class="flex size-full items-center justify-center rounded-full font-semibold leading-none"
        [style.background-color]="color() + '22'"
        [style.color]="color()"
        [style.font-size.px]="size() * (initials().length > 2 ? 0.3 : 0.38)"
        >{{ initials() }}</span
      >
    }
  `,
})
export class SecurityIconComponent {
  readonly securityId = input.required<string>();
  readonly symbol = input('');
  readonly assetClass = input<AssetClass>('Stock');
  readonly size = input(18);

  /** Reset when the row shows another security. */
  protected readonly failed = linkedSignal({ source: this.securityId, computation: () => false });
  protected readonly color = computed(() => ASSET_CLASS_COLORS[this.assetClass()] ?? '#71717a');
  protected readonly initials = computed(
    () =>
      this.symbol()
        .replace(/[^a-z0-9]/gi, '')
        .slice(0, 3)
        .toUpperCase() || '?',
  );
}
