import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { BrnDialogContent } from '@spartan-ng/brain/dialog';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';

/** Modal dialog (spartan/ui on the CDK): focus trap, Esc to close, backdrop, scrollable body. */
@Component({
  selector: 'app-modal',
  imports: [HlmDialogImports, BrnDialogContent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <hlm-dialog [state]="open() ? 'open' : 'closed'" (closed)="closed.emit()" [ariaLabel]="title()">
      <hlm-dialog-content
        *brnDialogContent
        class="gap-0 p-0 sm:max-w-none"
        [style.width]="'min(' + width() + ', 100vw - 2rem)'"
      >
        <hlm-dialog-header class="border-b px-5 py-4">
          <h2 hlmDialogTitle class="text-base font-semibold">{{ title() }}</h2>
        </hlm-dialog-header>
        <div class="max-h-[75vh] overflow-y-auto p-5">
          <ng-content />
        </div>
      </hlm-dialog-content>
    </hlm-dialog>
  `,
})
export class ModalComponent {
  readonly open = input(false);
  readonly title = input('');
  readonly width = input('32rem');
  readonly closed = output<void>();
}
