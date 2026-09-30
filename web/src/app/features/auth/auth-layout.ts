import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { LogoComponent } from '../../shared/logo';

@Component({
  selector: 'app-auth-layout',
  imports: [LogoComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="grid min-h-dvh place-items-center px-4 py-10">
      <div class="w-full max-w-sm">
        <div class="mb-8 flex flex-col items-center gap-3 text-center">
          <app-logo [size]="52" />
          <h1 class="text-xl font-semibold">{{ title() }}</h1>
          @if (subtitle()) {
            <p class="text-sm text-muted-foreground">{{ subtitle() }}</p>
          }
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
