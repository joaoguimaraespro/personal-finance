import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { Prefs } from './core/prefs';
import { Toasts } from './core/toast';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <router-outlet />
    <div class="pointer-events-none fixed right-4 bottom-4 z-50 flex w-80 flex-col gap-2" aria-live="polite">
      @for (t of toasts.items(); track t.id) {
        <div
          class="pointer-events-auto flex items-center gap-3 rounded-xl px-4 py-3 text-sm shadow-lg"
          [class]="t.kind === 'error' ? 'bg-rose-600 text-white' : 'bg-slate-900 text-white dark:bg-white dark:text-slate-900'"
        >
          <span class="flex-1">{{ t.message }}</span>
          @if (t.action) {
            <button class="font-semibold underline" (click)="t.action.run(); toasts.dismiss(t.id)">{{ t.action.label }}</button>
          }
        </div>
      }
    </div>
  `,
})
export class App {
  protected readonly toasts = inject(Toasts);
  // Instantiated at startup so theme and language apply before the first screen renders.
  private readonly prefs = inject(Prefs);
}
