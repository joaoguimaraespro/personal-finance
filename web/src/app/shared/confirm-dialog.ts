import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { BrnAlertDialogContent } from '@spartan-ng/brain/alert-dialog';
import { HlmAlertDialogImports } from '@spartan-ng/helm/alert-dialog';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { NgIcon } from '@ng-icons/core';
import { Confirm } from '../core/confirm';
import { APP_ICONS } from './icons';

@Component({
  selector: 'app-confirm-dialog',
  imports: [NgIcon, HlmAlertDialogImports, HlmButtonImports, BrnAlertDialogContent, TranslatePipe],
  providers: [APP_ICONS],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <hlm-alert-dialog
      [state]="confirm.pending() ? 'open' : 'closed'"
      (closed)="confirm.answer(false)"
    >
      <hlm-alert-dialog-content *brnAlertDialogContent>
        @if (confirm.pending(); as req) {
          <hlm-alert-dialog-header>
            <div class="flex items-start gap-3">
              <span
                class="flex size-9 shrink-0 items-center justify-center rounded-full"
                [class]="
                  req.destructive
                    ? 'bg-rose-100 text-rose-600 dark:bg-rose-500/15 dark:text-rose-400'
                    : 'bg-primary/10 text-primary dark:bg-primary/20'
                "
                aria-hidden="true"
              >
                <ng-icon [name]="req.destructive ? 'lucideTriangleAlert' : 'lucideCircleAlert'" />
              </span>
              <h2 hlmAlertDialogTitle class="pt-1.5 text-left">{{ req.message }}</h2>
            </div>
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
              @if (req.destructive) {
                <ng-icon name="lucideTrash2" />
              }
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
