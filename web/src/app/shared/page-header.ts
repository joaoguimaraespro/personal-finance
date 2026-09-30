import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { NgIcon } from '@ng-icons/core';
import { APP_ICONS } from './icons';

/** Page title row: icon (same as the sidebar entry), title, optional subtitle; actions go in the content. */
@Component({
  selector: 'app-page-header',
  imports: [NgIcon],
  providers: [APP_ICONS],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'page-header' },
  template: `
    <div class="flex min-w-0 items-center gap-3">
      <span
        class="bg-primary/10 text-primary dark:bg-primary/20 flex size-10 shrink-0 items-center justify-center rounded-xl"
        aria-hidden="true"
      >
        <ng-icon [name]="icon()" class="text-xl" />
      </span>
      <div class="min-w-0">
        <h1 class="text-2xl font-semibold tracking-tight">{{ title() }}</h1>
        @if (subtitle()) {
          <p class="text-muted-foreground text-sm">{{ subtitle() }}</p>
        }
      </div>
    </div>
    <ng-content />
  `,
})
export class PageHeaderComponent {
  readonly icon = input.required<string>();
  readonly title = input.required<string>();
  readonly subtitle = input<string | null | undefined>('');
}
