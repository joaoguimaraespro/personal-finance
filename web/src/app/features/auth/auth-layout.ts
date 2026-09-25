import { ChangeDetectionStrategy, Component, input } from '@angular/core';

@Component({
  selector: 'app-auth-layout',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="grid min-h-dvh place-items-center px-4 py-10">
      <div class="w-full max-w-sm">
        <div class="mb-8 flex flex-col items-center gap-3 text-center">
          <div class="grid h-12 w-12 place-items-center rounded-2xl bg-brand-600 text-xl font-bold text-white">€</div>
          <h1 class="text-xl font-semibold">{{ title() }}</h1>
          @if (subtitle()) { <p class="text-sm text-slate-500 dark:text-slate-400">{{ subtitle() }}</p> }
        </div>
        <div class="card">
          <ng-content />
        </div>
      </div>
    </div>
  `,
})
export class AuthLayoutComponent {
  readonly title = input.required<string>();
  readonly subtitle = input('');
}
