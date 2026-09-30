import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { NgIcon } from '@ng-icons/core';
import { HlmEmptyImports } from '@spartan-ng/helm/empty';
import { APP_ICONS } from './icons';

/** Empty list/section: icon, title, short explanation and an optional action (projected content). */
@Component({
  selector: 'app-empty-state',
  imports: [NgIcon, HlmEmptyImports],
  providers: [APP_ICONS],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'block' },
  template: `
    <hlm-empty
      class="gap-4 p-6 md:p-8"
      [class.border]="bordered()"
      [class.border-dashed]="bordered()"
    >
      <hlm-empty-header>
        <hlm-empty-media variant="icon" class="bg-primary/10 text-primary dark:bg-primary/20">
          <ng-icon [name]="icon()" />
        </hlm-empty-media>
        @if (title()) {
          <div hlmEmptyTitle class="text-base">{{ title() }}</div>
        }
        @if (text()) {
          <p hlmEmptyDescription>{{ text() }}</p>
        }
      </hlm-empty-header>
      <hlm-empty-content class="empty:hidden"><ng-content /></hlm-empty-content>
    </hlm-empty>
  `,
})
export class EmptyStateComponent {
  readonly icon = input('lucideInbox');
  readonly title = input<string | null | undefined>('');
  readonly text = input<string | null | undefined>('');
  readonly bordered = input(false);
}
