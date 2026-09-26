import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { BrnAlertDialogContent } from '@spartan-ng/brain/alert-dialog';
import { HlmAlertDialogImports } from '@spartan-ng/helm/alert-dialog';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { Confirm } from '../core/confirm';

@Component({
  selector: 'app-confirm-dialog',
  imports: [HlmAlertDialogImports, HlmButtonImports, BrnAlertDialogContent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <hlm-alert-dialog
      [state]="confirm.pending() ? 'open' : 'closed'"
      (closed)="confirm.answer(false)"
    >
      <hlm-alert-dialog-content *brnAlertDialogContent>
        @if (confirm.pending(); as req) {
          <hlm-alert-dialog-header>
            <h2 hlmAlertDialogTitle>{{ req.message }}</h2>
          </hlm-alert-dialog-header>
          <hlm-alert-dialog-footer>
            <button hlmBtn variant="outline" (click)="confirm.answer(false)">
              {{ req.cancelLabel ?? ('common.cancel' | translate) }}
            </button>
            <button
              hlmBtn
              [variant]="req.destructive ? 'destructive' : 'default'"
              (click)="confirm.answer(true)"
            >
              {{ req.confirmLabel ?? ('common.confirm' | translate) }}
            </button>
          </hlm-alert-dialog-footer>
        }
      </hlm-alert-dialog-content>
    </hlm-alert-dialog>
  `,
})
export class ConfirmDialogComponent {
  protected readonly confirm = inject(Confirm);
}
