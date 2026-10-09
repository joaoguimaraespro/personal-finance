import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { HlmToaster } from '@spartan-ng/helm/sonner';
import { Prefs } from './core/prefs';
import { ConfirmDialogComponent } from './shared/confirm-dialog';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, HlmToaster, ConfirmDialogComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <router-outlet />
    <app-confirm-dialog />
    <hlm-toaster
      [position]="toasterPosition"
      [theme]="toasterTheme()"
      [richColors]="true"
      [closeButton]="true"
    />
  `,
})
export class App {
  // Instantiated at startup so theme and language apply before the first screen renders.
  private readonly prefs = inject(Prefs);
  /** Phones have the navigation bar at the bottom: messages appear at the top there. */
  protected readonly toasterPosition =
    typeof window !== 'undefined' && window.matchMedia('(max-width: 767.98px)').matches
      ? ('top-center' as const)
      : ('bottom-right' as const);
  protected readonly toasterTheme = computed(() =>
    this.prefs.theme() === 'system' ? 'system' : this.prefs.theme(),
  );
}
